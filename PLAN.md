# Plan: Merge DesktopRepositoryServer into unified RepositoryServer + Add Firmware Support

## Status: ALL STEPS COMPLETED

All 8 steps have been implemented. Build succeeds with 0 errors, tests pass.

## Context

The OpenShock/RepositoryServers repo currently has a `DesktopRepositoryServer` for distributing desktop app modules. We need to:
1. Refactor it into a single unified `RepositoryServer` application (not separate apps per product)
2. Add firmware repository endpoints for OTA updates, frontend flashing, and release notes

The existing desktop server is a .NET 10 ASP.NET Core app with PostgreSQL/EF Core, admin token auth, RFC 7807 error handling, and Docker deployment.

**Routing decision:** Keep existing v1 desktop endpoints at `/v1/` unchanged for backwards compatibility. New v2 API introduces `/v2/desktop/` and `/v2/firmware/` prefixes.

**Channel strategy:** Explicit channel field set by admin when creating a firmware version (not derived from semver).

## Step 1: Rename and restructure the project [DONE]

Rename `DesktopRepositoryServer/` → `RepositoryServer/` and update namespaces.

**Files to rename/modify:**
- `DesktopRepositoryServer/` → `RepositoryServer/`
- `DesktopRepositoryServer.Tests/` → `RepositoryServer.Tests/`
- `RepositoryServers.slnx` — update project paths
- `docker/DesktopRepositoryServer.Dockerfile` → `docker/RepositoryServer.Dockerfile`
- `docker/entrypoint.sh` — update DLL name
- `.github/workflows/ci-build.yml` — update matrix entries
- `.github/workflows/ci-tag.yml` — update image names

**Namespace changes:**
- `OpenShock.Desktop.RepositoryServer` → `OpenShock.RepositoryServer`
- Update all `using` statements, `.csproj` RootNamespace/AssemblyName
- `appsettings.json` Serilog `Using` and `Override` sections

## Step 2: Reorganize controllers with versioned routing

```
RepositoryServer/
  Controllers/
    OpenShockControllerBase.cs          # Shared base (keep as-is, update namespace)
    V1/
      RepoController.cs                 # GET /v1/ — existing desktop manifest (unchanged behavior)
      AdminController.cs                # /v1/admin/... — existing desktop admin (unchanged behavior)
    V2/
      Desktop/
        RepoController.cs               # GET /v2/desktop/ — same as v1 but under new prefix
        AdminController.cs              # /v2/desktop/admin/...
      Firmware/
        LatestController.cs             # GET /v2/firmware/latest/{channel}
        VersionsController.cs           # GET /v2/firmware/versions[/{version}]
        ChipsController.cs              # GET /v2/firmware/chips
        AdminController.cs              # /v2/firmware/admin/versions/{version}
```

Route attributes:
- V1 controllers: `[ApiVersion("1.0")]` `[Route("/v{version:apiVersion}/")]` — unchanged
- V2 Desktop: `[ApiVersion("2.0")]` `[Route("/v{version:apiVersion}/desktop")]`
- V2 Firmware: `[ApiVersion("2.0")]` `[Route("/v{version:apiVersion}/firmware")]`

## Step 3: Add firmware database entities

New entities in `RepoServerDb/`:

**PostgreSQL Enums:**
- `firmware_channel`: "stable", "beta", "develop"
- `firmware_artifact_type`: "merged", "app", "bootloader", "partitions", "staticfs"
- `firmware_release_note_type`: "warning", "info", "breaking", "section"
- `firmware_chip_architecture`: "xtensa", "riscv"

**`FirmwareChip`** (table: `firmware_chips`)
- `Id` (PK, varchar(32): "ESP32", "ESP32-S2", "ESP32-S3", "ESP32-C3")
- `Name` (varchar(64), display name: "ESP32", "ESP32-S2", etc.)
- `Architecture` (firmware_chip_architecture enum, nullable)

**`FirmwareBoard`** (table: `firmware_boards`)
- `Id` (PK, varchar(64): "OpenShock-Core-V2", "OpenShock-Pishock-Plus")
- `ChipId` (FK → firmware_chips, NOT NULL)
- `Name` (varchar(128), display name: "OpenShock Core V2")
- `Discontinued` (bool, default false)

**`FirmwareVersion`** (table: `firmware_versions`)
- `Version` (PK, varchar(64), semver string)
- `Channel` (firmware_channel enum) — explicitly set by admin
- `ReleaseDate` (DateTimeOffset)
- `CommitHash` (varchar(40))
- `ReleaseUrl` (varchar(256), nullable) — GitHub release URL

