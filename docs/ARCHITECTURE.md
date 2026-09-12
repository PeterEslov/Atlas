# Architecture notes

## Layering rules

| Project | May reference | May NOT reference |
|---|---|---|
| `Atlas.Domain` | nothing (BCL only) | EF Core, ASP.NET Core, Azure SDK, `Atlas.Application`, `Atlas.Infrastructure` |
| `Atlas.Application` | `Atlas.Domain` | EF Core, ASP.NET Core, Azure SDK, `Atlas.Infrastructure` |
| `Atlas.Infrastructure` | `Atlas.Application`, `Atlas.Domain` | `Atlas.Api` |
| `Atlas.Api` | everything | — |

If you ever find yourself wanting to `using Microsoft.EntityFrameworkCore;`
inside `Atlas.Domain` or `Atlas.Application`, that's the signal an
abstraction is missing in `Atlas.Application/Common/Interfaces` — add the
interface there and implement it in `Atlas.Infrastructure` instead.

## Why entities have private setters

Every entity in `Atlas.Domain` exposes state through get-only properties and
mutates itself only through named methods (`Ticket.AssignTo`,
`Ticket.ChangeStatus`, ...). This is what makes "a ticket can never end up
in an invalid state" an actual guarantee rather than a convention someone
has to remember to follow in every controller and service. EF Core can
still materialize these types because it writes to the backing fields /
private setters directly via reflection — that capability is exactly why
this pattern is idiomatic in EF Core-based Clean Architecture, not a hack.

## Why `Ticket` owns Comments, History, Tags and Attachments

`Ticket` is the aggregate root for the ticketing bounded context.
`TicketComment`, `TicketHistory`, `TicketTag` and `Attachment` have no
public constructor reachable from outside `Atlas.Domain` — they can only be
created through methods on `Ticket` (`AddComment`, `AddTag`,
`AddAttachment`; `TicketHistory` entries are appended internally by every
mutating method). This guarantees the invariant "every ticket state change
is recorded in its history" can't be worked around by constructing a
`TicketHistory` row directly and skipping the real transition.

## Repository shape

`ITicketRepository` is deliberately **not** a generic `IRepository<T>`.
Tickets have query needs — filtering, paging, conditional eager-loading of
Comments/History/Tags — that a one-size-fits-all abstraction only gets in
the way of. `IUserRepository`, `IOrganizationRepository` (Del 6), and
`IProjectRepository`/`ITeamRepository` (Del 7) all follow the same
non-generic shape. `IProjectRepository` and `ITeamRepository` also introduce
a pattern the earlier repositories didn't need: `GetMemberCountsAsync` runs
one `GROUP BY` query for a whole page of ids, rather than one `COUNT(*)` per
row (which is what `OrganizationRepository.GetChildCountsAsync` does — fine
for a single organization, but a textbook N+1 if repeated once per row of a
25-row page).

`ITicketRepository.GetOrCreateTagAsync`/`FindTagByNameAsync` are the other
notable repository-level pattern: `TicketsController`'s tag endpoints take a
tag *name* in the request body, not a tag id, so there's no separate
"create the tag first" step for API callers. Adding a tag get-or-creates the
`Tag` row (normalized via `Tag.Create`'s `.Trim().ToLowerInvariant()`, so
`"Billing"` and `"billing"` resolve to the same row); removing one only
looks the row up — it deliberately does *not* create a tag just to
immediately not-apply it.

## Ticket workflows (closed out alongside Del 7)

`Ticket.Reopen`, `Ticket.AddTag` and `Ticket.RemoveTag` existed as domain
methods before they had endpoints — they were written when `Ticket` itself
was built, but wiring them through `ITicketService`/`TicketsController` was
deferred. That gap is what the roadmap called "richer ticket workflows" and
is why Fas 2 stayed yellow after Del 7's Project/Team work was otherwise
done. It's now closed: `POST /api/tickets/{id}/reopen`,
`POST /api/tickets/{id}/tags`, `POST /api/tickets/{id}/tags/remove`, a
`projectId` filter on `GET /api/tickets`, and a real
`DELETE /api/tickets/{id}` (the endpoint `Permissions.TicketDelete` was
defined and granted for, but previously had nothing behind it) all exist
now. `DELETE /api/tickets/{id}` is a genuine hard delete — cascade-deleting
Comments/History/Tags/Attachments with it — which is a real tradeoff
against the soft-cancel path (`POST .../status` with `Cancelled`, which
keeps the full audit trail); see the doc comment on
`TicketService.DeleteAsync` for the reasoning.

## Del 8 — Azure App Service deployment

Three small but easy-to-miss decisions worth recording, all in `Program.cs`:

- **Swagger UI's availability is a config switch (`EnableSwaggerUi`), not an
  `app.Environment.IsDevelopment()` check.** The two used to be the same
  condition, which was fine when "Development" only ever meant "running
  locally." Once the app also runs in Azure, that stops being true: turning
  Swagger on for portfolio/demo viewing there must not also turn on the
  auto-migrate-on-startup block, which stays hard-tied to
  `IsDevelopment()` specifically so a config change alone can never trigger
  it against a real database. Two independent knobs, not one overloaded one.
