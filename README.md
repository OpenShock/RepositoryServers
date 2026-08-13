# OpenShock Repository Server

[![Documentation](https://img.shields.io/badge/docs-mkdocs-blue.svg)](https://openshock.org)
[![GitHub license](https://img.shields.io/github/license/openshock/repositoryserver.svg)](https://raw.githubusercontent.com/openshock/repositoryserver/master/LICENSE)
[![GitHub Sponsors](https://img.shields.io/badge/GitHub-Sponsors-ff69b4)](https://github.com/sponsors/openshock)
[![Discord](https://img.shields.io/discord/1078124408775901204)](https://discord.gg/openshock)

<table>
  <tr>
    <td>master</td>
    <td><a href="https://github.com/OpenShock/RepositoryServer/actions/workflows/ci-build.yml"><img src="https://github.com/OpenShock/RepositoryServer/actions/workflows/ci-build.yml/badge.svg?branch=master" alt="Build Status" /></a></td>
  </tr>
  <tr>
    <td>develop</td>
    <td><a href="https://github.com/OpenShock/RepositoryServer/actions/workflows/ci-build.yml"><img src="https://github.com/OpenShock/RepositoryServer/actions/workflows/ci-build.yml/badge.svg?branch=develop" alt="Build Status" /></a></td>
  </tr>
</table>

Distribution and ingestion server for OpenShock hub firmware and desktop modules.

It serves:

- Hub OTA updates, so an ESP32 can ask whether a newer build exists for its board and channel
- The board, chip and USB catalog the web flashtool needs before it can flash anything
- The desktop module index and module zip downloads

Publishing is done by GitHub Actions using short-lived OIDC tokens, so there are no long-lived CI
secrets.

### API Documentation

The Scalar API reference is served at `/scalar` on any running instance.

`firmware-api-spec.md` in this repository is the canonical contract for the firmware endpoints:
response shapes, caching rules, error codes and the release workflow. Consumers should conform to it.

# Configuration

The server can be configured using the following environment variables. Values may also be supplied
through `appsettings.Custom.json`, user secrets or command line arguments.

| Variable                              | Required | Default value                        | Allowed / Example value                                                                                    |
|---------------------------------------|----------|--------------------------------------|------------------------------------------------------------------------------------------------------------|
| `OPENSHOCK__DB__CONN`                 | x        |                                      | `Host=postgres-server-host;Port=5432;Database=repo-server;Username=openshock;Password=superSecurePassword` |
| `OPENSHOCK__DB__SKIPMIGRATION`        |          | `false`                              | `true`, `false`                                                                                            |
| `OPENSHOCK__DB__DEBUG`                |          | `false`                              | `true`, `false`                                                                                            |
| `OPENSHOCK__ADMINTOKEN`               | x        |                                      | `superSecureAdminToken`                                                                                    |
| `OPENSHOCK__CICD__AUDIENCE`           | x        | `openshock-repository-server`        | Audience that publishing workflows request their OIDC token for                                            |
| `OPENSHOCK__FIRMWARE__CDNBASEURL`     | x        | `https://cdn.openshock.app/firmware` | Public base URL firmware artifacts are served from                                                         |
| `OPENSHOCK__FIRMWARE__STORAGE__TYPE`  | x        | `Local`                              | `Local`, `S3`, `BunnyCdn`                                                                                  |
| `OPENSHOCK__FIRMWARE__STAGEDRELEASETTL`  |       | `01:00:00`                           | How long a release may sit in `staging` before it is aborted                                               |
| `OPENSHOCK__FIRMWARE__EDITINGRELEASETTL` |       | `7.00:00:00`                         | How long a release may sit in `editing` before it is aborted                                               |
| `OPENSHOCK__REPO__ID`                 | x        |                                      | `openshock-desktop-modules`                                                                                |
| `OPENSHOCK__REPO__NAME`               | x        |                                      | `OpenShock Modules`                                                                                        |
| `OPENSHOCK__REPO__AUTHOR`             | x        |                                      | `OpenShock`                                                                                                |
| `OPENSHOCK__REPO__HOMEPAGE`           |          |                                      | `https://openshock.org`                                                                                    |
| `OPENSHOCK__REPO__CDNBASEURL`         | x        |                                      | Public base URL desktop module zips are served from                                                        |
| `OPENSHOCK__METRICS__ALLOWEDNETWORKS` |          | private networks                     | CIDRs allowed to scrape `/metrics`                                                                         |

Refer to the [Npgsql Connection String](https://www.npgsql.org/doc/connection-string-parameters.html)
documentation page for details about `OPENSHOCK__DB__CONN`.

Configuration is validated on startup. If a required value is missing or invalid, the offending
fields are printed and the process exits with code `-10`.

## Storage

Firmware artifacts and module zips are written to one of three backends, selected with
`OPENSHOCK__FIRMWARE__STORAGE__TYPE`. The server only ever writes. Serving the files is the job of
whatever CDN sits in front of the storage, and clients use the absolute URLs returned by the API
instead of building paths themselves.

### Local

Writes to disk. Intended for development.

| Variable                                        | Required | Default value    | Allowed / Example value |
|-------------------------------------------------|----------|------------------|-------------------------|
| `OPENSHOCK__FIRMWARE__STORAGE__LOCAL__BASEPATH` | x        | `./cdn-storage`  | `/var/lib/repo-server`  |

### S3

Works with AWS S3 and any S3-compatible service such as Cloudflare R2 or MinIO.

| Variable                                          | Required | Default value | Allowed / Example value              |
|---------------------------------------------------|----------|---------------|--------------------------------------|
| `OPENSHOCK__FIRMWARE__STORAGE__S3__BUCKETNAME`    | x        |               | `openshock-firmware`                 |
| `OPENSHOCK__FIRMWARE__STORAGE__S3__ACCESSKEY`     | x        |               |                                      |
| `OPENSHOCK__FIRMWARE__STORAGE__S3__SECRETKEY`     | x        |               |                                      |
| `OPENSHOCK__FIRMWARE__STORAGE__S3__SERVICEURL`    |          |               | Set for non-AWS endpoints            |
| `OPENSHOCK__FIRMWARE__STORAGE__S3__REGION`        |          |               | `eu-central-1`, required for AWS S3  |
| `OPENSHOCK__FIRMWARE__STORAGE__S3__KEYPREFIX`     |          |               | `firmware/`                          |

### BunnyCdn

| Variable                                            | Required | Default value | Allowed / Example value              |
|-----------------------------------------------------|----------|---------------|--------------------------------------|
| `OPENSHOCK__FIRMWARE__STORAGE__BUNNYCDN__STORAGEURL` | x       |               | `https://storage.bunnycdn.com/myzone` |
| `OPENSHOCK__FIRMWARE__STORAGE__BUNNYCDN__APIKEY`     | x       |               |                                       |

## Logging

Everything under `Serilog` in `appsettings.json` is passed to Serilog. Console, Grafana Loki and
OpenTelemetry sinks are available. Prometheus metrics are exposed on `/metrics`, restricted to the
networks in `OPENSHOCK__METRICS__ALLOWEDNETWORKS`.

# Authentication

There are two schemes, and they do not overlap.

## Admin token

Every admin endpoint requires the `Authorization` header to equal `OPENSHOCK__ADMINTOKEN` exactly.
There is no `Bearer` prefix. This covers catalog management, the publish allowlist, Discord webhooks
and changelog fixes.

## GitHub OIDC

Publishing endpoints take a GitHub Actions OIDC token as a bearer token. The server verifies the
signature against GitHub's JWKS, checks the issuer and the configured audience, then looks up
`(provider, owner, repo)` in the `repositories` table. A workflow from an unregistered repository is
rejected, so registration is the authorization decision, not the token itself.

Registered repositories carry scopes, `publish_firmware` and `publish_modules`. A repository
registered with no scopes authenticates but cannot publish anything. Ownership is additionally
checked per object: a release records the repository that created it, and uploads, publish and abort
each re-check it.

Publishing workflow side:

```yaml
permissions:
  id-token: write

steps:
  - run: |
      TOKEN=$(curl -s -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
        "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=openshock-repository-server" | jq -r .value)
```

# Endpoints

## Firmware (v2)

| Endpoint                                            | Auth  | Purpose                                                            |
|-----------------------------------------------------|-------|--------------------------------------------------------------------|
| `GET /2/firmware/manifest`                          | none  | Channels, latest versions, boards, chips, USB filters, advisories  |
| `GET /2/firmware/latest/{channel}`                  | none  | Newest release on a channel, all boards                            |
| `GET /2/firmware/latest/{channel}/{board}?version=` | none  | Hub OTA check, returns 204 when already current                    |
| `GET /2/firmware/versions`                          | none  | Paginated version history                                          |
| `GET /2/firmware/versions/{version}[/{board}]`      | none  | A specific published version                                       |
| `GET /2/firmware/boards`, `GET /2/firmware/chips`   | none  | Catalog listings                                                   |
| `/2/firmware/releases/...`                          | OIDC  | Release ingestion                                                  |
| `/2/firmware/admin/...`                             | admin | Boards, chips, USB devices, serial filters, advisories, allowlist  |
| `/2/admin/discord-webhooks`                         | admin | Notification webhooks                                              |

Channels cascade, `stable` is visible to `beta`, and both are visible to `develop`, so a stable
release is also the newest thing a beta hub should be offered. Boards are addressed by name, the
PlatformIO env name a hub compiles in as `OPENSHOCK_FW_BOARD`. Board UUIDs are still accepted.

## Desktop modules (v1)

| Endpoint                                     | Auth  | Purpose                                     |
|----------------------------------------------|-------|---------------------------------------------|
| `GET /1/`                                    | none  | Repository index with every module version  |
| `PUT /1/cicd/modules/{id}/versions/{version}` | OIDC | Upload a module zip                         |
| `/1/admin/modules/...`                       | admin | Create and delete modules and versions      |

## Release workflow

Firmware releases are staged, then promoted. Artifacts are uploaded under an internal staging prefix
and copied to their published keys only on publish, so a published key is written exactly once and
aborting a release can never remove an object a live version is serving.

```
POST   /2/firmware/releases[?nofail]              init, parses the changelog markdown
PUT    /2/firmware/releases/{id}/boards/{board}   upload artifacts, multipart, 64 MB per request
POST   /2/firmware/releases/{id}/publish          promote to live
DELETE /2/firmware/releases/{id}                  abort and clean up
```

Uploads carry a `sha256` form field mapping each artifact type to its expected hash, and the whole
request is verified before anything is written. If the changelog fails to parse, init returns 400 and
the build fails. With `?nofail` the release is created in `editing` status instead and cannot be
published until a maintainer fixes the notes through the admin endpoint. Releases that outlive their
TTL are aborted by a background job that runs every five minutes.

See sections 5.2 and 5.3 of `firmware-api-spec.md` for request bodies and the changelog format.

# Deployment / Self Hosting

The stack is small:

- Postgres 17 as database
- One or multiple repository servers (container, `ghcr.io/openshock/repository-server`)
- Object storage or a CDN serving the artifacts the server writes

Build the image yourself with:

```bash
docker build -f docker/RepositoryServer.Dockerfile -t repository-server .
```

The entrypoint generates a self-signed certificate at `/defaultcert.pfx` if none is mounted, and the
container configuration binds Kestrel to `https://*:443`. Database migrations are applied on startup
unless `OPENSHOCK__DB__SKIPMIGRATION` is set, so do not run them by hand.

## Seeding a fresh instance

A new database has no chips and no boards, and release init rejects a board it does not know, so the
first CI publish will fail until the catalog exists.

```bash
REPO_SERVER_URL=https://repo.my-openshock-instance.net \
ADMIN_TOKEN=superSecureAdminToken \
PUBLISH_OWNER=OpenShock PUBLISH_REPO=firmware \
./scripts/seed-catalog.sh
```

Every call the script makes is idempotent, so re-running it after adding a board is safe. Board names
have to match the `[env:...]` names in the firmware repository's `platformio.ini` exactly, because
that is what a hub reports as its own board. `PUBLISH_SCOPES` defaults to `publish_firmware`, use
`publish_modules` for a desktop module repository, or both comma separated.

# Development

Requires the .NET SDK version pinned in `global.json` and a Postgres instance.

```bash
docker run -d --name repo-server-pg -p 5432:5432 \
  -e POSTGRES_DB=repo-server -e POSTGRES_USER=openshock -e POSTGRES_PASSWORD=openshock \
  postgres:17-alpine

dotnet run --project RepositoryServer
```

The Development profile listens on `http://*:5080`.

## Tests

```bash
dotnet test --project RepositoryServer.Tests/RepositoryServer.Tests.csproj
```

TUnit on the Microsoft Testing Platform. The integration suite starts a real Postgres container
through Testcontainers and applies the actual migrations against it, so Docker has to be available.

## Migrations

Migrations live in `RepositoryServer/Migrations` and target `MigrationOpenShockContext`. The
design-time factory connects to `Host=localhost;Database=repo-server;Username=openshock;Password=openshock`.

```bash
dotnet ef migrations add MyMigration --project RepositoryServer
```

## Support development!

You can support the OpenShock Dev Team here: [Sponsor OpenShock](https://github.com/sponsors/OpenShock)