**`FirmwareArtifact`** (table: `firmware_artifacts`)
- `Version` + `BoardId` + `ArtifactType` (composite PK)
- `Version` (FK → firmware_versions, CASCADE)
- `BoardId` (FK → firmware_boards, CASCADE)
- `ArtifactType` (firmware_artifact_type enum)
- `HashSha256` (bytea, NOT NULL)
- `FileSize` (int64, NOT NULL)

**`FirmwareReleaseNote`** (table: `firmware_release_notes`)
- `Version` + `Index` (composite PK)
- `Version` (FK → firmware_versions, CASCADE)
- `Index` (int, ordering)
- `Type` (firmware_release_note_type enum)
- `Title` (varchar(256), nullable — for section headers)
- `Content` (text, NOT NULL)

**URL Generation Convention:**
Binary URLs are generated as: `{cdnBaseUrl}/{version}/{boardId}/{artifactType}.bin`
Example: `https://cdn.openshock.app/firmware/1.5.0-rc.6/OpenShock-Core-V2/firmware.bin`

**EF Core Configuration:**
- Map enums using Npgsql's `HasPostgresEnum<T>()` in `OnModelCreating`
- Configure composite keys and cascade deletes
- Add to `RepoServerContext` as new DbSets alongside existing Module/Version

**Configuration (appsettings.json):**
```json
{
  "Firmware": {
    "CdnBaseUrl": "https://cdn.openshock.app/firmware"
  }
}
```

## Step 4: Add firmware models (DTOs)

New files in `Models/Firmware/`:

**Response DTOs:**
- `FirmwareLatestResponse.cs` — lightweight for OTA clients:
  - `Version` (string)
  - `Channel` (string)
  - `ReleaseDate` (DateTimeOffset)
  - `Artifacts` (Dictionary<string, FirmwareBoardArtifact>) — board ID → merged artifact only

- `FirmwareVersionResponse.cs` — full detail for version endpoint:
  - `Version` (string)
  - `Channel` (string)
  - `ReleaseDate` (DateTimeOffset)
  - `CommitHash` (string)
  - `ReleaseUrl` (string, nullable)
  - `Artifacts` (Dictionary<string, List<FirmwareArtifactDto>>) — board ID → all artifact types
  - `ReleaseNotes` (List<FirmwareReleaseNoteDto>)

- `FirmwareVersionSummary.cs` — for list endpoint:
  - `Version` (string)
  - `Channel` (string)
  - `ReleaseDate` (DateTimeOffset)

- `FirmwareBoardArtifact.cs` — single artifact (for latest endpoint):
  - `Url` (string) — generated from convention
  - `Sha256Hash` (string)
  - `FileSize` (long)

- `FirmwareArtifactDto.cs` — artifact with type (for version detail):
  - `Type` (string) — "merged", "app", etc.
  - `Url` (string) — generated from convention
  - `Sha256Hash` (string)
  - `FileSize` (long)

- `FirmwareReleaseNoteDto.cs` — release note item:
  - `Type` (string)
  - `Title` (string, nullable)
  - `Content` (string)

- `FirmwareBoardDto.cs` — board info:
  - `Id` (string)
  - `Name` (string)
  - `ChipId` (string)
  - `ChipName` (string)
  - `Discontinued` (bool)

- `FirmwareChipDto.cs` — chip info:
  - `Id` (string)
  - `Name` (string)
  - `Architecture` (string, nullable)

**Admin Request DTOs:**
- `CreateFirmwareVersionRequest.cs` — create/update version:
  - `Channel` (string)
  - `ReleaseDate` (DateTimeOffset)
  - `CommitHash` (string)
  - `ReleaseUrl` (string, nullable)
  - `Artifacts` (Dictionary<string, Dictionary<string, FirmwareArtifactUpload>>) — boardId → artifactType → metadata
  - `ReleaseNotes` (List<FirmwareReleaseNoteUpload>)

- `FirmwareArtifactUpload.cs` — artifact metadata (no URL):
  - `Sha256Hash` (string)
  - `FileSize` (long)

- `FirmwareReleaseNoteUpload.cs`:
  - `Type` (string)
  - `Title` (string, nullable)
  - `Content` (string)

- `CreateFirmwareBoardRequest.cs`:
  - `Id` (string)
  - `Name` (string)
  - `ChipId` (string)

- `CreateFirmwareChipRequest.cs`:
  - `Id` (string)
  - `Name` (string)
  - `Architecture` (string, nullable)

## Step 5: Add firmware controllers

**Public Endpoints:**

**`LatestController`** — `GET /v2/firmware/latest/{channel}`
- Returns latest version for a channel with **merged artifacts only** (for OTA)
- Query: `firmware_versions` WHERE channel = X, ORDER BY release_date DESC, LIMIT 1
- JOIN `firmware_artifacts` WHERE artifact_type = 'merged'
- Response: `FirmwareLatestResponse` with board → merged artifact map
- Lightweight, cacheable, optimized for OTA clients polling for updates