- **`ForwardedHeadersOptions` + `app.UseForwardedHeaders()`.** Azure App
  Service terminates TLS at its own edge and forwards requests to the app as
  plain HTTP internally. Without telling the app to trust the
  `X-Forwarded-Proto`/`X-Forwarded-For` headers App Service sets,
  `app.UseHttpsRedirection()` sees every request as HTTP and keeps
  redirecting an already-HTTPS client back to `http://` — App Service
  upgrades it again, and the browser loops. `KnownNetworks`/`KnownProxies`
  are cleared because App Service's proxy isn't a fixed IP this app can pin
  down in config, unlike a self-hosted reverse proxy would be.
- **`MapHealthChecks("/health")` is deliberately not wired to the
  database.** It backs Azure App Service's own "Health check" feature (pings
  the path, pulls an unhealthy instance out of rotation), and it has to work
  from the moment Del 8 deploys — which is before Del 9 gives it a cloud
  database to check. `AddDbContextCheck<AtlasDbContext>()` is the natural
  follow-up once there's something real behind it.

**Key Vault was pulled forward from Del 20 into Del 8** for one practical
reason: Peter's subscription already has one. Building the "insecure
intermediate" version — the JWT signing key as a plain-text App Service
Application Setting — only to redo it as a Key Vault secret once Del 20
formally arrived would have been pure throwaway work, so `Program.cs` reads
`KeyVault:Name` and, if set, layers `AddAzureKeyVault(..., new
DefaultAzureCredential())` onto configuration before anything else is bound.
Two details worth knowing if you're extending this: App Service authenticates
to the vault via a **system-assigned managed identity**, not a stored
credential of any kind — there's no chicken-and-egg secret needed to reach
the secrets. And Key Vault secret names can't contain underscores (only
letters, digits and hyphens), so the naming convention there is `--`
(`Jwt--SigningKey`), not the `__` used for plain environment-variable-backed
settings (`Jwt__Issuer`) — the configuration provider maps both to the same
kind of nested key (`Jwt:SigningKey`, `Jwt:Issuer`), just via two different
separators. Del 9's `ConnectionStrings:AtlasDb` will follow the same Key
Vault pattern once it exists. See `docs/AZURE_DEPLOYMENT.md` section 3 for
the exact commands (managed identity, role assignment, secret creation).

**Confirmed working end-to-end against the live subscription on 2026-09-10**:
Key Vault role assignment, pipeline run (build, test, `AzureWebApp@1` ZIP
deploy), and `GET /health` returning `Healthy` from
`https://app-projectatlas-dev-sc.azurewebsites.net/health`. The one hiccup
along the way was environmental, not a design flaw: Git Bash (MSYS) on
Windows silently rewrites a leading-slash argument like
`--scope "$VAULT_ID"` into a mangled Windows-style path before Azure CLI
ever sees it, producing a `(MissingSubscription)` error that has nothing to
do with the subscription itself — `export MSYS_NO_PATHCONV=1` fixes it; see
`docs/AZURE_DEPLOYMENT.md` section 0 for the full explanation.

