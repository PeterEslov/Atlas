/* =====================================================================
   Project Atlas — 001_InitialSchema.sql
   Target: Azure SQL Database / SQL Server 2019+ (T-SQL)

   This script is a hand-written, human-reviewable reference for the schema
   that Atlas.Infrastructure's EF Core model (see Persistence/Configurations)
   produces. In day-to-day development you will NOT run this file directly —
   EF Core migrations are the source of truth:

       cd src/Atlas.Api
       dotnet ef migrations add InitialCreate --project ../Atlas.Infrastructure --startup-project .
       dotnet ef database update             --project ../Atlas.Infrastructure --startup-project .

   This script exists for two reasons that come up constantly in real jobs:
     1. DBAs / reviewers who want to read the schema without opening C#.
     2. A reference to restore from if migrations history is ever lost.

   It is idempotent — safe to run more than once against the same database.
   ===================================================================== */

SET NOCOUNT ON;
GO

-- =====================================================================
-- dbo.Organizations
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Organizations' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Organizations
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Organizations PRIMARY KEY,
        Name            NVARCHAR(200)    NOT NULL,
        Type            INT              NOT NULL, -- 0 = Internal, 1 = Customer
        IsActive        BIT              NOT NULL CONSTRAINT DF_Organizations_IsActive DEFAULT (1),
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL
    );

    CREATE INDEX IX_Organizations_Name ON dbo.Organizations (Name);
END
GO

-- =====================================================================
-- dbo.Users
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Users
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
        OrganizationId  UNIQUEIDENTIFIER NOT NULL,
        FullName        NVARCHAR(200)    NOT NULL,
        Email           NVARCHAR(256)    NOT NULL,
        Role            INT              NOT NULL, -- 0 Customer, 1 Agent, 2 Manager, 3 Admin
        IsActive        BIT              NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_Users_Organizations FOREIGN KEY (OrganizationId)
            REFERENCES dbo.Organizations (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email);
    CREATE INDEX IX_Users_OrganizationId ON dbo.Users (OrganizationId);
END
GO