**`VersionsController`**
- `GET /v2/firmware/versions?channel={channel}` — version list, optional channel filter
  - Returns: List<FirmwareVersionSummary>
- `GET /v2/firmware/versions/{version}` — full metadata
  - Returns: `FirmwareVersionResponse` with **all artifact types**, release notes
  - JOIN artifacts, boards, chips to include full board info

**`BoardsController`** — `GET /v2/firmware/boards`
- Query params: `?chipId={chipId}`, `?includeDiscontinued=false`
- Returns: List<FirmwareBoardDto> with chip info joined
- Used by frontend for device selection UI

**`ChipsController`** — `GET /v2/firmware/chips`
- Returns: List<FirmwareChipDto>
- Returns all chips (from firmware_chips table)

**Admin Endpoints:**

**`AdminController`** — `/v2/firmware/admin`

Version Management:
- `PUT /v2/firmware/admin/versions/{version}` — upsert version + artifacts + notes
  - Transactional: replaces all artifacts and notes for the version
  - Validates: board IDs exist, channel is valid, semver format
  - Body: `CreateFirmwareVersionRequest`
- `DELETE /v2/firmware/admin/versions/{version}` — cascade delete version

Board Management:
- `GET /v2/firmware/admin/boards` — list all boards (including discontinued)
- `PUT /v2/firmware/admin/boards/{id}` — upsert board
  - Body: `CreateFirmwareBoardRequest`
  - Validates: chip ID exists
- `PATCH /v2/firmware/admin/boards/{id}/discontinue` — set discontinued=true
- `DELETE /v2/firmware/admin/boards/{id}` — delete board (fails if artifacts exist)

Chip Management:
- `GET /v2/firmware/admin/chips` — list all chips
- `PUT /v2/firmware/admin/chips/{id}` — upsert chip
  - Body: `CreateFirmwareChipRequest`
- `DELETE /v2/firmware/admin/chips/{id}` — delete chip (fails if boards exist)

## Step 6: Add firmware problem types

`Problems/FirmwareError.cs`:
- `FirmwareVersionNotFound` (404)
- `FirmwareBoardNotFound` (404)
- `FirmwareChipNotFound` (404)
- `FirmwareInvalidChannel` (400)
- `FirmwareInvalidSemver` (400)
- `FirmwareInvalidArtifactType` (400)
- `FirmwareBoardInUse` (409) — cannot delete board with artifacts
- `FirmwareChipInUse` (409) — cannot delete chip with boards

## Step 7: EF Core migration

```bash
dotnet ef migrations add AddFirmwareTables --project RepositoryServer
```

**Migration should include:**
- CREATE TYPE statements for PostgreSQL enums (firmware_channel, firmware_artifact_type, etc.)
- CREATE TABLE statements for chips, boards, versions, artifacts, release_notes
- Foreign key constraints with CASCADE deletes
- Composite primary keys
- Indexes on commonly queried fields (channel, release_date, board_id)

## Step 8: Update Docker and CI

- `docker/RepositoryServer.Dockerfile` — update COPY paths and DLL name
- `docker/entrypoint.sh` — `exec dotnet OpenShock.RepositoryServer.dll`
- `ci-build.yml` — update matrix: dockerfile, image name, test project path
- `ci-tag.yml` — update image name to `repository-server`
- Update deploy dispatch event type to `update-repository-server-prod`

## Verification

1. `dotnet build` — project compiles
2. `dotnet test` — existing tests pass
3. Existing v1 desktop endpoint still works at `/v1/`
4. New v2 desktop endpoints respond at `/v2/desktop/`
5. New v2 firmware endpoints respond (empty results / proper 404s):
   - `GET /v2/firmware/chips` returns empty array
   - `GET /v2/firmware/boards` returns empty array
   - `GET /v2/firmware/versions` returns empty array
   - `GET /v2/firmware/latest/stable` returns 404 FirmwareVersionNotFound
6. Admin can create chips, boards, versions:
   - `PUT /v2/firmware/admin/chips/ESP32` succeeds
   - `PUT /v2/firmware/admin/boards/OpenShock-Core-V2` succeeds
   - `PUT /v2/firmware/admin/versions/1.0.0` succeeds
7. URL generation works correctly:
   - Artifact URLs follow convention: `{cdnBase}/{version}/{boardId}/{type}.bin`
   - Configuration value from appsettings is used
8. Docker build: `docker build -f docker/RepositoryServer.Dockerfile .`
9. PostgreSQL enums are created correctly in database
