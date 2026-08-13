# OpenShock Repository Server

## Project Overview
Unified repository server for distributing OpenShock desktop app modules and firmware OTA updates. ASP.NET Core Web API with PostgreSQL/EF Core backend.

## Tech Stack
- .NET 10, ASP.NET Core (SlimBuilder)
- PostgreSQL via Npgsql + EF Core (with DbContext pooling)
- Serilog (console + Grafana Loki)
- OpenTelemetry (Prometheus exporter)
- API Versioning (Asp.Versioning) — URL-based: `/1/`, `/2/firmware/`
- FlexLabs.EntityFrameworkCore.Upsert for upserts
- MiniValidation for config validation
- Semver NuGet package for semver parsing
- OneOf for discriminated parser results
- TUnit for tests
- Scalar for API docs UI
- Docker (Alpine) with self-signed HTTPS cert generation

## Project Structure
```
RepositoryServer/                    # Main web API project
  AuthenticationHandlers/
    AdminTokenAuthentication.cs      # AdminToken scheme (static header compare)
    GitHubOidcAuthentication.cs      # JwtBearer + GitHub OIDC claim handling
                                     # auto-upserts repositories rows on first use
  Config/                            # ApiConfig, DbConfig, RepoConfig, MetricsConfig,
                                     # FirmwareConfig, CiCdConfig, FirmwareAdvisoryConfig
  Controllers/
    OpenShockControllerBase.cs       # Shared base controller
    V1/                              # Desktop module endpoints (V1)
      RepoController.cs              # GET /1/ — desktop module manifest
      AdminController.cs             # /1/admin/... — desktop module CRUD
      CiCdController.cs              # PUT /1/cicd/modules/{id}/versions/{v} — desktop zip upload
    V2/Firmware/                     # Firmware V2 endpoints
      ManifestController.cs          # GET /2/firmware/manifest (bootstrap payload)
      LatestController.cs            # GET /2/firmware/latest/{channel}[/{board}?version=]
      VersionsController.cs          # GET /2/firmware/versions[/{version}[/{board}]]
      BoardsController.cs            # GET /2/firmware/boards
      ChipsController.cs             # GET /2/firmware/chips
      ReleasesController.cs          # POST/PUT/DELETE /2/firmware/releases/... (CI/CD ingestion)
      Admin/                         # AdminToken-protected CRUD (split by concern)
        RepositoriesController.cs    # GET, DELETE /2/firmware/admin/repositories
        VersionsAdminController.cs   # DELETE /2/firmware/admin/versions/{v}
        BoardsAdminController.cs     # PUT, PATCH, DELETE /2/firmware/admin/boards/{id}
                                     # + USB attach/detach
        ChipsAdminController.cs      # PUT, DELETE /2/firmware/admin/chips/{id} + USB attach/detach
        ReleasesAdminController.cs   # PUT /2/firmware/admin/releases/{id}/changelog (fix)
        UsbDevicesController.cs      # PUT/GET/DELETE /2/firmware/admin/usb-devices
        UsbSerialFiltersController.cs # PUT/GET/DELETE /2/firmware/admin/usb-serial-filters
  Enums/                             # ReleaseChannel, FirmwareArtifactType, FirmwareChipArchitecture,
                                     # ReleaseNoteSectionType, ReleaseStatus, RepositoryProvider
  Errors/                            # AuthResultError, ExceptionError
  ExceptionHandler/                  # Global IExceptionHandler + RequestInfo logging
  Migrations/                        # EF Core migrations
  Models/                            # Desktop module DTOs
  Models/Firmware/                   # Firmware DTOs
    FirmwareManifestResponse, FirmwareReleaseDto, FirmwareVersionSummary,
    FirmwareBoardDetailDto, FirmwareChipRefDto, FirmwareBoardReleaseResponseDto,
    FirmwareSourceDto, FirmwareAdvisoryDto, FirmwareUsbDeviceDto,
    FirmwareUsbSerialFilterDto (+ admin variant), RepositoryDto, VersionListResponse,
    InitReleaseRequest (+ response), FixChangelogRequest, Upsert* requests
  Problems/                          # RFC 7807 problem details (OpenShockProblem base)
  RepoServerDb/                      # EF Core entities + RepoServerContext
    SourceRepository, FirmwareVersion, FirmwareRelease, FirmwareArtifact, FirmwareReleaseNote,
    FirmwareStagedArtifact, FirmwareStagedReleaseNote, FirmwareBoard, FirmwareChip,
    UsbDevice, UsbSerialFilter, FirmwareChipUsbDevice, FirmwareBoardUsbDevice
  Services/
    IStorageService (+ BunnyCdn / Local / S3 impls)
    IDiscordNotificationService + DiscordNotificationService (webhook, fire-and-forget)
    StagedReleaseCleanupService     # BackgroundService, PeriodicTimer 5 min, two TTLs
  Utils/
    ChangelogParser                  # Markdown → FirmwareReleaseNoteDto[] (OneOf-based)
    SourceUrlBuilder                 # provider-aware commit/ref/run URL construction
    FirmwareResponseMapper           # shared release/artifact response projections
    CacheControlAttribute            # filter per spec §8
    FirmwareArtifactFileNames, ByteArrayHexConverter, SemVersionConverter
  Program.cs                         # App startup: schemes, DbContextPool, HttpClient,
                                     # HostedService, storage, migrations, etc.

RepositoryServer.Tests/              # TUnit test project (references the main project)
  Utils/
    ChangelogParserTests
    SourceUrlBuilderTests
docker/
  RepositoryServer.Dockerfile
  entrypoint.sh                      # Cert gen + `dotnet OpenShock.RepositoryServer.dll`
  appsettings.Container.json
```

