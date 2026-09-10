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

Docker was deliberately **not** introduced here even though App Service
supports container deployment — Del 17 ("Docker — containerisering av
API:et", Phase 5) owns that scope. Del 8 deploys the plain `dotnet publish`
output instead, the same way `dotnet run` already does locally.

GitHub Actions authenticates to Azure via **OIDC federated credentials**
(`azure/login@v2`), not a stored client secret — the workflow exchanges its
own GitHub-issued token for an Azure AD token at run time, scoped to one
repo and one branch. Nothing secret-shaped lives in GitHub at all, which is
also why `AZURE_CLIENT_ID`/`AZURE_TENANT_ID`/`AZURE_SUBSCRIPTION_ID` are
repository **Variables**, not **Secrets** — they're GUIDs that say *which*
app registration to exchange a token with, not something that grants access
on its own. See `docs/AZURE_DEPLOYMENT.md` for the exact `az` commands.

## Current known simplifications (by design)

- App Service (Del 8) has nothing behind `ConnectionStrings:AtlasDb` yet —
  Azure can't reach the local dev SQL Server this project has used through
  Del 1–7 (it's behind a home NAT, not a public IP). Every database-backed
  endpoint will fail in Azure until Del 9 (Azure SQL) lands; only `/health`
  and, if `EnableSwaggerUi` is turned on, the OpenAPI/Swagger surface are
  expected to work there in the meantime. See `docs/AZURE_DEPLOYMENT.md`.
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
- Connection strings and the JWT signing key live in
  `appsettings.Development.json` / user-secrets locally, and as plain Azure
  App Service Application Settings once deployed (Del 8) — a step up from a
  committed file, but still a secret sitting in App Service configuration
  rather than a vault. Phase 3 (Del 20) replaces that with Azure Key Vault +
  `DefaultAzureCredential`.