Docker was deliberately **not** introduced here even though App Service
supports container deployment — Del 17 ("Docker — containerisering av
API:et", Phase 5) owns that scope. Del 8 deploys the plain `dotnet publish`
output instead, the same way `dotnet run` already does locally.

The CI/CD pipeline runs on **Azure Pipelines** (Azure DevOps), not GitHub
Actions — Peter's source repo lives in Azure Repos. It authenticates to
Azure via **workload identity federation**, the same no-stored-secret idea
as GitHub's OIDC login, but set up differently: because Azure DevOps is a
first-party Microsoft service (unlike GitHub, which Azure only trusts once
you manually register the relationship via
`az ad app federated-credential create`), the whole thing is one guided step
in the Azure DevOps portal — an Azure Resource Manager service connection
with "Workload Identity federation (automatic)", scoped to the resource
group, not the subscription. It creates the app registration, service
principal, and federated credential together; `azure-pipelines.yml`
references the result by its service connection *name* only — no
client-id/tenant-id/subscription-id values live in the pipeline definition
at all, which is even less than GitHub's Variables-not-Secrets approach
needed. See `docs/AZURE_DEPLOYMENT.md` section 5 for the exact steps.

**Del 9 — Azure SQL, reusing an existing server.** Same reuse-what's-already-
there principle as Key Vault: Peter had a SQL logical server in his
subscription already, so Del 9 provisions a new, empty database on it
(serverless General Purpose with Azure's free monthly limit — 100,000
vCore-seconds, auto-pause when idle) rather than standing up a whole new
server. The connection string follows `Jwt--SigningKey`'s exact pattern into
Key Vault as `ConnectionStrings--AtlasDb` — no new App Setting needed, since
`KeyVault:Name` was already wired in Del 8. `DependencyInjection.cs`'s
`sqlOptions.EnableRetryOnFailure(...)` — present since Fas 1, well before
there was any cloud database to need it — earned its keep here: Azure SQL
sees transient faults (a serverless database waking from auto-pause, a
brief network blip) that a local SQL Server instance essentially never does.

**Confirmed working end-to-end against the live subscription on 2026-09-10**:
`POST /api/auth/register` through App Service returns a signed JWT with the
correct claims and permission set. Getting there surfaced one genuine bug,
not an Azure quirk: `sql/001_InitialSchema.sql` — the hand-written schema
reference this doc's own header warns not to run directly — had quietly
drifted out of sync with the EF model since Del 5 added `User.PasswordHash`;
nobody had touched the file since. It got run against the fresh Azure SQL
database anyway (easy mistake — the Query editor conflates "run some SQL
here" with "run the *right* SQL here"), producing a `Users` table missing
that column and a 500 with no useful detail (the exception-hiding
middleware doing exactly its job — `az webapp log tail` was what actually
showed `Invalid column name 'PasswordHash'`). The fix was two-part: correct
the reference file itself (now matches the migration), and rebuild the
Azure SQL schema purely from `dotnet ef database update` — dropping every
foreign key and table first (a short dynamic script over `sys.foreign_keys`/
`sys.tables`, since hand-ordering 14 tables' drop order isn't worth doing
twice) rather than trying to patch the drifted schema column-by-column.
That's also where a second, smaller version of the Del 8 MSYS lesson showed
up again: `SQL_SERVER_NAME` unset in a fresh terminal window produced a
connection string like `Server=tcp:.database.windows.net,...` — a host that
plainly can't resolve — the same "echo the value before you trust it in the
next command" discipline that `MSYS_NO_PATHCONV` debugging already taught.

**Del 10 — Azure Blob Storage, attachments.** A third instance of the same
managed-identity-over-stored-secret shape as Key Vault (Del 8) and Azure SQL
(Del 9), but this time reusing nothing: a brand-new Storage account, since
knowing exactly which permissions the account needs is simpler when it only
ever holds this project's attachments. `IBlobStorageService`
(Atlas.Application) is implemented by `AzureBlobStorageService`
(Atlas.Infrastructure) against a `BlobContainerClient` that's constructed one
of two ways — `BlobStorage:ConnectionString` (Azurite, local dev) or
`BlobStorage:AccountUrl` + `DefaultAzureCredential` (Azure, via the App
Service's managed identity granted "Storage Blob Data Contributor" RBAC on
the account) — exactly the dual-path shape `ConnectionStrings:AtlasDb`
already has. Unlike the SQL connection string and the JWT signing key, the
account URL isn't itself a secret (nothing can be done with it without an
Azure AD identity Azure also trusts), so it's a plain App Setting
(`BlobStorage__AccountUrl`), the same reasoning `KeyVault:Name` already
follows — not every piece of cloud configuration needs Key Vault, only the
pieces that are actually secrets. `AddInfrastructure` also calls
`blobContainerClient.CreateIfNotExists(PublicAccessType.None)`
synchronously at startup, the same fail-fast idea as the `Jwt:SigningKey`
null-check in `Program.cs`: a missing or unreachable container should crash
the app immediately with a clear error, not serve requests in a half-working
state. `TicketService.AddAttachmentAsync` uploads the blob *before* creating
the `Attachment` row (with a best-effort compensating blob delete if the
DB write then fails) rather than the other order, because an orphaned blob
nobody points to is a far cheaper failure than a DB row whose `BlobName`
points at nothing — a broken download for a real user.

Getting there surfaced a genuine EF Core bug, not an Azure one:
`TicketService.AddAttachmentAsync` deterministically threw
`DbUpdateConcurrencyException: expected to affect 1 row(s), but actually
affected 0 row(s)` on every attachment upload, both locally and later in
Azure. The SQL Peter captured from the console log showed why — EF Core had
generated an `UPDATE` for the brand-new `Attachment` row instead of an
`INSERT`. Every other child entity on `Ticket` (`TicketComment`,
`TicketHistory`, `TicketTag`) is added purely by appending to the
aggregate's own in-memory collection and letting EF's change tracker infer
`EntityState.Added` for the newly-discovered row via `DetectChanges()` — no
explicit `Add()` call anywhere. That inference is inherently a guess for an
entity with a client-generated key (every entity here uses a `Guid` set in
its constructor, not a database-generated one): EF has to decide whether a
newly-discovered graph member is a brand-new row or an existing one just
being reattached, and for `Attachment` — the one call path with an `await`
to an external system (the blob upload) sitting between loading the tracked
`Ticket` and saving — that guess came out wrong. The fix was to stop relying
on the guess: `TicketRepository` gained an explicit
`AddAttachmentAsync(Attachment)` that calls
`_dbContext.Attachments.AddAsync(...)` directly, the same explicit-staging
pattern `Ticket` (`TicketRepository.AddAsync`) and `Tag`
(`GetOrCreateTagAsync`) already used — removing the ambiguity outright
rather than depending on it resolving correctly by chance.

A second, unrelated-to-EF lesson showed up right after: the Azure Pipelines
build broke with `error CS0246: The type or namespace name 'AttachmentDto'
could not be found`, even though everything built and ran fine locally.
Cause: `TicketsController.cs` (referencing `AttachmentDto`) had already been
committed and pushed in an earlier commit, but `TicketDtos.cs` (defining it)
had only ever been written to Peter's working copy — never `git add`ded,
so it sat as an uncommitted local change while CI cloned a fresh copy of
`origin/main` that simply didn't have it yet. A clean illustration of why
CI exists at all: a local build only ever proves the code on disk compiles,
never that what's actually shared (committed *and* pushed) does. Committing
the full batch of pending Del 10 files fixed it — after first excluding
`.azurite/` (Azurite's own local emulator data, now `.gitignore`d, the same
"don't commit runtime state" reasoning as a LocalDB `.mdf` file) and a stray
manual test-download artifact that had ended up inside `src/Atlas.Api/`.

**Confirmed working end-to-end against the live subscription on
2026-09-11**: `POST /api/tickets/{id}/attachments` and the matching
`GET .../download` both round-tripped a real file through App Service →
managed identity → the Storage account, verified locally against Azurite
first and then again against Azure. Getting a token for the Azure
verification also surfaced a one-off, self-resolving hiccup worth knowing
about rather than fixing: `POST /api/auth/login` 500'd on the first attempt
against a database that had been sitting idle since Del 9's testing, then
succeeded immediately on retry — consistent with Azure SQL serverless
auto-pause needing a few seconds to wake the database on first contact,
longer than whatever timeout tripped first. `EnableRetryOnFailure` doesn't
help here specifically because a cold-start delay isn't the kind of
mid-operation transient fault it's built to retry — nothing to fix, just
the cost of `--auto-pause-delay 60` doing its job of not billing for an idle
database.

**Del 11 — a background worker for overdue-ticket notifications.** The
first Del in Fas 4 ("Enterprise"), and the first time anything in this
solution runs as a second, separate process alongside `Atlas.Api`:
`Atlas.Worker`, a plain .NET Generic Host (`Microsoft.NET.Sdk.Worker`,
`Host.CreateApplicationBuilder`) rather than an ASP.NET Core host — no HTTP
surface at all, just a `BackgroundService` that polls every 5 minutes for
tickets that have become overdue and turns each one into a `Notification`
row, the first code path to actually write to the `Notifications` table,
which has existed in the schema since Fas 1 but sat unused until now.

Having a second host process forced a real architectural question:
`DependencyInjection.cs`'s `AddInfrastructure()` was, until now, a single
monolithic method — fine for `Atlas.Api`, which genuinely needs persistence,
Blob Storage and JWT auth all at once, but wrong for `Atlas.Worker`, which
only ever reads tickets and writes notifications. Forcing the worker through
the same `AddInfrastructure()` would have meant it inherited Blob Storage's
and JWT's fail-fast startup checks (Del 10's `CreateIfNotExists` call, the
`Jwt:SigningKey` length check in `Program.cs`) for two systems it never
touches — it would refuse to start without `BlobStorage:*`/`Jwt:*`
configuration it has no use for. The fix: split `AddInfrastructure()` into
three composable pieces — `AddPersistence`, `AddBlobStorage`,
`AddAuthInfrastructure` — with `AddInfrastructure()` itself now just calling
all three in order (so `Atlas.Api`'s `Program.cs` needed no changes at all),
while `Atlas.Worker`'s `Program.cs` calls only `AddPersistence`.

Finding overdue tickets reuses the exact same business definition as
`Ticket.IsOverdue` and the UI's `TicketListQuery.OverdueOnly` filter (a past
`DueAtUtc`, not `Resolved`/`Closed`/`Cancelled`, assigned to someone — an
unassigned ticket has nobody to notify), but as a dedicated, non-paginated
`ITicketRepository.GetOverdueAsync()` rather than reusing `SearchAsync`:
`TicketService.Normalize()` caps `SearchAsync` at `MaxPageSize = 100`,
appropriate for a UI page but wrong for a batch job that needs the
*complete* overdue set every time, not one page of it. Avoiding a duplicate
reminder on every 5-minute tick for the same overdue ticket is a single
batch existence check — `INotificationRepository.GetNotifiedTicketIdsAsync
(NotificationType.TicketOverdue, ticketIds)` — the same N+1-avoidance shape
as Del 7's `GetMemberCountsAsync`, run once per tick rather than once per
ticket.