## Namespace
`OpenShock.RepositoryServer`

## Key Patterns
- **Auth**: Two schemes. `AdminTokenAuthentication` (header compare) for admin endpoints;
  JwtBearer + `GitHubOidcAuthentication.Configure(...)` for CI/CD endpoints. The OIDC hook
  validates a GitHub Actions OIDC JWT, then looks up a matching row in the `repositories`
  table and attaches `AuthSchemas.CiCdClaims` to the principal. **Unregistered repositories are
  rejected** — the table is the publish allowlist, and a valid OIDC token alone says nothing about
  which repo is calling. Onboarding is `PUT /2/firmware/admin/repositories` (admin token).
  Authorization does not stop there: release upload/publish/abort each re-check that the release
  belongs to the calling repository, and desktop modules carry an owning `repository_id`.
- **Error Handling**: `OpenShockProblem` base class → `ToObjectResult()` for RFC 7807
- **Config**: Bind root config to `ApiConfig`, validated with MiniValidator, registered as singleton
- **Background jobs**: `StagedReleaseCleanupService` (`BackgroundService` with `PeriodicTimer`)
  periodically aborts expired staging/editing releases per `FirmwareConfig.StagedReleaseTtl`
  and `EditingReleaseTtl`
- **DB Migrations**: `MigrationOpenShockContext` subclass for migration tooling, auto-applied on startup
- **JSON**: CamelCase, custom `SemVersionConverter` + `ByteArrayHexConverter`
- **Upsert**: FlexLabs `.Upsert().On().RunAsync()` pattern
- **Caching**: `CacheControlAttribute` action filter — public max-age on 2xx, no-store on errors
- **CORS**: Allow all origins/methods/headers
- **Metrics**: Prometheus at `/metrics`, restricted to private networks
- **Notifications**: Fire-and-forget Discord webhooks via `IDiscordNotificationService`.
  Targets live in the `discord_webhooks` table, each subscribing to specific events, managed via
  `/2/admin/discord-webhooks`. The URL is a credential so it is write-only — responses return a
  masked form. The service is a **singleton** that resolves its own `HttpClient` and `DbContext`:
  notifications outlive the request that triggers them, so capturing request-scoped ones dropped
  them non-deterministically. No subscribed webhooks → no-op.

## Database
- PostgreSQL (15+ required for `NULLS NOT DISTINCT` on `usb_serial_filters`), connection in `ApiConfig.Db.Conn`
- Shared table: `repositories` (PK: id, UNIQUE `(provider, owner, repo)`)
- Desktop tables: `modules` (PK: id), `versions` (composite PK: version+module, FK→modules CASCADE)
- Firmware tables: `firmware_chips`, `firmware_boards`, `firmware_versions`,
  `firmware_artifacts`, `firmware_release_notes`, `firmware_releases`,
  `firmware_staged_artifacts`, `firmware_staged_release_notes`
- USB catalog: `usb_devices`, `usb_serial_filters`, `firmware_chip_usb_devices`,
  `firmware_board_usb_devices`
- Firmware enums: `release_channel`, `firmware_artifact_type`, `firmware_release_note_type`,
  `firmware_chip_architecture`, `release_status`, `repository_provider`
- URL convention for firmware artifacts: `{CdnBaseUrl}/{version}/{boardId}/{artifactType}.bin`
  — the board **UUID**, because a storage key must be immutable. The board *name* is the public
  identifier everywhere it acts as a label (routes, response `boardId`, boards map key, ingestion,
  errors); see `Utils/FirmwareBoardLookup`, the single resolution chokepoint, and spec §4.2
- Uploads are staged under `_staging/{releaseId}/` and copied to the published key by
  `PublishRelease`, so a published key is written exactly once and abort/TTL cleanup can never
  name a live key (`Utils/FirmwareArtifactFileNames.BuildStagingPath`)
- The server is the CDN writer: firmware CI drives init → upload → publish rather than uploading
  to the CDN itself. `scripts/seed-catalog.sh` bootstraps chips, boards and the publish allowlist —
  a fresh database rejects every ingestion until it has run
- Source-traceability URLs (commit / ref / run) are built server-side per provider
  (`Utils/SourceUrlBuilder`) and never stored

## CI/CD
- `ci-build.yml`: Test + build Docker + promote image + deploy prod (on master)
- `ci-tag.yml`: Release builds on semver tags, multi-arch (amd64+arm64)
- Image: `ghcr.io/openshock/repository-server`
- GitOps dispatch: `update-repository-server-prod` → `openshock/kubernetes-cluster-gitops`

## Commands
- Build: `dotnet build RepositoryServer/RepositoryServer.csproj`
- Test: `dotnet test --project RepositoryServer.Tests/RepositoryServer.Tests.csproj`
  (Microsoft.Testing.Platform runner, opted in via `global.json`; the legacy positional
  project path is rejected — use `--project`)
- Docker: `docker build -f docker/RepositoryServer.Dockerfile .`
- Migration: `dotnet ef migrations add <Name> --project RepositoryServer`
