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

**Del 15 — AuditLog, a system-wide compliance trail.** `AuditLog` itself is
not new — it's existed, unused, since Fas 1, with a doc comment already
drawing the line this Del finally acts on: a security- and
compliance-relevant record of events *across every entity* (user creation,
role changes, project archival, ...), deliberately distinct from
`TicketHistory`, which only ever tracks field-level changes on a single
ticket. Del 15 is the work of finding every event on that side of the line
and actually writing to the table.

That turned out to be five call sites across four services, not one:
`AuthService.RegisterAsync` writes a `Created` entry for the new user
(`UserService` never sees registration — it only exists once a user already
does); `UserService` writes `RoleChanged`/`Deactivated`/`Reactivated`;
`OrganizationService` writes `Created`/`Renamed`/`Deactivated`/`Reactivated`;
`ProjectService` writes `Created`/`Archived`/`Unarchived`; and
`TicketService.DeleteAsync` writes a `Deleted` snapshot — the one ticket-level
event that belongs here rather than in `TicketHistory`, because a hard delete
removes the ticket `TicketHistory` itself lives on, so `AuditLog` ends up
holding the *only* surviving trace that the ticket ever existed. Each entry
stores the actor's `UserId`, the action name, the entity's name and id, and
an `OldValuesJson`/`NewValuesJson` pair (via a small internal
`AuditLogSerializer`, `System.Text.Json` with web defaults) — never a
password or password hash, even for `Created` on a `User`.

Two things Del 15 deliberately does *not* log, both for the same reason Del
13 gave for `TicketService.AssignAsync` not invalidating the stats cache:
`Project.AddMemberAsync`/`RemoveMemberAsync` (membership churn is routine,
not a lifecycle event worth a compliance trail) and any ticket field-level
change (status, priority, assignment, comments — all already `TicketHistory`'s
job, and duplicating them into `AuditLog` would just be two records of the
same fact drifting out of sync over time).

Writing the audit row itself is deliberately **not** fail-open, unlike Del
12's Service Bus publish or Del 13's Redis cache. Both of those guard an
external system Atlas doesn't otherwise depend on for correctness — losing a
notification or a cache hit degrades the feature, not the fact. An audit
entry is the opposite: it only means something if it is exactly as durable
as the change it describes. So every audit write happens on the *same*
scoped `AtlasDbContext` as the change it records — `_auditLogRepository
.AddAsync(...)` stages the row, then the existing `_unitOfWork
.SaveChangesAsync(...)` call that was already going to run commits both in
one transaction. There's no separate audit database, no message queue, and
no window where the domain change could succeed while the audit entry it
required silently didn't.

Getting there surfaced a real gap: only `TicketService` had ever needed the
idea of "the user who did this" — `UserService`, `OrganizationService`, and
`ProjectService` had no `actorUserId` concept at all, because nothing they
did before Del 15 needed to record who did it. Closing that meant adding an
`actorUserId` parameter to `ChangeRoleAsync`/`DeactivateAsync`/
`ReactivateAsync` (User), `CreateAsync`/`RenameAsync`/`DeactivateAsync`/
`ReactivateAsync` (Organization), and `CreateAsync`/`ArchiveAsync`/
`UnarchiveAsync` (Project) — and giving `OrganizationsController`/
`ProjectsController` the same `ICurrentUserService`-backed `ActorUserId`
property (falling back to throwing `AuthenticationException` rather than
ever writing a null actor) that `TicketsController`/`UsersController`
already had.