`OverdueTicketWorker : BackgroundService` is a singleton that lives for the
whole process, but `AtlasDbContext` is scoped and must never be shared
across concurrent or long-lived operations — the same reason a web request
gets its own DI scope in `Atlas.Api`. `IServiceScopeFactory` is the standard
bridge: every tick creates its own scope (and therefore its own
`DbContext`), does its work, and disposes it — a fresh scope every 5 minutes
rather than one scope for the process's entire lifetime. A single failed
tick is caught and logged rather than allowed to crash the whole worker —
the same "keep going" philosophy any long-running service needs, since the
alternative is the entire background service going dark until someone
notices and restarts it by hand.

Getting `Atlas.Worker` running at all surfaced a build-template gotcha, not
a logic bug: the `Microsoft.NET.Sdk.Worker` project template defaults to
`<InvariantGlobalization>true</InvariantGlobalization>` — a reasonable
default for a worker aimed at a minimal container image, since it strips out
ICU's culture-specific data (date formats, sorting, collation) to shrink the
runtime. But `Microsoft.Data.SqlClient` genuinely needs that data internally
for the TDS login handshake's collation negotiation, and fails outright with
`System.NotSupportedException: Globalization Invariant Mode is not
supported` the moment it tries to open a connection — before even attempting
authentication. `Atlas.Api.csproj` never had this setting (the ASP.NET Core
Web template doesn't default to it), which is why the exact same
`ConnectionStrings:AtlasDb` value had worked there without incident all
along. The fix was simply removing the line.

That's also where a second, deeper bug came out — and where the Del 10
`DbUpdateConcurrencyException` story above needs a correction. `Ticket.
AssignTo(...)` threw the identical `DbUpdateConcurrencyException: expected
to affect 1 row(s), but actually affected 0 row(s)` as Del 10's `Attachment`
bug, but this time for `TicketHistory` (`_history.Add(...)` inside
`AssignTo`) — and with no `await` anywhere between loading the tracked
`Ticket` and calling `SaveChangesAsync`, which was the entire distinguishing
factor Del 10's fix leaned on. That theory turns out to have been
incomplete: the real root cause is that *every* entity in this model has a
`Guid` key assigned client-side (`Guid.NewGuid()` in the constructor), with
no explicit EF Core configuration saying so. Left unconfigured, EF Core's
default convention for a `Guid` primary key is still "this *could* be
store-generated" (`ValueGeneratedOnAdd`). The moment a new child entity is
discovered through the change tracker rather than an explicit `Add()` call —
appending to an already-tracked, previously-loaded parent's collection, with
or without an intervening `await` — EF sees a non-default key value on an
entity it has never tracked before and, since that kind of key *could* be
store-generated, assumes the row must already exist and marks it `Modified`
instead of `Added`. An `UPDATE` gets generated against a row that was never
inserted; zero rows match; `DbUpdateConcurrencyException`. Del 10's explicit
`AddAttachmentAsync` fix was a correct, narrow workaround for `Attachment`
specifically, but it left the identical latent bug sitting in
`TicketComment`, `TicketTag`, `TicketHistory` and `Notification` — nobody
had exercised those paths against a real database yet to find out.

The actual fix, in `AtlasDbContext.OnModelCreating`, is a single loop over
every entity type in the model that marks any `Guid`-typed `Id` property
`ValueGeneratedNever()` — telling EF Core the truth, once, for the whole
model, rather than re-discovering and patching this one call site at a time
as each new domain method happens to get its first live test. It doesn't
change the database schema at all (SQL Server's `uniqueidentifier` columns
here were never store-generated via a `DEFAULT` constraint — the ambiguity
was purely in EF's own bookkeeping), so no new EF Core migration was needed.

**Confirmed working end-to-end locally on 2026-09-11**: a ticket created
with a past `DueAtUtc` and then assigned (via `POST
/api/tickets/{id}/assign`) showed up in `Atlas.Worker`'s very next tick as
`"N ticket(s) overdue, 1 new notification(s) created"`, with a matching
`Notifications` row confirmed via `sqlcmd`; a second tick against the same,
now-already-notified ticket correctly created zero new rows — both the
create path and the duplicate-prevention path proven in the same run.
`azure-pipelines.yml` needed no changes: its `dotnet restore`/`build`/`test`
steps aren't scoped to a single project, so they already pick up
`Atlas.Worker` via `ProjectAtlas.sln`; `dotnet publish`/the deploy stage stay
scoped to `Atlas.Api` only, so nothing about how the worker would actually
run in Azure (a Container App Job? a WebJob? Functions with a timer
trigger? an always-on App Service?) has been decided yet — deliberately
deferred, the same way Del 20 remains open, rather than guessed at now.

**Del 12 — Azure Service Bus, real-time TicketAssigned notifications.** The
second Del in Fas 4, and the first time two of this solution's own processes
talk to each other directly rather than only through the shared `AtlasDb`
database: `Atlas.Api` publishes a `TicketAssignedEvent` to a Service Bus
**topic** (`atlas-ticket-events`) the moment `TicketService.AssignAsync`
successfully assigns a ticket, and `Atlas.Worker` — already running as a
second host process since Del 11 — now also runs a `TicketAssignedConsumer :
BackgroundService` that subscribes to it (via the `atlas-notifications`
**subscription** on that topic) and turns each event into a `Notification`
row, a second notification-producing path alongside Del 11's overdue-ticket
check.

A topic-and-subscription, not a queue, on purpose: a queue delivers each
message to exactly one competing receiver, the right shape when there is one
kind of consumer doing one job. A topic instead lets any number of
independent subscriptions each receive their own full copy of every
message — the right shape here because `TicketAssigned` is a fact about the
world ("this happened"), not a work item for one specific handler, and a
future second subscriber (an email-notification service, an analytics
pipeline) should be able to start listening without `Atlas.Api` changing
anything about how or where it publishes.

`ITicketEventPublisher`/`ServiceBusTicketEventPublisher`
(`Atlas.Infrastructure/Messaging`) follow the same "narrow, purpose-built
interface" rule `ITicketRepository`'s own doc comment established — one
method scoped to `TicketAssignedEvent` specifically, not a generic
`PublishAsync<T>(topic, message)` guessed at before there's a second real
event type to shape it against. `TicketService.AssignAsync` calls it *after*
`SaveChangesAsync` has already committed the assignment, and the publish
itself is wrapped in a try/catch that logs a warning and swallows any
failure — a Service Bus hiccup must never turn an already-successful
assignment into a failed HTTP response for the caller. There is no outbox
pattern guaranteeing the event eventually gets published if the process
crashes in the narrow window between the DB commit and the publish call — a
known, deliberate gap, not an oversight, the same class of tradeoff Del 10
already accepted for the orphaned-blob case.

Each `TicketAssignedEvent`'s `EventId` (generated once per publish attempt,
not once per SDK-internal retry) doubles as the Service Bus message's
`MessageId`, so the topic's duplicate-detection window (enabled with
`--enable-duplicate-detection true
--duplicate-detection-history-time-window PT10M` when the topic is created)
recognizes a sender-side retry of the *same* publish attempt as a duplicate.
It does not, and cannot, protect against the *consumer* processing the same
message twice — Service Bus is at-least-once delivery, not exactly-once —
see `TicketAssignedConsumer`'s own doc comment for why that stays an open,
documented gap rather than something Del 12 closes. In practice this
distinction surfaced immediately during testing, in the most mundane way
possible: two manual calls to `POST /api/tickets/{id}/assign` against the
same ticket (a genuine retry by the person testing it, not a network-level
one) correctly produced two separate `Notification` rows, since each call is
a real, distinct assignment with its own `EventId` — a useful reminder that
duplicate detection and "don't do the same real-world thing twice" are
different guarantees, and only the first one is Service Bus's job.