-- =====================================================================
-- dbo.Teams
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Teams' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Teams
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Teams PRIMARY KEY,
        OrganizationId  UNIQUEIDENTIFIER NOT NULL,
        Name            NVARCHAR(200)    NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_Teams_Organizations FOREIGN KEY (OrganizationId)
            REFERENCES dbo.Organizations (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Teams_OrganizationId ON dbo.Teams (OrganizationId);
END
GO

-- =====================================================================
-- dbo.TeamMembers  (Team <-> User, many-to-many)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TeamMembers' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.TeamMembers
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TeamMembers PRIMARY KEY,
        TeamId          UNIQUEIDENTIFIER NOT NULL,
        UserId          UNIQUEIDENTIFIER NOT NULL,
        JoinedAtUtc     DATETIME2        NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_TeamMembers_Teams FOREIGN KEY (TeamId)
            REFERENCES dbo.Teams (Id) ON DELETE CASCADE,
        CONSTRAINT FK_TeamMembers_Users FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_TeamMembers_TeamId_UserId ON dbo.TeamMembers (TeamId, UserId);
END
GO

-- =====================================================================
-- dbo.Projects
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Projects' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Projects
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Projects PRIMARY KEY,
        OrganizationId  UNIQUEIDENTIFIER NOT NULL,
        Name            NVARCHAR(200)    NOT NULL,
        Description     NVARCHAR(2000)   NOT NULL CONSTRAINT DF_Projects_Description DEFAULT (''),
        IsArchived      BIT              NOT NULL CONSTRAINT DF_Projects_IsArchived DEFAULT (0),
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_Projects_Organizations FOREIGN KEY (OrganizationId)
            REFERENCES dbo.Organizations (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Projects_OrganizationId ON dbo.Projects (OrganizationId);
END
GO

-- =====================================================================
-- dbo.ProjectMembers  (Project <-> User, many-to-many)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ProjectMembers' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.ProjectMembers
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProjectMembers PRIMARY KEY,
        ProjectId       UNIQUEIDENTIFIER NOT NULL,
        UserId          UNIQUEIDENTIFIER NOT NULL,
        JoinedAtUtc     DATETIME2        NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_ProjectMembers_Projects FOREIGN KEY (ProjectId)
            REFERENCES dbo.Projects (Id) ON DELETE CASCADE,
        CONSTRAINT FK_ProjectMembers_Users FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_ProjectMembers_ProjectId_UserId ON dbo.ProjectMembers (ProjectId, UserId);
END
GO

-- =====================================================================
-- dbo.Tickets
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Tickets' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Tickets
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY,
        Title               NVARCHAR(200)    NOT NULL,
        Description         NVARCHAR(MAX)    NOT NULL,
        Status              INT              NOT NULL CONSTRAINT DF_Tickets_Status DEFAULT (0), -- 0 New
        Priority            INT              NOT NULL CONSTRAINT DF_Tickets_Priority DEFAULT (1), -- 1 Medium
        OrganizationId      UNIQUEIDENTIFIER NOT NULL,
        ProjectId           UNIQUEIDENTIFIER NULL,
        CreatedByUserId     UNIQUEIDENTIFIER NOT NULL,
        AssignedToUserId    UNIQUEIDENTIFIER NULL,
        DueAtUtc            DATETIME2        NULL,
        ResolvedAtUtc       DATETIME2        NULL,
        ClosedAtUtc         DATETIME2        NULL,
        CreatedAtUtc        DATETIME2        NOT NULL,
        ModifiedAtUtc       DATETIME2        NULL,

        CONSTRAINT FK_Tickets_Organizations FOREIGN KEY (OrganizationId)
            REFERENCES dbo.Organizations (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_Tickets_Projects FOREIGN KEY (ProjectId)
            REFERENCES dbo.Projects (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_Tickets_CreatedByUser FOREIGN KEY (CreatedByUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_Tickets_AssignedToUser FOREIGN KEY (AssignedToUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    -- The dashboard's hottest query is: WHERE Status = @status ORDER BY CreatedAtUtc DESC.
    -- Without this composite index, SQL Server does a full clustered-index scan and a
    -- separate sort once the table passes a few hundred thousand rows — exactly the
    -- "why is this slow with 10 million tickets?" exercise from Del 7. The index lets
    -- the engine seek straight to the matching rows, already in the right order.
    CREATE INDEX IX_Tickets_Status_CreatedAtUtc ON dbo.Tickets (Status, CreatedAtUtc DESC);

    CREATE INDEX IX_Tickets_OrganizationId ON dbo.Tickets (OrganizationId);
    CREATE INDEX IX_Tickets_AssignedToUserId ON dbo.Tickets (AssignedToUserId);
    CREATE INDEX IX_Tickets_DueAtUtc ON dbo.Tickets (DueAtUtc);
END
GO

-- =====================================================================
-- dbo.TicketComments
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TicketComments' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.TicketComments
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TicketComments PRIMARY KEY,
        TicketId        UNIQUEIDENTIFIER NOT NULL,
        AuthorUserId    UNIQUEIDENTIFIER NOT NULL,
        Body            NVARCHAR(4000)   NOT NULL,
        IsInternal      BIT              NOT NULL CONSTRAINT DF_TicketComments_IsInternal DEFAULT (0),
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_TicketComments_Tickets FOREIGN KEY (TicketId)
            REFERENCES dbo.Tickets (Id) ON DELETE CASCADE,
        CONSTRAINT FK_TicketComments_Users FOREIGN KEY (AuthorUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_TicketComments_TicketId ON dbo.TicketComments (TicketId);
END
GO

-- =====================================================================
-- dbo.TicketHistory
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TicketHistory' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.TicketHistory
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TicketHistory PRIMARY KEY,
        TicketId            UNIQUEIDENTIFIER NOT NULL,
        ChangedByUserId     UNIQUEIDENTIFIER NOT NULL,
        FieldName           NVARCHAR(100)    NOT NULL,
        OldValue            NVARCHAR(1000)   NULL,
        NewValue            NVARCHAR(1000)   NULL,
        ChangedAtUtc        DATETIME2        NOT NULL,
        CreatedAtUtc        DATETIME2        NOT NULL,
        ModifiedAtUtc       DATETIME2        NULL,

        CONSTRAINT FK_TicketHistory_Tickets FOREIGN KEY (TicketId)
            REFERENCES dbo.Tickets (Id) ON DELETE CASCADE,
        CONSTRAINT FK_TicketHistory_Users FOREIGN KEY (ChangedByUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_TicketHistory_TicketId_ChangedAtUtc ON dbo.TicketHistory (TicketId, ChangedAtUtc);
END
GO

-- =====================================================================
-- dbo.Tags
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Tags' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Tags
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tags PRIMARY KEY,
        OrganizationId  UNIQUEIDENTIFIER NOT NULL,
        Name            NVARCHAR(100)    NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_Tags_Organizations FOREIGN KEY (OrganizationId)
            REFERENCES dbo.Organizations (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_Tags_OrganizationId_Name ON dbo.Tags (OrganizationId, Name);
END
GO

-- =====================================================================
-- dbo.TicketTags  (Ticket <-> Tag, many-to-many)
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'TicketTags' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.TicketTags
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_TicketTags PRIMARY KEY,
        TicketId        UNIQUEIDENTIFIER NOT NULL,
        TagId           UNIQUEIDENTIFIER NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_TicketTags_Tickets FOREIGN KEY (TicketId)
            REFERENCES dbo.Tickets (Id) ON DELETE CASCADE,
        CONSTRAINT FK_TicketTags_Tags FOREIGN KEY (TagId)
            REFERENCES dbo.Tags (Id) ON DELETE NO ACTION
    );

    CREATE UNIQUE INDEX UX_TicketTags_TicketId_TagId ON dbo.TicketTags (TicketId, TagId);
END
GO

-- =====================================================================
-- dbo.Attachments
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Attachments' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Attachments
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Attachments PRIMARY KEY,
        TicketId            UNIQUEIDENTIFIER NOT NULL,
        FileName            NVARCHAR(260)    NOT NULL,
        ContentType         NVARCHAR(100)    NOT NULL,
        SizeInBytes         BIGINT           NOT NULL,
        BlobName            NVARCHAR(500)    NOT NULL, -- pointer into Azure Blob Storage, see Del 8
        UploadedByUserId    UNIQUEIDENTIFIER NOT NULL,
        CreatedAtUtc        DATETIME2        NOT NULL,
        ModifiedAtUtc       DATETIME2        NULL,

        CONSTRAINT FK_Attachments_Tickets FOREIGN KEY (TicketId)
            REFERENCES dbo.Tickets (Id) ON DELETE CASCADE,
        CONSTRAINT FK_Attachments_Users FOREIGN KEY (UploadedByUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Attachments_TicketId ON dbo.Attachments (TicketId);
END
GO

-- =====================================================================
-- dbo.Notifications
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'Notifications' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.Notifications
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
        RecipientUserId     UNIQUEIDENTIFIER NOT NULL,
        Type                INT              NOT NULL, -- see Atlas.Domain.Enums.NotificationType
        Message             NVARCHAR(1000)   NOT NULL,
        RelatedTicketId     UNIQUEIDENTIFIER NULL,
        IsRead              BIT              NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT (0),
        ReadAtUtc           DATETIME2        NULL,
        CreatedAtUtc        DATETIME2        NOT NULL,
        ModifiedAtUtc       DATETIME2        NULL,

        CONSTRAINT FK_Notifications_Users FOREIGN KEY (RecipientUserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION,
        CONSTRAINT FK_Notifications_Tickets FOREIGN KEY (RelatedTicketId)
            REFERENCES dbo.Tickets (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_Notifications_RecipientUserId_IsRead ON dbo.Notifications (RecipientUserId, IsRead);
END
GO

-- =====================================================================
-- dbo.AuditLogs
-- =====================================================================
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AuditLogs' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        UserId          UNIQUEIDENTIFIER NOT NULL,
        Action          NVARCHAR(100)    NOT NULL,
        EntityName      NVARCHAR(100)    NOT NULL,
        EntityId        UNIQUEIDENTIFIER NOT NULL,
        OldValuesJson   NVARCHAR(MAX)    NULL,
        NewValuesJson   NVARCHAR(MAX)    NULL,
        TimestampUtc    DATETIME2        NOT NULL,
        CreatedAtUtc    DATETIME2        NOT NULL,
        ModifiedAtUtc   DATETIME2        NULL,

        CONSTRAINT FK_AuditLogs_Users FOREIGN KEY (UserId)
            REFERENCES dbo.Users (Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_AuditLogs_EntityName_EntityId ON dbo.AuditLogs (EntityName, EntityId);
    CREATE INDEX IX_AuditLogs_TimestampUtc ON dbo.AuditLogs (TimestampUtc);
END
GO

PRINT 'Project Atlas — initial schema applied (or already present).';
GO