Reading the trail back needed its own small piece of EF Core: `AuditLog` has
deliberately never had a `User` navigation property — it's stayed a minimal,
write-side-only entity since Fas 1, and adding a navigation purely to satisfy
a read-side convenience felt like the wrong end to grow it from. So
`AuditLogRepository.SearchAsync` resolves the acting user's display name at
query time with an explicit `GroupJoin`/`SelectMany`/`DefaultIfEmpty` against
`Users` — a manual LEFT JOIN — rather than `Include`, translating to one SQL
query with no navigation property required. `GET /api/audit-logs` sits behind
a new `AuditLog.Read` permission, and — unlike `User.Manage`/`Project.Manage`/
`Organization.Read`, all of which Manager already holds — it stops at Admin
only. The reason isn't extra caution for its own sake: `AuditLogs` has no
per-organization filter at all (a Manager's own tenant's audit trail isn't
separated from anyone else's), so granting it at Manager level would leak
audit visibility across every organization in the system — a materially
bigger gap than the existing, already-accepted `Organization.Read` one.

**Confirmed working end-to-end locally on 2026-09-12**, via
`scripts/test-del15-audit-log.sh` against a running `Atlas.Api`: seven
checks covering all four services — a self-registered user producing a
`Created` row, a role change producing `RoleChanged`, deactivate/reactivate
producing both entries, an organization's create/rename/deactivate cycle
producing three entries, a project's create/archive/unarchive cycle
producing three more, a hard-deleted ticket leaving exactly one `Deleted`
snapshot behind as its only remaining trace, and a Manager token correctly
receiving `403` from `GET /api/audit-logs` rather than `200` — all seven
passing against the real API and a real Azure SQL database, not a mock.

**Del 14 — Serilog and Application Insights, structured logging.** The
roadmap's Fas 5 line for this Del said "Application Insights, structured
logging" as if it were one thing to add from scratch, but the codebase
already had extensive structured logging: `TicketService`, `UserService`,
`OrganizationService`, `ProjectService` and `AuthService` all had `ILogger<T>`
call sites (`_logger.LogInformation("... {Property} ...", ...)`) since the
Dels that introduced them. `ILogger<T>` is an abstraction ASP.NET Core's
default logging providers implement — and Serilog implements too, as a
drop-in replacement. Del 14's actual job was almost entirely swapping the
logging *engine*, not writing new log call sites at every layer; the one
genuine gap it closed is described below. Scoped to `Atlas.Api` only for
this Del, not `Atlas.Worker` — see "Current known simplifications" below.

`Program.cs` uses Serilog's documented two-stage initialization pattern.
A minimal "bootstrap" logger — `Log.Logger = new
LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();` — exists
before `WebApplication.CreateBuilder(args)` even runs, so a startup failure
before configuration or DI exist yet (a malformed connection string, a
missing required setting) still gets logged instead of vanishing. Once the
builder exists, `builder.Host.UseSerilog((context, _, loggerConfiguration)
=> ...)` builds the real logger — `ReadFrom.Configuration(...)` (from the new
`Serilog.Settings.Configuration` package; NOT bundled into `Serilog.AspNetCore`
itself, unlike the console sink — confirmed by checking its actual dependency
list rather than assuming), enrichers, the console sink, and a conditional
second sink to Application Insights, wired up only when
`ApplicationInsights:ConnectionString` is actually a non-empty value —
`appsettings.json` ships that key empty, and `appsettings.Development.json`
omits it entirely, so a plain local `dotnet run` never tries to send
telemetry anywhere. Everything — the whole `try`/`catch`/`finally` around
`app.Run()` — is wrapped so that any unhandled startup or runtime exception
is captured by `Log.Fatal(ex, "Atlas.Api terminated unexpectedly")` before
the process exits, and `await Log.CloseAndFlushAsync()` in the `finally`
block makes sure buffered log events (Application Insights' sink batches
before sending) aren't lost on shutdown.

The `catch` clause is `catch (Exception ex) when (ex is not
HostAbortedException)` — deliberately excluding one specific exception type.
`dotnet ef migrations add`/`dotnet ef database update` build just enough of
the host to discover the `DbContext` and then deliberately throw
`Microsoft.Extensions.Hosting.HostAbortedException` as their own mechanism
for stopping short of `app.Run()`. Without the `when` guard, every single
`dotnet ef` command run against this project would log a spurious "Fatal"
line for something that isn't a failure at all.

`UseSerilogRequestLogging()`, added to the middleware pipeline right after
`app.Build()`, replaces the framework's own multi-line-per-request logging
with one structured line per HTTP request ("HTTP {Method} {Path} responded
{StatusCode} in {Elapsed} ms") — the kind of thing a real dashboard or log
query actually wants to filter and aggregate on, rather than several lines
of noise per request.

The one real gap Del 14 closed, rather than just re-plumbing: before this
Del, a failed login (`AuthService.LoginAsync`) produced no log line
whatsoever — the `AuthenticationException` it throws is caught by
`ExceptionHandlingMiddleware`'s dedicated 401 branch, which (unlike its
catch-all `Exception` branch) never logged anything either. Two
`_logger.LogWarning` calls were added, one per failure branch (invalid
credentials; a deactivated account), each naming the account being attempted
against — deliberately more specific than the identical, deliberately vague
401 message the caller ever sees (never revealing *why* a login failed is
what prevents user-enumeration; a log line that never leaves this process
has no such constraint, and repeated failed attempts against the same
`{Email}` becoming a queryable signal once this reaches Application Insights
was worth the two extra lines).

Adding the Application Insights sink surfaced a real NuGet dependency
conflict, not a cosmetic warning: `Serilog.Sinks.ApplicationInsights 5.0.1`
declares `Microsoft.ApplicationInsights (>= 2.23.0 && < 3.0.0)`, and an
initial pin at `Microsoft.ApplicationInsights 3.1.2` built fine locally but
produced a genuine `NU1608` warning ("resolved outside of dependency
constraint") — NuGet correctly flagging that the sink has never been tested
against the 3.x line and its behavior there is unverified. The fix was
pinning to `2.23.0`, the newest version that actually satisfies the sink's
own declared range, not silencing the warning.

Getting `scripts/test-del14-logging.sh` itself to work correctly along the
way surfaced a genuine ASP.NET Core hosting lesson, unrelated to Del 14's
actual application code: launching `Atlas.Api`'s compiled DLL directly
(rather than `dotnet run`, for reliable PID-based process control in the
script) with no `--contentRoot` argument resolves `ContentRootPath` to
`Directory.GetCurrentDirectory()` — the process's working directory — not
the DLL's own folder, so the app couldn't find its `appsettings*.json` files
when launched from the repo root. A *relative* `--contentRoot` value doesn't
fix this either — HostBuilder resolves it against `AppContext.BaseDirectory`
(the DLL's own folder, `bin/Debug/net10.0`), producing a nonsensical
double-nested path. The fix needed an *absolute* `--contentRoot` pointing at
`src/Atlas.Api`, which on Windows via Git Bash meant using `pwd -W` (the
Windows-style path MSYS's own `pwd` doesn't give a native, non-MSYS process
like `dotnet.exe`) rather than assuming the environment name
(`--environment`/`ASPNETCORE_ENVIRONMENT`) was ever the actual blocker, as it
first appeared to be.

**Confirmed working end-to-end locally on 2026-09-12**, via
`scripts/test-del14-logging.sh` against a running `Atlas.Api`: the app
starts and serves `/health` with a fake `ApplicationInsights:ConnectionString`
set (proving the sink wires up without crashing the app, though not that
telemetry actually reaches a real Azure resource — that needs a real
Application Insights instance, see `docs/AZURE_DEPLOYMENT.md`); console
output uses Serilog's own `[HH:mm:ss LVL]` format rather than the old
`info:`-style default; `UseSerilogRequestLogging` wrote a line for a GET
request with the correct status code; a failed login with the wrong
password produced `401` plus a matching `WRN` line; a failed login against a
deactivated account produced `401` plus its own, differently-worded `WRN`
line; and a successful login still produced the pre-existing `_logger`
line, now flowing through Serilog unchanged.

**Del 16 — a broader test suite: Application-layer unit tests plus real
HTTP integration tests.** Two new test projects, sitting next to
`Atlas.Domain.Tests` but deliberately different in what each one proves.

`Atlas.Application.Tests` mocks every Application-layer dependency
(`ITicketRepository`, `IUnitOfWork`, `IBlobStorageService`,
`ITicketEventPublisher`, `ITicketStatsCache`, `IAuditLogRepository`,
`IPasswordHasher`, `IJwtTokenGenerator`, ...) with **Moq**, and asserts that
`AuthService`, `UserService`, `OrganizationService`, `ProjectService`,
`TeamService` and `TicketService` orchestrate correctly in isolation — no
database, Azurite, Redis or Service Bus namespace needed. 39 tests turn
several previously-only-documented decisions into regression tests: the
Redis cache-invalidation matrix from Del 13 (status/priority/create/delete
invalidate; assignment and comments deliberately don't), the audit-scope
decision from Del 15 (`Project`/`Team` membership churn never writes an
audit row; Create/Archive/Unarchive/RoleChanged/Deactivated do), and the
orphaned-blob compensation path in `TicketService.AddAttachmentAsync` (if
the database write fails *after* the blob upload succeeds, the blob is
deleted before the exception is rethrown — verified with Moq's `Callback`
to capture the dynamically-generated blob name the mock never actually
receives ahead of time).

Two design mistakes were caught while writing these tests, before either
one reached disk, simply by re-reading the actual implementation instead of
trusting this document's own prose:

- `docs/ARCHITECTURE.md`'s Del 12 section describes Service Bus publish
  failures as "swallowed", which is true — but only inside
  `ServiceBusTicketEventPublisher` (`Atlas.Infrastructure`), not inside
  `TicketService.AssignAsync` itself, which has no `try`/`catch` around the
  publish call at all. A `TicketService`-level unit test with a throwing
  publisher mock correctly observes the exception *propagating*, the
  opposite of what the Del 12 prose might suggest at the wrong layer — see
  `TicketServiceTests`'s class doc comment for the corrected scope
  boundary.
- `TicketTag.Tag`'s navigation property (used by `ToDetailDto`'s
  `t.Tag?.Name ?? t.TagId.ToString()` to resolve a tag's display name) is
  only populated by EF Core's change-tracker *fixup* when a real
  `DbContext` is tracking both the `Ticket` and the `Tag` it's tagged
  with — fixup that a fully-mocked unit test, with no `DbContext` at all,
  can never trigger. `TicketServiceTests.AddTagAsync_...` was corrected to
  assert on the `TagId` alone; proving the *name* actually resolves is left
  to the integration test below, which is the only kind of test that can
  prove it.

`Atlas.Api.IntegrationTests` is the opposite kind of test: real HTTP calls,
through `Microsoft.AspNetCore.Mvc.Testing`'s
`WebApplicationFactory<Program>`, against a real ASP.NET Core host — real
JWT bearer authentication, real `[Authorize(Policy = ...)]` enforcement,
real routing — and a real database, a dedicated `AtlasDb_Test` LocalDB
instance kept separate from the `AtlasDb` a `dotnet run` session might have
open. `ApiFactory` (`Infrastructure/ApiFactory.cs`) is an `IAsyncLifetime`
xUnit collection fixture shared across every test class in the project: its
`InitializeAsync` runs `EnsureDeletedAsync()` + `MigrateAsync()` exactly
*once* per `dotnet test` invocation (not once per test class or test case)
and seeds one `Organization` row every test registers users against —
cheap only because every test generates its own unique data (a
Guid-suffixed email, a Guid-suffixed ticket title), the same discipline
this project's bash smoke-test scripts already use against Peter's real
dev database. `Program.cs`'s own `if (app.Environment.IsDevelopment())`
auto-migrate block does **not** run for `ApiFactory`'s `"Testing"`
environment (`IsDevelopment()` checks the environment name literally) —
`ApiFactory` is entirely responsible for its own schema setup, the same way
a human running `dotnet ef database update` by hand would be.

A direct `ProjectReference` from `Atlas.Api.IntegrationTests` to
`Atlas.Api.csproj` itself — not just `Atlas.Application` — is required for
`WebApplicationFactory<Program>` to resolve `ContentRootPath` correctly out
of the box: `Microsoft.AspNetCore.Mvc.Testing`'s MSBuild targets only emit
the `[assembly: WebApplicationFactoryContentRootAttribute]` that makes this
work automatically when there's a direct reference to the web project,
sidestepping a repeat of Del 14's `--contentRoot`/`pwd -W` debugging saga
(described above) for what would otherwise be the exact same underlying
problem. `public partial class Program {}`, appended to the bottom of
`Program.cs`, was added ahead of time during Del 14 for exactly this Del.

19 integration tests cover the two things this project's mocked unit tests
structurally cannot: `UsersController`'s self-role-change/self-deactivate
guard (it reads `ICurrentUserService.UserId` off a real authenticated
`HttpContext`, which only means something with a real HTTP pipeline behind
it — Admin included, since the guard checks the caller's own token, not
their role) and the `TicketTag.Tag` name-resolution fixup flagged above
(`AddTag_ThenGetById_ReturnsTheResolvedTagName` adds a tag through the real
API, fetches the ticket back, and asserts the actual tag name comes back —
something the mocked `TicketServiceTests` version of this same scenario
explicitly could not assert). The rest exercise permission-policy
enforcement end to end (a `Customer` token getting `403` from
`POST /api/tickets/{id}/assign`, a `Manager` token getting `403` from
`POST /api/organizations` since `Organization.Manage` is Admin-only, ...)
and a handful of full create → mutate → read round trips against the real
database.

**A genuine, if minor, finding, not a false positive**: one integration
test (`Register_WithAnEmailThatAlreadyExists_Returns401NotTheDocumented400`)
caught a real inconsistency between `AuthController.Register`'s
`[ProducesResponseType(StatusCodes.Status400BadRequest)]` Swagger metadata
and its actual runtime behavior. `AuthService.RegisterAsync` throws
`AuthenticationException` — the same exception type used for "wrong
password" at login — for a duplicate email, and
`ExceptionHandlingMiddleware` maps every `AuthenticationException` to
**401**, never 400. No mocked service-level unit test could ever catch this
kind of gap: `AuthServiceTests` correctly asserts
`ThrowsAsync<AuthenticationException>`, which says nothing about what
status code the middleware turns that into over HTTP. Left as a documented
inconsistency for now rather than "fixed" in either direction — see
README.md's Authentication & authorization section.

Integration tests are tagged `[Trait("Category", "Integration")]` on every
test class and excluded from CI's `dotnet test` step in
`azure-pipelines.yml` via `--filter "Category!=Integration"` — the
`ubuntu-latest` build agent has none of LocalDB, Azurite, a local Redis
container, or `az login`, the same reason this project's bash smoke-test
scripts (`scripts/test-del1*.sh`) have always been a local-only, opt-in
tier rather than part of CI.

**Confirmed working end-to-end locally on 2026-09-12**: `dotnet test
--filter "Category!=Integration"` — 93 tests (`Atlas.Domain.Tests` +
`Atlas.Application.Tests`) — all passing; `dotnet test
tests/Atlas.Api.IntegrationTests --filter "Category=Integration"` — all 19
integration tests passing against a real `AtlasDb_Test` LocalDB database,
Azurite, a local Redis container, and `az login`.

**Del 17 — Docker containerization: a multi-stage `Dockerfile` for
`Atlas.Api`, and `docker-compose.yml` running the whole local stack in
containers.** Scoped the same way Del 14 and Del 16 were — `Atlas.Api` only,
not `Atlas.Worker` — for the same reason: this is the piece with a
meaningful surface to containerize (a real HTTP host, real dependencies on
SQL Server/Redis/Azurite), and adding a second Dockerfile for `Atlas.Worker`
later is additive, not a rework of this one.

`src/Atlas.Api/Dockerfile` is two stages, not one, and the split is the
whole point: a `build` stage on the full 800 MB `dotnet/sdk:10.0` image
compiles and publishes the solution, then a `final` stage on the much
smaller ~220 MB `dotnet/aspnet:10.0` *runtime* image copies in only the
published output. The SDK, the source tree, NuGet's package cache and every
intermediate `obj`/`bin` file never exist in the image that actually ships —
`docker build` needs the SDK stage to run somewhere, but Docker discards it
once `final` is built. The build context has to be the repository root, not
`src/Atlas.Api/` itself, because `Atlas.Api.csproj`'s `ProjectReference`
entries reach outside that folder (to `Atlas.Application` and
`Atlas.Infrastructure`, transitively `Atlas.Domain`) — `docker build` can
only `COPY` files that live inside its context. `docker-compose.yml` sets
`context: .` / `dockerfile: src/Atlas.Api/Dockerfile` for exactly this
reason. Copying just the `.csproj`/`.sln` files first, running
`dotnet restore`, and only then copying the rest of the source is a
deliberate Docker layer-caching trick: as long as no project reference
changes, the slow `dotnet restore` step is served from cache on every
rebuild, even after editing a hundred `.cs` files. `USER app` runs the
container as the aspnet base image's own built-in non-root user rather than
root; `ENTRYPOINT ["dotnet", "Atlas.Api.dll"]` (array form, not shell form)
runs `dotnet` directly as PID 1 so it receives `SIGTERM` directly on
`docker stop`/`docker compose down`, which is what lets `Program.cs`'s
`finally { await Log.CloseAndFlushAsync(); }` actually flush before the
container exits — a shell-form entrypoint would wrap `dotnet` in `/bin/sh
-c "..."`, which swallows that signal and forces a slower, harder kill.

`docker-compose.yml` adds three supporting containers Peter's machine
previously ran directly (LocalDB, a Redis container already run this way
since Del 13, Azurite via `npm install -g azurite` since Del 10) — a real
`mcr.microsoft.com/mssql/server:2022-latest`, a bare `redis` image, and the
official `mcr.microsoft.com/azure-storage/azurite` image — each reachable
from `api` by Compose service name over one Compose-managed network
(`sqlserver`, `redis`, `azurite` — see `src/Atlas.Api/appsettings.Docker.json`,
a third environment alongside Development and Testing, since none of
Development's hostnames — `(localdb)\mssqllocaldb`, `localhost:6379`,
Azurite's default `127.0.0.1` endpoint — resolve from inside another
container the way they do from a process on the host). Each supporting
container has a `healthcheck`, and `api`'s `depends_on` uses
`condition: service_healthy`, not just `service_started` — SQL Server in
particular takes 15–30+ seconds to actually accept a connection after the
container process starts, comfortably longer than `AddPersistence`'s
`EnableRetryOnFailure` (3 retries, up to 5s apart) reliably covers on its
own. `Program.cs`'s auto-migrate-on-startup block, previously
`if (app.Environment.IsDevelopment())`, now also runs for
`IsEnvironment("Docker")` — the same "fresh, disposable, local-only SQL
Server, nothing worth protecting" trust level Development already has, and
neither name is ever what a real Azure App Service deployment runs under.

**Confirmed working end-to-end locally on 2026-09-14**: `docker compose up
--build` builds the image (22 BuildKit steps), starts all four containers,
and — after two expected, self-healing transient `Login failed for user
'sa'` errors (SQL Server's own password-policy initialization racing the
healthcheck probe, then `api`'s first connection attempt, both while SQL
Server was still finishing its first-boot startup work) — successfully
creates and migrates the containerized `AtlasDb` from scratch, all 14
tables and both migration-history rows included. `Atlas.Api` starts under
`Hosting environment: Docker` and serves Swagger UI at
`http://localhost:8080/swagger`.

The first real request through it, `POST /api/auth/register`, failed —
twice, with two different, genuinely different causes, worth recording
separately since they're easy to conflate:

- First attempt: a `400` (its response body wasn't captured before the
  request changed, so the exact field is unconfirmed) — `RegisterRequest`
  has no explicit `[Required]` attributes of its own, so the two live
  possibilities for a bare `ValidationProblemDetails` here are ASP.NET
  Core's implicit-required behavior for non-nullable reference type
  properties (`FullName`/`Email`/`Password`) when a field is missing or
  `null` in the JSON body, or a JSON conversion failure — e.g. an
  unparseable `OrganizationId` GUID, or a `Role` value outside `UserRole`'s
  0–3 range, since no `JsonStringEnumConverter` is registered and Swagger
  UI's Role field is therefore numeric, not the role's name.
- Second attempt: a genuine `500`, `"An unexpected error occurred"` —
  `ExceptionHandlingMiddleware` only has four `catch` branches
  (`DomainException`→400, `NotFoundException`→404,
  `AuthenticationException`→401, catch-all `Exception`→500), and
  `AuthService.RegisterAsync` never checks that `OrganizationId` actually
  refers to a real row before calling `User.Create(...)` and
  `SaveChangesAsync()` — so an `OrganizationId` with no matching
  `Organizations` row throws a `DbUpdateException` (a real FK violation,
  `FK_Users_Organizations_OrganizationId`) that isn't any of the three typed
  exceptions the middleware specifically handles, and falls through to the
  generic 500. This turned out to be the exact same bootstrapping
  chicken-and-egg problem README.md's "[8. Try it](../README.md#8-try-it)"
  section already documents for a plain `dotnet run` — a brand-new database
  has zero organizations, so there is no valid `organizationId` to register
  against until `sql/002_SeedData.sql` (or one hand-inserted row) runs —
  except the containerized `AtlasDb` starts every bit as empty as a fresh
  LocalDB would, and nothing in `docker-compose.yml`/`Dockerfile`/
  `appsettings.Docker.json` seeds it automatically. Loading the seed script
  into a running container needs `docker compose cp` (the file isn't inside
  the SQL Server image) followed by `docker compose exec sqlserver
  /opt/mssql-tools18/bin/sqlcmd ...` — see README.md's new "[Alternative: run
  everything in Docker](../README.md#alternative-run-everything-in-docker-del-17)"
  section for the exact commands.

One more environment-specific snag surfaced while running those `docker
compose exec` commands from Git Bash (MINGW64) on Windows: Git Bash's own
MSYS2 layer automatically rewrites any argument that looks like a Unix
absolute path (`/opt/mssql-tools18/bin/sqlcmd`) into a Windows path
(`C:/Program Files/Git/opt/...`) before Docker ever sees it, which fails
with a confusing "no such file or directory" that has nothing to do with
SQL Server or the container's actual contents. `MSYS_NO_PATHCONV=1` prefixed
onto the command disables that rewrite for the one invocation. This is a
Git-Bash-on-Windows quirk, not a Docker or Compose issue — a `bash`/`zsh`
shell on Linux or macOS, or PowerShell, never hits it.

Once seeded, both attempts corrected, register and login succeeded through
Swagger UI, completing the first full end-to-end smoke test of the
containerized stack.

**Del 18 — CI/CD: verifying the Docker image builds on every push.** The
roadmap line for this Del just says "extend CI/CD with docker build", which
leaves real room to interpret — anywhere from "prove the Dockerfile still
builds" up through "push it to a registry and switch App Service to run it
in production". `docs/AZURE_DEPLOYMENT.md`'s Del 17 section had already
flagged that range as a deliberately open question rather than something
Del 17 should decide on its own behalf. Presented with the choice, this Del
took the smallest, lowest-risk slice of it: a new `DockerBuild` job in
`azure-pipelines.yml`'s existing `BuildAndTest` stage runs
`docker build -f src/Atlas.Api/Dockerfile -t projectatlas-api:$(Build.BuildId) .`
on every push and PR against `main`, and nothing more — no Azure Container
Registry was provisioned, no image is pushed anywhere, and `Deploy` still
publishes straight to App Service exactly the way Del 8 set it up. The
value is the same one a compile step already provides for the C# above it
in the same stage: it turns Dockerfile bit-rot (a `COPY` path left stale
after a project rename, a project reference the multi-stage build can no
longer resolve) into an immediate, loud CI failure instead of something
only discovered the next time someone actually runs `docker compose up
--build` by hand. Whether to go further — provision an ACR, push the image,
and switch App Service to Web App for Containers — is left as a deliberate,
separate decision for a later Del, not a gap this one failed to close.

`DockerBuild` is its own job, not a step appended to the existing `Build`
job, on purpose: `src/Atlas.Api/Dockerfile`'s own `build` stage runs a
completely separate `dotnet restore`/`publish` *inside* the container, from
source, so it needs none of `Build`'s own dotnet output — giving it a
separate job lets Azure Pipelines schedule it in parallel with `Build`
rather than forcing it to wait in line behind it. (Whether that parallelism
is actually realized depends on how many parallel jobs the Azure DevOps
org has available — a free-tier org with only one will still run them one
after another in practice, which is a scheduling detail, not a pipeline
bug.) Because `DockerBuild` lives inside the `BuildAndTest` stage, a broken
Dockerfile fails that stage, which — via `Deploy`'s own
`dependsOn: BuildAndTest` — blocks the App Service deployment too, exactly
the way a failing `dotnet test` already does, even though nothing about
`DockerBuild` sits on the App Service deploy's actual critical path. That's
intentional, not an oversight: a broken Dockerfile is a real regression
worth gating on. No explicit Docker installation step was needed —
Azure Pipelines' Microsoft-hosted `ubuntu-latest` pool image ships Docker
Engine preinstalled.

**Confirmed working end-to-end against the live pipeline on 2026-09-14**,
though not on the first attempt: the very first run failed at the `Deploy`
stage — not because of anything `DockerBuild` did (`BuildAndTest` itself
passed, `DockerBuild` included), but because `azureServiceConnection` in
`azure-pipelines.yml` still held the generic placeholder value
(`sc-projectatlas-dev-sc`) `docs/AZURE_DEPLOYMENT.md`'s own Del 8 setup
instructions had used as an example, while the service connection Peter had
actually created in Azure DevOps was named `project-atlas-connectionname`.
Azure Pipelines matches a service connection by name, character for
character, against whatever `azureServiceConnection` resolves to — there is
no fuzzy matching, no fallback to "the only Azure Resource Manager
connection in the project" — so any mismatch here fails `Deploy` outright,
with an error naming the service connection it couldn't find. This wasn't a
bug Del 18 introduced; it was a stale value left over from Del 8 that
nothing had exercised end-to-end until this Del's own pipeline run finally
forced the question. Corrected in both `azure-pipelines.yml` and
`docs/AZURE_DEPLOYMENT.md`'s section 5 (which now shows the real name Peter
uses, rather than an illustrative placeholder), after which both
`BuildAndTest` (`Build` and `DockerBuild`) and `Deploy` ran green.

**Del 19 — Infrastructure as Code: redeploying the environment from Bicep,
and a real-deployment saga worth reading in full.** Every earlier Azure Del
(8 through 14) provisioned its resource by hand, one `az` command at a
time, documented as a checklist in `docs/AZURE_DEPLOYMENT.md`. Del 19 turns
that checklist into `infra/main.bicep` and a `modules/` folder — one file
per resource type (App Service plan, Web App, SQL database, Storage
account, Application Insights, Redis, plus a role-assignment module per
existing resource it needs access to), deployed at resource-group scope
against `rg-projectatlas-dev-sc`. The existing Key Vault, SQL logical
server and Service Bus namespace are all declared `existing` and never
owned by this template — consistent with why each was reused rather than
provisioned fresh in the first place (see the Del 9/12 sections above) —
and modules that reach into a *different* resource group than the main
deployment (the SQL database, onto a server that might not live in
`rg-projectatlas-dev-sc`; the Service Bus role assignment, against a
namespace that doesn't either) use `scope: resourceGroup(...)` on the
module call, a pattern borrowed — as a *pattern*, not as code, per Peter's
explicit instruction — from a personal Bicep template repo of his
(`KR.AZ.Bicep.Templates`) that already used the same cross-resource-group
scoping for an Application Insights workspace module.

Two design choices worth calling out on their own. First,
`Jwt--SigningKey` and `ConnectionStrings--AtlasDb` — the two Key Vault
secrets Del 8/9 already set by hand — are deliberately never written by
any Bicep module here; only the one secret that's genuinely new
(`Redis--ConnectionString`, below) gets a `keyVaultSecret.bicep` call, so
re-running this deployment can never silently overwrite a secret that's
already correct. Second, the Key Vault ("Key Vault Secrets User") and
Storage ("Storage Blob Data Contributor") role assignments Del 8/10 also
already granted by hand are *not* redeployed either, even though the
modules for them (`keyVaultRoleAssignment.bicep`,
`storageRoleAssignment.bicep`) still exist in the repo — a real `az
deployment group create` run explained why (see below), and it's worth
understanding rather than just a rule to follow.

**Confirmed working end-to-end against the live subscription on
2026-09-14** — but only after four real, distinct failures, each diagnosed
from Azure's own error message rather than guessed at in advance, which is
the actual point of running IaC for real instead of stopping at "it
compiles":

1. `az bicep build` (and the CI job below) only ever check syntax — they
   never caught that `webApp.bicep` hardcoded `siteConfig.alwaysOn: true`.
   Azure's Free (F1) App Service tier — this project's actual plan SKU —
   doesn't support Always On at all; deploying that setting against F1
   fails outright. This one surfaced from `az deployment group what-if`,
   before any real resource was touched: the preview showed
   `alwaysOn: false => true` as a pending change, which was the cue to
   check F1's actual capabilities rather than assume the setting was safe.
   Fixed by turning `alwaysOn` into a parameter defaulting to `false`
   (matching what Del 8 already had running), documented in
   `webApp.bicep` as only safe to flip once the plan SKU is B1 or higher.
2. The first real `az deployment group create` failed two of its
   resources with `RoleAssignmentExists`. Azure refuses a second role
   assignment for the same (identity, role, scope) triple no matter what
   the new assignment's own resource name is — so the deterministic-GUID
   naming trick that makes a *Bicep-created* role assignment safe to
   redeploy doesn't help when the original assignment was created by hand
   via `az role assignment create` (a different, randomly-named resource),
   which is exactly what Del 8/10 did. Nothing to fix code-wise: this is
   Bicep correctly detecting the work is already done. The two calls
   (`keyVaultRoleAssignment`, `storageRoleAssignment`) were removed from
   `main.bicep`'s module list — the modules themselves stay in the repo,
   still correct, useful as-is against a fresh environment that doesn't
   have these grants yet.
3. The same run failed Redis outright: `"Azure Cache for Redis is
   retiring, create Azure Managed Redis instance instead."` — a platform-
   level retirement neither Peter nor this session knew about ahead of
   time, discovered only because a real deployment was attempted against
   a real subscription. `redisCache.bicep` was rewritten against the
   replacement resource type, `Microsoft.Cache/redisEnterprise` (+ its
   required `.../databases` child resource), Balanced_B0 the cheapest SKU
   in the new tier structure — researched against Microsoft's current
   documentation rather than pattern-matched from training data, given how
   recently the platform had changed. That rewrite surfaced two further,
   smaller gaps on the next two attempts: the API version originally
   chosen (`2024-05-01-preview`) wasn't actually registered in Peter's
   subscription/region (`NoRegisteredProviderFound`, which usefully listed
   the versions that *are* registered — pinned to the stable `2025-07-01`
   instead), and that version's schema turned out to require
   `publicNetworkAccess` explicitly (set to `'Enabled'`, since this
   project has no VNet integration anywhere — nothing else could reach the
   cache otherwise).
4. Even after Redis itself deployed successfully, the *same* deployment
   failed one step later: `"The ListKeys operation is not supported when
   access keys are disabled."` Azure Managed Redis defaults new databases
   to key-based access **disabled**, steering toward Entra ID
   authentication instead — a deliberate platform default, not a bug, but
   one this project's approach (a `StackExchange.Redis` client reading a
   connection string with a password from Key Vault, the same shape Del
   13 already used against classic Redis) genuinely needs turned on. Fixed
   by setting `accessKeysAuthentication: 'Enabled'` explicitly on the
   database resource rather than relying on a default that had quietly
   flipped.

Every one of these was root-caused from the exact text Azure returned,
confirmed by re-running the real command, never by inference from
documentation alone once a live counter-example existed — the same
discipline this project has applied to every other Azure Del (see the Del
18 section above for the `azureServiceConnection` mismatch, diagnosed the
same way). `azure-pipelines.yml` also gained a `BicepValidate` job
alongside `DockerBuild`, on the same "prove it still compiles on every
push" logic — `az bicep build` only, no Azure calls, no cost, and
deliberately not a `what-if`/`create` in CI: those need real
environment-specific parameter values and would provision or change
billable resources unattended on every push, exactly the kind of risk this
project has avoided everywhere else (see `docs/AZURE_DEPLOYMENT.md`'s "you
run this yourself" reasoning, restated in `infra/main.bicep`'s own header
comment).

**Del 21 — Frontend: React + TypeScript against the real API, not a mock.**
`frontend/` (React 18.3, TypeScript 5.6, Vite 5.4, Tailwind CSS 3.4,
TanStack Query 5.59, `react-router-dom` 6.26) is a full CRUD client covering
all five entities — Tickets, Organizations, Users, Projects, Teams — plus
Auth and a read-only Audit Log view, built entirely from reading the real
backend source (every controller, DTO, enum, permission string and JWT
claim) rather than guessed or pattern-matched from a typical REST API
shape. `Program.cs`'s `AllowLocalDev` CORS policy had already whitelisted
`http://localhost:5173`/`:3000` back in Del 8, in anticipation of exactly
this Del.

Two decisions worth calling out on their own, because both come from a
mismatch between how a typical frontend is built and how this specific
backend actually behaves. First, `Program.cs` never registers a
`JsonStringEnumConverter`, so every C# enum (`TicketStatus`,
`TicketPriority`, `OrganizationType`, `UserRole`) serializes as a plain
number over the wire, not a string — `src/api/types.ts` mirrors each one as
a numeric TypeScript enum with identical integer values, and every
`<select>` submits that numeric value rather than a label. Second,
`src/auth/permissions.ts` copies the literal permission strings from
`Atlas.Domain/Security/Permissions.cs` and gates every nav link, button and
route with `can(Permissions.X)`, sourced from `AuthResponseDto.permissions`
— but this is documented explicitly, in the code and here, as a UX nicety
only, never a security boundary. The server's `[Authorize(Policy=...)]`
checks are the real enforcement; a user who edits the client bundle to
un-hide a button still hits a 403 from the API, exactly as intended.

Building the frontend also surfaced one genuine, pre-existing backend
contract gap rather than a frontend bug: `AuthResponseDto` never included
`organizationId`, even though `JwtTokenGenerator` already stamps an
`organization_id` claim into every token it issues (see the Del 5 section
above). Rather than changing a backend response shape for a Del scoped to
the frontend only, `src/auth/jwt.ts` reads the claim directly out of the
JWT's own (unverified, base64url-decoded) payload client-side — a
pragmatic workaround, not a fix, and a real candidate for a future Del to
close properly on the backend side instead.

Verification had a real gap worth being honest about: the cloud sandbox
this frontend was built in has no route to the npm registry (org egress
policy, `403`/`host_not_allowed`), so `npm install`/`tsc`/a real Vite build
were never actually run before the code reached Peter's machine. What
verification *did* happen there was manual — a brace/paren balance check,
an unused-import heuristic, and an import/export cross-check across all 39
TypeScript/TSX files — which is real signal but not the same thing as a
compiler actually agreeing the code is correct. That gap closed the moment
Peter ran `npm install && npm run dev` locally himself.

**Confirmed working end-to-end against Peter's local LocalDB-backed API
(mid-September 2026)**, after two real, worth-recording snags along the
way — both environment/bootstrapping issues, not bugs in the frontend
code itself:

1. The seeded demo users (`sql/002_SeedData.sql` — Anna, John) have no
   password hash, so they can't actually log in through the app; the
   bootstrap path is registering a fresh account via the frontend's own
   Register form, which needs a real `organizationId` looked up directly
   from whichever database is actually running underneath (LocalDB,
   the `docker-compose.yml` SQL Server container, or Azure SQL — three
   different places to run the same `SELECT Id, Name FROM
   dbo.Organizations` query, depending on which local setup is active).
2. An early login attempt failed with a generic browser-level "Failed to
   fetch" — distinct from a proper 401/400 `ProblemDetails` response,
   meaning the request never reached `ExceptionHandlingMiddleware` at
   all. Left undiagnosed to a single root cause (most likely `Atlas.Api`
   not yet running, or a port mismatch), but self-resolved once the API
   was confirmed up — a reminder that this class of error is a browser/
   network-layer symptom, not an API error, and worth ruling out first.

The frontend was subsequently also verified against the Del 17
`docker-compose.yml` stack — `Atlas.Api` inside Docker listens on port
8080, not the 5080 `dotnet run` uses, so `frontend/.env.local`'s
`VITE_API_BASE_URL` is the only thing that changes between the two; the
`AllowLocalDev` CORS policy itself needed no change, since it only ever
cares which origin the browser request comes from (`localhost:5173`), not
which port the API happens to be listening on.

One small piece of forward-looking groundwork landed alongside Del 21
without being its own Del: `Program.cs`'s CORS setup now also reads an
optional `Cors:AdditionalOrigins` configuration value (empty by default),
so a future deployed frontend origin (e.g. Azure Static Web Apps, for a
portfolio demo) can be added as a plain Azure App Setting later, without a
code change or redeploy. Nothing consumes it yet — that's a real deploy
this project hasn't attempted, tracked as a draft checklist in
`docs/DEMO_DEPLOY_CHECKLIST.md` rather than documented here as done.

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
  background worker) and Del 12 (`TicketAssigned` events via Service Bus).
- `AuditLog` (Del 15) has no per-organization filter on
  `GET /api/audit-logs` — the reason it's Admin-only rather than
  Manager-level like `User.Manage`/`Project.Manage` (see the Del 15 section
  above). Adding one would need `AuditLog` to record which organization each
  entity belonged to at the time, which it doesn't today, since not every
  audited entity (a `User`, an `Organization` itself) obviously has one. Also
  still missing: any retention/archival policy — the table only ever grows.
- `TicketAssignedConsumer` (Del 12) has no protection against Service Bus's
  at-least-once delivery redelivering the same message and creating a
  second `Notification` row — closing that gap would mean keying off each
  event's `EventId` (not currently stored on `Notification`), the same kind
  of guard `GetNotifiedTicketIdsAsync` already gives Del 11's overdue-ticket
  path against its own, different kind of duplicate.
- Del 12's Service Bus RBAC role (`Azure Service Bus Data Owner`) is granted
  to Peter's own Azure AD identity for local development. The deployed
  `Atlas.Api`'s managed identity was, until Del 19, missing the
  corresponding role and couldn't publish from Azure at all — closed by
  `infra/modules/serviceBusRoleAssignment.bicep` (2026-09-14), which grants
  it the narrower "Azure Service Bus Data Sender" instead of "Data Owner"
  (least-privilege: the deployed API only ever publishes, never consumes or
  administers the namespace). The role assignment itself is confirmed
  created (`az deployment group create`, 2026-09-14); an actual publish
  call against the deployed API, and certainly a full round trip, still
  isn't verified — see `docs/AZURE_DEPLOYMENT.md` section 9.2, which
  remains accurate: no consumer exists in Azure yet to confirm one.
- `Atlas.Worker` (Del 11) only runs locally so far — it isn't deployed
  anywhere in Azure yet. *How* it should run there (a Container App Job on a
  schedule, a WebJob alongside the existing App Service, Azure Functions with
  a timer trigger, or its own always-on App Service) is a deliberate open
  question, not an oversight — the same "get it right locally first, decide
  the Azure hosting shape as its own step" order Del 8/9/10 already followed
  for `Atlas.Api` itself. Del 12's Service Bus consumer inherits the same
  open question, since it lives inside `Atlas.Worker`.
- Del 13's Redis cache has no managed-identity path at this project's
  configuration, unlike Blob Storage (Del 10) and Service Bus (Del 12):
  `Redis:ConnectionString` genuinely is a secret in Azure — a Key Vault
  entry (`Redis--ConnectionString`), not a plain App Setting, unlike
  `BlobStorage:AccountUrl`/`ServiceBus:FullyQualifiedNamespace`. An Azure
  Redis instance is now provisioned (Del 19, 2026-09-14,
  `infra/modules/redisCache.bicep`) — but as Azure Managed Redis
  (`Microsoft.Cache/redisEnterprise`), not the classic Azure Cache for
  Redis `docs/AZURE_DEPLOYMENT.md` section 10 originally planned around:
  classic Redis turned out to be mid-retirement when Del 19 first tried to
  deploy it, a platform change neither discovered until a real `az
  deployment group create` failed with Azure saying so outright. Managed
  Redis *does* have an Entra ID/RBAC auth path in principle, just not at
  the access-policy configuration this project's Bicep sets up (key-based
  access, `accessKeysAuthentication: 'Enabled'`) — closing that gap the
  same way Blob Storage/Service Bus already are is a reasonable Del 20+
  candidate, not something Del 19 needed to solve. `RedisTicketStatsCache`
  itself (the C# cache-aside logic) hasn't changed at all — same
  StackExchange.Redis client, same connection-string shape, just a
  different port (10000, not 6380) once the app actually points at the
  cloud instance instead of the local Docker container Del 13 is still
  confirmed working against day to day.
- Ticket hard-delete (`TicketService.DeleteAsync`) cascades `Attachment` rows
  away via the configured `OnDelete(DeleteBehavior.Cascade)`, but doesn't
  clean up the matching blobs in Storage the other direction — a theoretical
  mirror image of the orphaned-blob case `AddAttachmentAsync`'s compensating
  delete already guards against (see Del 10 above), just not something
  normal application use triggers today. `DownloadAttachmentAsync` logs a
  warning and returns 404 if it ever encounters a DB row with no matching
  blob, rather than throwing — there is genuinely nothing to download.
- Del 14's Serilog/Application Insights logging is scoped to `Atlas.Api`
  only — `Atlas.Worker` still uses the default .NET Generic Host logging
  providers, unchanged. Extending the same Serilog setup to `Atlas.Worker`
  is deliberately deferred rather than done alongside Del 14, the same
  "get the App first, extend to the Worker as its own step" order
  `AddPersistence`/`AddBlobStorage`/`AddAuthInfrastructure` already
  established (see Del 11 above) for infrastructure wiring generally.
- `ApplicationInsights:ConnectionString` is still empty in `appsettings.json`
  and absent entirely from `appsettings.Development.json` by design, so
  local development never attempts to send telemetry anywhere.
  `scripts/test-del14-logging.sh` still only ever passes a fake connection
  string to prove the sink wires up without crashing the app, not that
  telemetry reaches a real resource — that's now genuinely possible to
  verify, though, since Del 19 (2026-09-14,
  `infra/modules/applicationInsights.bicep`) provisioned a real,
  workspace-based Application Insights resource (plus its backing Log
  Analytics workspace) for the first time; the deployed `Atlas.Api`'s
  `ApplicationInsights__ConnectionString` app setting is wired to it
  (`infra/modules/webApp.bicep`). Whether telemetry actually shows up in
  the portal against real production traffic hasn't been checked yet —
  Del 19 provisioned the resource and wired the connection string, the
  same "infrastructure, not verification" scope every other module in
  `infra/` has.
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
- Del 16's test suite is scoped to `Atlas.Api` only, the same scoping
  decision Del 14 made — `Atlas.Worker` has no unit or integration test
  project of its own yet. Its two consumers
  (`OverdueTicketNotificationService`, `TicketAssignedConsumer`) are
  currently only exercised indirectly, by hand, via
  `scripts/test-del11-worker.sh`/`scripts/test-del12-servicebus.sh` — a real
  gap next to `Atlas.Api`'s now much better-covered services, and a
  reasonable candidate to close alongside whatever Del eventually gives
  `Atlas.Worker` its own Serilog setup.
- The `AuthController.Register` 401-vs-documented-400 inconsistency Del 16
  found (see that Del's section above) is left as a documented
  inconsistency rather than fixed in either direction — fixing it means
  picking a side (loosen `ExceptionHandlingMiddleware`'s mapping to
  distinguish "bad credentials" from "this business rule was violated", or
  just correct the Swagger attribute to match what the API already does)
  and this project would rather make that call deliberately, later, than as
  a rushed one-line change while writing an unrelated test.
- `AuthService.RegisterAsync` still doesn't validate that `OrganizationId`
  refers to a real `Organizations` row before saving — Del 17's own
  containerized-stack testing hit exactly this gap (see that Del's section
  above), the same way a fresh LocalDB always has. Left unfixed for the same
  reason as the point above: turning that FK violation into a proper
  `DomainException`/400 is a deliberate, separate change to
  `ExceptionHandlingMiddleware`'s and/or `AuthService`'s error handling, not
  something to slip in while writing Docker infrastructure.
- Docker Compose's `docker-compose.yml` has no automated seeding step of its
  own — the same "run `sql/002_SeedData.sql` by hand once" bootstrapping
  step README.md's "Try it" section has always documented for a plain
  `dotnet run` applies equally to the containerized stack, just via
  `docker compose cp` + `docker compose exec sqlserver sqlcmd` instead of a
  direct `sqlcmd -S "(localdb)\mssqllocaldb"` call. A future Del could add
  this as an init container or a compose `depends_on` step, but that's a
  bigger design decision (does every `docker compose up` reseed, or only an
  explicitly empty database?) than Del 17 tried to make.
- `appsettings.Docker.json` reuses the same JWT signing key and Azure
  Service Bus namespace `appsettings.Development.json` already commits —
  deliberately, since the containerized stack is exactly as disposable and
  local-only as a plain `dotnet run` is, not a step toward a real
  deployment. The image `docker-compose.yml` builds has no path to Azure of
  its own yet; that only starts to matter once Del 18 (CI/CD) decides
  whether/where this same `Dockerfile` gets built and pushed in the
  pipeline.
- Del 18's `DockerBuild` job builds the image and throws it away — nothing
  persists it, not even as a pipeline artifact. That's consistent with the
  Del's own deliberately minimal scope (see that Del's section above), but
  it does mean the image a given commit *would* produce is never actually
  available anywhere to inspect or run without rebuilding it locally.
  Publishing it as a pipeline artifact (without going as far as pushing to
  a real registry) would be a small, natural next step if that becomes
  useful before the larger ACR/Web-App-for-Containers decision is made.
- The `azureServiceConnection`/`webAppName` variables at the top of
  `azure-pipelines.yml` are plain strings checked into source control, with
  no validation beyond "does a service connection or Web App with this
  exact name exist" at pipeline-run time — there is no earlier, faster
  feedback loop (a lint step, a required-variable check) that would catch a
  stale or mistyped value before `Deploy` actually runs and fails. Del 18's
  own service-connection mismatch (see that Del's section above) is a live
  example of exactly this gap; it was caught by running the pipeline, not
  by anything checking the YAML beforehand.
- `infra/modules/redisCache.bicep`'s Redis access key travels from the
  `redisEnterprise/databases` resource's `listKeys()` call to the
  `Redis--ConnectionString` Key Vault secret as a plain (non-`@secure()`)
  module output, because Bicep module outputs can't be marked `@secure()`
  the way parameters can. The key ends up recorded in this deployment's
  history (`az deployment group show`), readable by anyone with read
  access to the resource group's deployment history, not just Key Vault
  Secrets User — a real gap, same category as the reused SQL admin login
  above, not something Del 19 solves. Rotating the key after deployment
  (`az redis regenerate-keys`, or its Managed Redis equivalent) and
  clearing old deployment history are the practical mitigations until a
  cleaner pattern replaces it.
- `BicepValidate` (Del 19's CI job) only ever runs `az bicep build` —
  syntax and type compilation, no Azure calls at all. It would have caught
  none of the four real failures Del 19 actually hit deploying for real
  (see that Del's section above): a role assignment collision, a retired
  resource type, an unregistered API version, and a platform default that
  needed overriding are all only visible to a real `az deployment group
  what-if`/`create` against a real subscription, which CI deliberately
  never runs (cost/risk, not a testing gap CI could close for free — see
  the reasoning in `infra/main.bicep`'s header comment). A green
  `BicepValidate` run is proof the templates parse, not proof they deploy.
- `AuthResponseDto` still doesn't carry `organizationId`, even though Del
  21's frontend needs it and the JWT already contains it as a claim —
  `src/auth/jwt.ts` decodes the token payload client-side as a workaround
  rather than changing the backend response shape for a frontend-only Del.
  Adding `organizationId` to `AuthResponseDto` properly, so no client ever
  needs to reach into an unverified JWT payload for something the server
  already knows, is a real candidate for a future Del.
- Del 21's permission-based UI gating (`src/auth/permissions.ts`,
  `RequirePermission`) is real code doing real work, but it's a UX
  convenience, not a security boundary, and it isn't tested as one: nothing
  in the frontend automatically verifies that its gates stay in sync with
  `Atlas.Domain/Security/RolePermissions.cs` if that file changes later. The
  server's `[Authorize(Policy=...)]` checks remain the only actual
  enforcement; a drift between the two would only ever produce a confusing
  UI (a hidden button that would have worked, or a visible one that 403s),
  never a real permission bypass.
- Del 21 was never actually compiled or built in the environment it was
  written in — the cloud sandbox has no route to the npm registry, so
  `tsc`/`vite build` only ran for the first time on Peter's own machine,
  after the code had already been delivered. Verification up to that point
  was manual (brace/paren balance, unused-import and import/export
  cross-checks across all 39 files) — real signal, but not a substitute for
  a compiler actually agreeing the code type-checks.
