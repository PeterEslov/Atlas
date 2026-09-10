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
- `AuditLog` and `Notification` tables exist in the schema but nothing
  writes to them yet — they're wired up in Phase 4 (Del 11's overdue-ticket
  background worker and Del 12's Service Bus consumers).
- `Attachment.BlobName` is a plain string column; there is no upload
  endpoint yet. Phase 3 (Del 10) adds Azure Blob Storage and the upload flow.
- The JWT signing key and the Azure SQL connection string both live in
  `appsettings.Development.json` / user-secrets locally, and as Azure Key
  Vault secrets once deployed (`Jwt--SigningKey` since Del 8,
  `ConnectionStrings--AtlasDb` since Del 9 — both pulled forward from
  Del 20, since a vault was already available). Phase 3 (Del 20) is now
  mostly about generalizing this — e.g. moving it off a pre-existing vault
  and onto one provisioned as part of the project's own IaC, and replacing
  the reused server-admin SQL login with a narrower one scoped to just
  `AtlasDb` — rather than introducing Key Vault from scratch.