`DependencyInjection.cs` gained a fourth composable piece, `AddMessaging` —
unlike `AddBlobStorage`/`AddAuthInfrastructure` (Atlas.Api only), both
`Atlas.Api` (publishing) and `Atlas.Worker` (consuming) call it, so
`AddInfrastructure()` (still just chaining all four) needed no changes, and
`Atlas.Worker`'s `Program.cs` picked up one new line. It registers a
singleton `ServiceBusClient` — the SDK explicitly documents this type as
safe, and intended, to share for the app's whole lifetime, the same
justification `BlobContainerClient` already has (Del 10) — and, unlike every
other external dependency in this project, there is no local-emulator branch
at all: SQL got LocalDB and Blob Storage got Azurite (Del 10), but Service
Bus has no first-party local emulator, so Del 12 deliberately uses the
*same* real Azure namespace for local development that production will use
(`nspl-sb-core-dev-sc`, Peter's existing Standard-tier namespace — reusing
it rather than provisioning a new one costs nothing extra, since Service
Bus's ~$10/month fee is per namespace, not per topic). That makes this the
first piece of infrastructure in the whole project where
`DefaultAzureCredential` gets exercised locally at all — SQL uses a plain
connection string, Blob Storage uses Azurite's fixed well-known key, and Key
Vault isn't touched by `Atlas.Worker` at all.

Getting that working locally surfaced a genuine `Azure.Identity` gotcha, not
a Service Bus one: every local run failed with `AuthenticationFailedException:
ManagedIdentityCredential authentication failed: ... 169.254.169.254 ...`,
even after the right RBAC role (`Azure Service Bus Data Owner`, granted to
Peter's own Azure AD identity via `az login`) was already in place.
`DefaultAzureCredential` tries a fixed chain of credential sources and only
moves on to the next one when a source throws
`CredentialUnavailableException` — its "I don't apply here" signal. Locally
there genuinely is no managed identity, so `ManagedIdentityCredential`
*should* say exactly that and let the chain fall through to
`AzureCliCredential` (the one that would actually work, via `az login`). In
practice, its IMDS probe tries to reach 169.254.169.254 — an address that
only resolves inside a real Azure VM/App Service — and on a machine where
that address is genuinely unreachable rather than merely refused, the probe
exhausts its retries and throws `AuthenticationFailedException` instead,
which `DefaultAzureCredential` treats as a hard stop for the *entire* chain
rather than "try the next source": `AzureCliCredential` never even gets a
turn. The fix, in `AddMessaging`: a config-driven
`ServiceBus:ExcludeManagedIdentityCredential` flag, `true` only in
`appsettings.Development.json` — the same "a plain config key, not an
environment-name check, decides the local/Azure difference" idiom
`AddBlobStorage` already uses for `ConnectionString` vs. `AccountUrl`. With
it excluded locally, `AzureCliCredential` gets its turn and everything
works; in Azure, where the flag is absent, `ManagedIdentityCredential` stays
in the chain for when `Atlas.Api`'s own managed identity is eventually
granted a Service Bus role there too (not yet done — see "Current known
simplifications" below).

**Confirmed working end-to-end locally on 2026-09-11**: `POST
/api/tickets/{id}/assign` published a `TicketAssignedEvent`,
`Atlas.Worker`'s `TicketAssignedConsumer` received it and wrote a matching
`Notification` row (`Type = TicketAssigned`), verified via `sqlcmd` —
alongside the mundane duplicate-notification finding described above, which
turned out to demonstrate correct behaviour rather than a bug.

**Del 13 — Redis, a cache-aside dashboard query.** The roadmap only ever said
"Redis — cache for dashboard queries", but no dashboard query existed yet —
so Del 13 introduces one: `GET /api/tickets/stats`, aggregate ticket counts
by status, by priority, and how many are overdue, for the caller's own
organization. It's exactly the kind of query a dashboard calls repeatedly and
that's expensive enough (three `GROUP BY`/`COUNT` queries against `Tickets`)
to be worth caching, rather than caching something that already existed
purely to have something to cache.

`TicketStatsDto` (`Atlas.Application/Tickets/Dtos`) is deliberately flat —
named counts (`OpenCount`, `HighPriorityCount`, ...) rather than a
`Dictionary<TicketStatus,int>` — so a dashboard, curl, or Swagger reads
named fields directly, with no enum-keyed dictionary to unpack. `GetStatsAsync`
lands on `ITicketRepository` (`TicketRepository`'s implementation runs three
small aggregate queries server-side, never loading every ticket into memory
to count in C#) the same way `ITicketRepository`'s own doc comment already
argues against a generic `IRepository<T>`: `ITicketStatsCache`
(`Atlas.Application/Common/Interfaces`) follows the identical shape — one
narrow interface (`GetAsync`/`SetAsync`/`InvalidateAsync`, one entry per
organization) rather than a generic `ICache<TKey,TValue>` that would just
push key-naming, serialization and TTL decisions out to every call site.

`TicketService.GetStatsAsync` is the cache-aside logic itself: check Redis
first; on a hit, return it without ever touching Azure SQL; on a miss
(nothing cached, the TTL expired, or Redis itself is unreachable —
`ITicketStatsCache.GetAsync` treats all three identically), fall through to
the real query and populate the cache before returning. The more interesting
design work was deciding, precisely, which of `TicketService`'s existing
mutating methods need to evict that cache: `CreateAsync`, `ChangeStatusAsync`,
`ChangePriorityAsync`, `ReopenAsync` and `DeleteAsync` all do (each changes a
counted dimension — a status/priority bucket, or `TotalCount` itself), while
`AssignAsync` deliberately does **not** — who a ticket is assigned to isn't
one of `TicketStatsDto`'s counted fields, so invalidating there would just be
extra Redis round-trips for a write the cached snapshot was never wrong
about. Getting this right, method by method, is the actual skill in cache
invalidation — "invalidate on every write, just in case" would have been
easier to write and wrong to reach for.

A short TTL (`Redis:StatsCacheTtlSeconds`, 30 by default) sits on top of that
explicit invalidation, and isn't belt-and-braces redundancy: it's a backstop
for two things invalidation can't fix — a Redis call that itself fails at
invalidation time, and `OverdueCount`, which drifts stale purely from the
passage of time, with no ticket write at all to hook an eviction onto.

`RedisTicketStatsCache` (`Atlas.Infrastructure/Caching`) is built on
`IDistributedCache` — the ASP.NET Core abstraction over "an external
key/value store with TTLs" — registered via `AddStackExchangeRedisCache`
(`DependencyInjection.AddCaching`), rather than talking to
StackExchange.Redis's own richer client API directly; a single get/set/
remove-by-key store per organization is all this needs. Every method in it
is **fail open**, the same principle Del 12's Service Bus publish already
established in `TicketService.AssignAsync`: a Redis outage degrades this
feature back to "every request hits the database," logged as a warning,
never a 500 for a read that the database could have answered perfectly well
on its own.

Unlike Del 12's Service Bus (no first-party local emulator, so local
development there deliberately talks to the same real Azure namespace
production uses), Redis has a genuine local option: `docker run -p
6379:6379 redis` speaks the exact same RESP wire protocol a real Redis
server does. So, like LocalDB (SQL) and Azurite (Blob Storage) before it,
local development here runs against something real rather than a fake or
production itself — `Redis:ConnectionString` means `localhost:6379` locally
and an Azure Cache for Redis connection string (with its access key baked
in — see "Current known simplifications" below for why there's no
managed-identity path here yet) in Azure, the same one-key-does-both-
environments idiom `BlobStorage:ConnectionString`/`AccountUrl` already uses.

**Confirmed working end-to-end locally on 2026-09-12**, via
`scripts/test-del13-redis-cache.sh` against a running `Atlas.Api` and a
local Redis container: two rapid calls to `GET /api/tickets/stats` returned
an identical `generatedAtUtc` (a genuine cache hit); a status change
produced a new `generatedAtUtc` immediately, without waiting out the TTL (a
genuine invalidation); adding a comment left `generatedAtUtc` unchanged (the
negative case — a write that correctly does *not* invalidate); and stopping
the Redis container entirely still returned `200 OK` with fresh data instead
of a `500` (fail-open, proven by actually killing the dependency rather than
just reading the code).

## Current known simplifications (by design)

- Azure SQL (Del 9) reuses Peter's existing SQL login for the moment — the
  Key Vault secret holds the server admin's own credentials rather than a
  new login scoped to just `AtlasDb`. Creating that narrower login needs a
  T-SQL client against the server (`CREATE LOGIN`/`CREATE USER`, not an
  `az` command), which is why it's deferred rather than done on the spot —
  a good candidate for Del 20 when it generalizes the Key Vault setup.
- `actorUserId`/`createdByUserId` query parameters are gone (Del 5) — every
  action now reads the caller's id from the JWT's `sub` claim via
  `ICurrentUserService`. What's still missing: no token revocation or
  refresh-token flow, so a deactivated user's existing JWT keeps working
  until it naturally expires (`Jwt:ExpiryMinutes`, 60 by default today).
- Self-registration (`POST /api/auth/register`) lets the caller pick *any*
  role, including Admin, and any existing `organizationId` — a deliberate
  dev/demo convenience (see the doc comment on `RegisterRequest`) that a
  real deployment would replace with an invite flow or a
  default-to-Customer policy before going anywhere near production.
- `Notification` has been written to since Del 11 (the overdue-ticket
  background worker) and now Del 12 (`TicketAssigned` events via Service
  Bus); `AuditLog` still has nothing writing to it — a good candidate for a
  future Del.
- `TicketAssignedConsumer` (Del 12) has no protection against Service Bus's
  at-least-once delivery redelivering the same message and creating a
  second `Notification` row — closing that gap would mean keying off each
  event's `EventId` (not currently stored on `Notification`), the same kind
  of guard `GetNotifiedTicketIdsAsync` already gives Del 11's overdue-ticket
  path against its own, different kind of duplicate.
- Del 12's Service Bus RBAC role (`Azure Service Bus Data Owner`) is granted
  to Peter's own Azure AD identity for local development only —
  `Atlas.Api`'s Azure App Service managed identity has not yet been granted
  a Service Bus role there, so publishing from the deployed API doesn't work
  yet.
- `Atlas.Worker` (Del 11) only runs locally so far — it isn't deployed
  anywhere in Azure yet. *How* it should run there (a Container App Job on a
  schedule, a WebJob alongside the existing App Service, Azure Functions with
  a timer trigger, or its own always-on App Service) is a deliberate open
  question, not an oversight — the same "get it right locally first, decide
  the Azure hosting shape as its own step" order Del 8/9/10 already followed
  for `Atlas.Api` itself. Del 12's Service Bus consumer inherits the same
  open question, since it lives inside `Atlas.Worker`.
- Del 13's Redis cache has no managed-identity path at all, unlike Blob
  Storage (Del 10) and Service Bus (Del 12): classic (non-Enterprise) Azure
  Cache for Redis only supports key-based authentication, so
  `Redis:ConnectionString` genuinely is a secret in Azure — a Key Vault
  entry, not a plain App Setting, unlike `BlobStorage:AccountUrl`/
  `ServiceBus:FullyQualifiedNamespace` — and no Azure Cache for Redis
  instance has been provisioned yet at all (see `docs/AZURE_DEPLOYMENT.md`
  section 10 for the plan). Del 13 is confirmed working only locally, against
  a Docker Redis container, so far.
- Ticket hard-delete (`TicketService.DeleteAsync`) cascades `Attachment` rows
  away via the configured `OnDelete(DeleteBehavior.Cascade)`, but doesn't
  clean up the matching blobs in Storage the other direction — a theoretical
  mirror image of the orphaned-blob case `AddAttachmentAsync`'s compensating
  delete already guards against (see Del 10 above), just not something
  normal application use triggers today. `DownloadAttachmentAsync` logs a
  warning and returns 404 if it ever encounters a DB row with no matching
  blob, rather than throwing — there is genuinely nothing to download.
- The JWT signing key and the Azure SQL connection string both live in
  `appsettings.Development.json` / user-secrets locally, and as Azure Key
  Vault secrets once deployed (`Jwt--SigningKey` since Del 8,
  `ConnectionStrings--AtlasDb` since Del 9 — both pulled forward from
  Del 20, since a vault was already available); the Blob Storage account URL
  (`BlobStorage__AccountUrl`, Del 10) is a plain App Setting instead, since
  unlike those two it isn't actually a secret. Phase 3 (Del 20) is now
  mostly about generalizing the Key Vault half of this — e.g. moving it off
  a pre-existing vault and onto one provisioned as part of the project's own
  IaC, and replacing the reused server-admin SQL login with a narrower one
  scoped to just `AtlasDb` — rather than introducing Key Vault from scratch.
