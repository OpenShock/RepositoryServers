# OpenShock Repository Server

## Project Overview
Unified repository server for distributing OpenShock desktop app modules and firmware OTA updates. ASP.NET Core Web API with PostgreSQL/EF Core backend.

## Tech Stack
- .NET 10, ASP.NET Core (SlimBuilder)
- PostgreSQL via Npgsql + EF Core (with DbContext pooling)
- Serilog (console + Grafana Loki)
- OpenTelemetry (Prometheus exporter)
- API Versioning (Asp.Versioning) — URL-based: `/v1/`, `/v2/firmware/`
- FlexLabs.EntityFrameworkCore.Upsert for upserts
- MiniValidation for config validation
- Semver NuGet package for semver parsing
- TUnit for tests
- Scalar for API docs UI
- Docker (Alpine) with self-signed HTTPS cert generation

## Project Structure
```
RepositoryServer/                    # Main web API project
  AuthenticationHandlers/            # Custom AdminToken auth handler
  Config/                            # ApiConfig, DbConfig, RepoConfig, MetricsConfig, FirmwareConfig
  Controllers/
    OpenShockControllerBase.cs       # Shared base controller
    V1/                              # Desktop endpoints
      RepoController.cs             # GET /v1/ — desktop module manifest
      AdminController.cs            # /v1/admin/... — desktop module CRUD
    V2/
      Firmware/
        LatestController.cs          # GET /v2/firmware/latest/{channel}
        VersionsController.cs        # GET /v2/firmware/versions[/{version}]
        BoardsController.cs          # GET /v2/firmware/boards
        ChipsController.cs           # GET /v2/firmware/chips
        AdminController.cs           # /v2/firmware/admin/... CRUD
  Errors/                            # Static error factories (AuthResultError, ExceptionError)
  ExceptionHandler/                  # Global IExceptionHandler + RequestInfo logging
  Migrations/                        # EF Core migrations (Initial + AddFirmwareTables)
  Models/                            # Desktop module DTOs
  Models/Firmware/                   # Firmware DTOs (response + admin request)
  Problems/                          # RFC 7807 problem details (OpenShockProblem base)
  RepoServerDb/                      # EF Core entities + RepoServerContext
  Utils/                             # ByteArrayHexConverter, SemVersionConverter, etc.
  Program.cs                         # App startup and middleware pipeline

RepositoryServer.Tests/              # TUnit test project
docker/
  RepositoryServer.Dockerfile
  entrypoint.sh                      # Cert gen + `dotnet OpenShock.RepositoryServer.dll`
  appsettings.Container.json
```

## Namespace
`OpenShock.RepositoryServer` (renamed from `OpenShock.Desktop.RepositoryServer`)

## Key Patterns
- **Auth**: Custom `AdminTokenAuthentication` handler, `AuthSchemas.AdminToken` scheme
- **Error Handling**: `OpenShockProblem` base class → `ToObjectResult()` for RFC 7807
- **Config**: Bind root config to `ApiConfig`, validated with MiniValidator, registered as singleton
- **DB Migrations**: `MigrationOpenShockContext` subclass for migration tooling, auto-applied on startup
- **JSON**: CamelCase, custom `SemVersionConverter` + `ByteArrayHexConverter`
- **Upsert**: FlexLabs `.Upsert().On().RunAsync()` pattern
- **CORS**: Allow all origins/methods/headers
- **Metrics**: Prometheus at `/metrics`, restricted to private networks

## Database
- PostgreSQL, connection in `ApiConfig.Db.Conn`
- Desktop tables: `modules` (PK: id), `versions` (composite PK: version+module, FK→modules CASCADE)
- Firmware tables: `firmware_chips`, `firmware_boards`, `firmware_versions`, `firmware_artifacts`, `firmware_release_notes`
- Firmware enums: `firmware_channel`, `firmware_artifact_type`, `firmware_release_note_type`, `firmware_chip_architecture`
- URL convention for firmware artifacts: `{CdnBaseUrl}/{version}/{boardId}/{artifactType}.bin`

## CI/CD
- `ci-build.yml`: Test + build Docker + promote image + deploy prod (on master)
- `ci-tag.yml`: Release builds on semver tags, multi-arch (amd64+arm64)
- Image: `ghcr.io/openshock/repository-server`
- GitOps dispatch: `update-repository-server-prod` → `openshock/kubernetes-cluster-gitops`

## Commands
- Build: `dotnet build RepositoryServer/RepositoryServer.csproj`
- Test: `dotnet test RepositoryServer.Tests/RepositoryServer.Tests.csproj`
- Docker: `docker build -f docker/RepositoryServer.Dockerfile .`
- Migration: `dotnet ef migrations add <Name> --project RepositoryServer`
