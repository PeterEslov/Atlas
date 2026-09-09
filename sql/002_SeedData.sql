/* =====================================================================
   Project Atlas — 002_SeedData.sql
   Optional demo data matching the scenario used throughout the docs:
   Northstar IT (internal) supporting ACME AB (customer), with a login
   incident raised as a ticket. Safe to run more than once — it checks
   for the seed organization before inserting anything.
   ===================================================================== */

SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Organizations WHERE Name = N'Northstar IT')
BEGIN
    DECLARE @NorthstarId UNIQUEIDENTIFIER = NEWID();
    DECLARE @AcmeId UNIQUEIDENTIFIER = NEWID();
    DECLARE @AnnaId UNIQUEIDENTIFIER = NEWID();
    DECLARE @JohnId UNIQUEIDENTIFIER = NEWID();
    DECLARE @TicketId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Now DATETIME2 = SYSUTCDATETIME();

    INSERT INTO dbo.Organizations (Id, Name, Type, IsActive, CreatedAtUtc)
    VALUES
        (@NorthstarId, N'Northstar IT', 0, 1, @Now),  -- 0 = Internal
        (@AcmeId,      N'ACME AB',      1, 1, @Now);  -- 1 = Customer

    INSERT INTO dbo.Users (Id, OrganizationId, FullName, Email, Role, IsActive, CreatedAtUtc)
    VALUES
        (@AnnaId, @NorthstarId, N'Anna Andersson', N'anna.andersson@northstar-it.example', 1, 1, @Now), -- 1 = Agent
        (@JohnId, @AcmeId,      N'John Smith',     N'john.smith@acme.example',             0, 1, @Now); -- 0 = Customer

    INSERT INTO dbo.Tickets
        (Id, Title, Description, Status, Priority, OrganizationId, ProjectId, CreatedByUserId, AssignedToUserId, DueAtUtc, CreatedAtUtc)
    VALUES
        (@TicketId,
         N'Customer cannot login',
         N'ACME reports users receive a 500 error on the login page since this morning.',
         2,                          -- 2 = InProgress
         2,                          -- 2 = High
         @AcmeId,
         NULL,
         @JohnId,
         @AnnaId,
         DATEADD(HOUR, 4, @Now),
         @Now);

    INSERT INTO dbo.TicketHistory (Id, TicketId, ChangedByUserId, FieldName, OldValue, NewValue, ChangedAtUtc, CreatedAtUtc)
    VALUES
        (NEWID(), @TicketId, @JohnId, N'Status', NULL, N'New', @Now, @Now),
        (NEWID(), @TicketId, @AnnaId, N'AssignedToUserId', NULL, CAST(@AnnaId AS NVARCHAR(36)), DATEADD(MINUTE, 8, @Now), @Now),
        (NEWID(), @TicketId, @AnnaId, N'Status', N'Open', N'InProgress', DATEADD(MINUTE, 45, @Now), @Now);

    INSERT INTO dbo.TicketComments (Id, TicketId, AuthorUserId, Body, IsInternal, CreatedAtUtc)
    VALUES
        (NEWID(), @TicketId, @AnnaId, N'Looking into it now — checking the identity provider logs.', 1, DATEADD(MINUTE, 50, @Now));

    PRINT 'Seed data inserted: Northstar IT, ACME AB, 2 users, 1 ticket (#' + CAST(@TicketId AS NVARCHAR(36)) + ').';
END
ELSE
BEGIN
    PRINT 'Seed data already present — skipped.';
END
GO
