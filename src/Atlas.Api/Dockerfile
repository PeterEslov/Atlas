# Del 17 — containerizing Atlas.Api.
#
# Two stages, not one, and that split is the whole point of this file:
#
#   "build" — has the full .NET SDK (~800 MB), compiles the solution, and is
#   thrown away once it's done its job.
#   "final" — has only the much smaller ASP.NET Core *runtime* image
#   (~220 MB) plus the already-compiled output copied in from "build". The
#   SDK, the source code, NuGet's package cache, and every intermediate
#   obj/bin file never exist in the image that actually ships or runs — only
#   the final `dotnet publish` output does. `docker build` still needs the
#   SDK stage to run somewhere, it just never ends up in the runtime image
#   docker-compose.yml/`docker run` actually starts.
#
# IMPORTANT — the build CONTEXT for this Dockerfile must be the repository
# ROOT, not this src/Atlas.Api/ folder: Atlas.Api.csproj has ProjectReference
# entries pointing at ../Atlas.Application/Atlas.Application.csproj and
# ../Atlas.Infrastructure/Atlas.Infrastructure.csproj, which in turn
# reference ../Atlas.Domain/Atlas.Domain.csproj — `docker build` can only
# COPY files that live inside its context, and none of those sibling
# projects live inside src/Atlas.Api/ itself. docker-compose.yml at the repo
# root already points `context: .` / `dockerfile: src/Atlas.Api/Dockerfile`
# for exactly this reason; if you ever run `docker build` by hand instead,
# it has to be `docker build -f src/Atlas.Api/Dockerfile .` from the repo
# root — `docker build -f Dockerfile .` from inside src/Atlas.Api/ will fail
# to restore with a "file not found" error on the very first COPY below.

# ---- Stage 1: build -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copying just the .csproj/.sln files first, then running `dotnet restore`,
# then copying everything else, is a deliberate Docker layer-caching trick —
# NOT just tidiness. Docker caches each instruction's result keyed on that
# instruction plus everything copied into it; as long as none of these
# seven project files change, `dotnet restore` (the slow step — it downloads
# every NuGet package this solution uses) is served entirely from cache on
# every rebuild, even after editing a hundred .cs files. Copying the whole
# repo first and restoring afterwards would invalidate that cache on every
# single source edit, since the copy step's cache key would change every time.
COPY ProjectAtlas.sln ./
COPY src/Atlas.Domain/Atlas.Domain.csproj src/Atlas.Domain/
COPY src/Atlas.Application/Atlas.Application.csproj src/Atlas.Application/
COPY src/Atlas.Infrastructure/Atlas.Infrastructure.csproj src/Atlas.Infrastructure/
COPY src/Atlas.Api/Atlas.Api.csproj src/Atlas.Api/
COPY src/Atlas.Worker/Atlas.Worker.csproj src/Atlas.Worker/
RUN dotnet restore src/Atlas.Api/Atlas.Api.csproj

# Now the real source. .dockerignore (repo root) keeps bin/, obj/, .vs/,
# tests/, docs/ and similar out of the build context entirely, so this COPY
# is fast and never drags a Windows-built bin/obj folder into a Linux image.
COPY src/ src/

# --no-restore: the restore above already resolved every package; redoing it
# here would just repeat the slow, cache-defeating step this two-phase COPY
# was built to avoid. -c Release matches azure-pipelines.yml's own
# buildConfiguration, so the container image and the CI/CD artifact are
# built the same way. -o /app/publish is an arbitrary but conventional path —
# it just has to match the COPY --from=build line in the "final" stage below.
RUN dotnet publish src/Atlas.Api/Atlas.Api.csproj -c Release --no-restore -o /app/publish

# ---- Stage 2: final (runtime) ---------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# .NET's own Linux container images have listened on port 8080 by default
# since .NET 8 (via the ASPNETCORE_HTTP_PORTS environment variable baked
# into the base image) — no ASPNETCORE_URLS override needed here. EXPOSE is
# documentation for anyone reading this file / running `docker inspect`;
# docker-compose.yml's own `ports:` mapping is what actually makes port 8080
# reachable from the host.
EXPOSE 8080

# The aspnet base image already ships a non-root "app" user (uid 64198) for
# exactly this purpose — running the container process as root inside the
# container is an avoidable privilege an attacker who compromises the app
# would otherwise inherit for free, even though container isolation limits
# what that root actually means on the host itself.
USER app

COPY --from=build /app/publish .

# The array form (not `ENTRYPOINT dotnet Atlas.Api.dll`) runs `dotnet`
# directly as PID 1 rather than wrapping it in `/bin/sh -c "..."` — PID 1
# is what receives SIGTERM when `docker stop`/`docker compose down` runs,
# and ASP.NET Core's own graceful-shutdown handling (finishing in-flight
# requests, and Program.cs's own `finally { await Log.CloseAndFlushAsync(); }`
# flushing buffered Serilog events) only gets that signal directly in the
# array form — a shell wrapper would swallow it and force a slower, harder
# kill after Docker's shutdown timeout instead.
ENTRYPOINT ["dotnet", "Atlas.Api.dll"]
