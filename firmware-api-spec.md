# Firmware API Specification

Canonical contract for the repository server's firmware endpoints. All consumers must conform to the types and behaviors defined here.

## 1. Consumers

| Consumer | Primary endpoints | Constraints |
|----------|-------------------|-------------|
| **Web flashtool** (frontend) | manifest, latest, versions | Needs chip name for esptool-js, board discontinuation status, all artifact types for flashing |
| **ESP32 hub OTA** | latest/{channel}/{board}, versions/{version}/{board} | Constrained device. Needs artifact URLs + hashes for its board. Knows its own board name and version. |
| **Backend API** (server-to-server) | latest, versions | Structured data for programmatic use |
| **CLI / external tools** | all public endpoints | Structured data, scriptable, may automate flashing or version queries |
| **CI/CD pipeline** | releases (ingestion) | Publishes new firmware versions with artifacts and markdown changelog |

## 2. Constants

### Channels

Fixed set, hardcoded across all consumers:

```
stable | beta | develop
```

### Artifact types

```
merged | app | bootloader | partitions | staticfs
```

### Release note section types

```
breaking | warning | info | section
```

`section` is a catch-all for custom headings (e.g. "Features", "Performance"). The `title` field carries the heading name.

### Release status

```
staging | editing | published | archived | aborted
```

Shared workflow status for staged releases. Not firmware-specific — desktop modules may adopt the same staging workflow in the future. In the database, the PostgreSQL enum is `release_status`.

- `staging` — CI/CD is actively creating the release and uploading artifacts. Changelog is valid.
- `editing` — Release notes need manual review or refinement before publish. Set when changelog validation fails with `?nofail`, or when a maintainer wants to restructure notes before releasing. Artifact uploads still work in this state.
- `published` — Live. Immutable.
- `archived` — Pushed to cold storage. Artifacts are no longer served from the CDN but are retained for historical record. Can be restored to `published` if needed. *(Future feature — not yet implemented.)*
- `aborted` — Cleaned up (manually or by the cleanup job).

### Version format

Standard semver: `1.5.1`, `1.5.1-beta.3`, `1.5.1-develop+21e43623`

Parsed with `Semver.SemVersion` (strict mode). Build metadata (`+...`) is stored but ignored in comparisons.

---

## 3. Shared Types

All JSON uses **camelCase** property names. Enums are serialized as **lowercase strings**. Dates are **ISO 8601** with timezone. All database primary keys are **UUIDs** (serialized as lowercase hyphenated strings, e.g. `"550e8400-e29b-41d4-a716-446655440000"`).

### FirmwareArtifact

```jsonc
{
  "type": "merged",              // artifact type (lowercase enum)
  "url": "https://...",          // absolute CDN URL
  "sha256Hash": "a1b2c3d4...",  // hex-encoded SHA-256
  "fileSize": 1572864           // bytes
}
```

CDN URL format: `{cdnBaseUrl}/{version}/{boardId}/{filename}` — the board **UUID**, not its name.
The name is the public identifier everywhere it acts as a label, but a storage key has to be immutable:
published versions never change, so a renameable path segment would strand artifacts. Consumers treat
`url` as opaque and never reconstruct it.

Filenames by type: `merged` → `firmware.bin`, `app` → `app.bin`, `bootloader` → `bootloader.bin`, `partitions` → `partitions.bin`, `staticfs` → `staticfs.bin`

### FirmwareReleaseNote

```jsonc
{
  "type": "breaking",            // section type (lowercase enum)
  "title": "Config format",     // optional — heading for the note
  "content": "Changed config format to TOML"
}
```

Notes are ordered. The array preserves the order from the original changelog submission.

### FirmwareChipRef

Embedded chip reference within board responses. The `name` field must match esptool-js chip identifiers exactly (e.g. `"ESP32"`, `"ESP32-S3"`, `"ESP32-C3"`).

```jsonc
{
  "name": "ESP32-S3"    // public identifier, matches esptool-js chip name exactly
}
```

The chip's UUID is internal and never exposed. `name` is unique and externally pinned by esptool-js,
and no chip ever appears in a storage path, so a surrogate key buys nothing on the public surface.

### FirmwareBoardDetail

Per-board data within a release response. Includes chip info, discontinuation status, and all artifacts for that board in the given version.

```jsonc
{
  "chip": {
    "name": "ESP32-S3"
  },
  "discontinued": false,          // true = board is EOL, still served
  "artifacts": [
    { "type": "merged", "url": "...", "sha256Hash": "...", "fileSize": 1572864 },
    { "type": "app", "url": "...", "sha256Hash": "...", "fileSize": 1048576 }
  ]
}
```

Discontinued boards are **always included** in responses. Consumers decide whether to show or hide them.

### Repository

Reference to a source code repository. Stored in the `repositories` table and referenced by ID elsewhere.

**Shared infrastructure**: The `repositories` table is not firmware-specific. It is shared across all domains — both firmware versions and desktop module versions reference it for source traceability. The DB entity is named `SourceRepository` (to avoid collision with the desktop manifest DTO) but the table is `repositories`.

```jsonc
{
  "id": "a3f1b2c4-5d6e-7f8a-9b0c-1d2e3f4a5b6c",
  "provider": "github",
  "owner": "openshock",
  "repo": "firmware"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | guid | yes | DB primary key |
| `provider` | string | yes | `"github"` (extensible to `"gitlab"`, etc.) |
| `owner` | string | yes | Organization or user (e.g. `"openshock"`) |
| `repo` | string | yes | Repository name (e.g. `"firmware"`) |

Unique constraint on `(provider, owner, repo)`.

### FirmwareSource

Source traceability for a firmware release. References a repository and includes build-specific fields plus server-constructed URLs.

```jsonc
{
  "repository": {
    "id": "a3f1b2c4-5d6e-7f8a-9b0c-1d2e3f4a5b6c",
    "provider": "github",
    "owner": "openshock",
    "repo": "firmware"
  },
  "commitHash": "21e43623abcdef1234567890abcdef1234567890",
  "ref": "refs/tags/v1.5.1",
  "runId": "12345678901",
  "commitUrl": "https://github.com/openshock/firmware/commit/21e43623abcdef1234567890abcdef1234567890",
  "refUrl": "https://github.com/openshock/firmware/releases/tag/v1.5.1",
  "runUrl": "https://github.com/openshock/firmware/actions/runs/12345678901"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `repository` | Repository | yes | Source repository |
| `commitHash` | string | yes | Full 40-char commit SHA |
| `ref` | string | no | Git ref (`refs/tags/v1.5.1`, `refs/heads/main`) |
| `runId` | string | no | CI/CD run identifier |
| `commitUrl` | string | yes | Full URL to the commit (server-constructed) |
| `refUrl` | string | no | Full URL to the tag/branch (server-constructed, present when `ref` is set) |
| `runUrl` | string | no | Full URL to the CI run (server-constructed, present when `runId` is set) |

**URL construction per provider** (server-side):

| Provider | commitUrl | refUrl (tag) | runUrl |
|----------|-----------|-------------|--------|
| `github` | `https://github.com/{owner}/{repo}/commit/{hash}` | `https://github.com/{owner}/{repo}/releases/tag/{tag}` | `https://github.com/{owner}/{repo}/actions/runs/{runId}` |

**DB storage**: `firmware_versions` stores `repository_id` (FK → `repositories`), `commit_hash`, `ref`, `run_id`. URLs are constructed at response time, never stored.

**Desktop modules**: The desktop `versions` table stores the same source fields (`repository_id`, `commit_hash`, `ref`, `run_id`). Desktop modules populated via CI/CD (`/v1/cicd/`) extract these from GitHub OIDC claims, same as firmware. The `FirmwareSource` response type is firmware-specific, but the underlying stored fields are a shared pattern.

### FirmwareRelease

Full release details including release notes. Used by both the latest endpoint and the specific version endpoint — same type, same shape. This is the rich response for frontends and API consumers.

```jsonc
{
  "version": "1.5.1",
  "channel": "stable",
  "releaseDate": "2026-04-15T00:00:00Z",
  "source": {
    "repository": { "id": "a3f1b2c4-...", "provider": "github", "owner": "openshock", "repo": "firmware" },
    "commitHash": "21e43623abcdef1234567890abcdef1234567890",
    "ref": "refs/tags/v1.5.1",
    "runId": "12345678901",
    "commitUrl": "https://github.com/openshock/firmware/commit/21e43623abcdef1234567890abcdef1234567890",
    "refUrl": "https://github.com/openshock/firmware/releases/tag/v1.5.1",
    "runUrl": "https://github.com/openshock/firmware/actions/runs/12345678901"
  },
  "releaseNotes": [
    { "type": "breaking", "title": "Config format", "content": "Changed config format to TOML\nMultiple lines of content are concatenated into a single note." },
    { "type": "warning", "content": "Requires hub reset after update" },
    { "type": "info", "content": "Fixed WiFi reconnection" },
    { "type": "info", "content": "Improved battery life" }
  ],
  "boards": {
    "Wemos-D1-Mini-ESP32": {
      "chip": { "id": "esp32", "name": "ESP32" },
      "discontinued": false,
      "artifacts": [
        { "type": "merged", "url": "https://firmware.openshock.org/1.5.1/3f1a5c8e-9d24-4b70-8e11-2c6b7a0f4d93/firmware.bin", "sha256Hash": "a1b2c3...", "fileSize": 1572864 }
      ]
    },
    "Pishock-Lite-2021": {
      "chip": { "id": "esp32", "name": "ESP32" },
      "discontinued": true,
      "artifacts": [
        { "type": "merged", "url": "https://firmware.openshock.org/1.5.1/Pishock-Lite-2021/firmware.bin", "sha256Hash": "d4e5f6...", "fileSize": 1572864 }
      ]
    }
  }
}
```

### FirmwareVersionSummary

Version info for paginated lists. Includes source traceability and release notes so consumers don't need per-version requests.

```jsonc
{
  "version": "1.5.1",
  "channel": "stable",
  "releaseDate": "2026-04-15T00:00:00Z",
  "source": {
    "repository": { "id": "a3f1b2c4-...", "provider": "github", "owner": "openshock", "repo": "firmware" },
    "commitHash": "21e43623abcdef1234567890abcdef1234567890",
    "ref": "refs/tags/v1.5.1",
    "runId": "12345678901",
    "commitUrl": "https://github.com/openshock/firmware/commit/21e43623abcdef1234567890abcdef1234567890",
    "refUrl": "https://github.com/openshock/firmware/releases/tag/v1.5.1",
    "runUrl": "https://github.com/openshock/firmware/actions/runs/12345678901"
  },
  "releaseNotes": [
    { "type": "breaking", "title": "Config format", "content": "Changed config format to TOML\nMultiple lines of content are concatenated into a single note." },
    { "type": "info", "content": "Fixed WiFi reconnection" }
  ]
}
```

### FirmwareBoardReleaseResponse

Minimal response for a single board in a single version. No release notes, no chip info, no other boards — just what the firmware needs to download and flash.

`boardId` is the board's canonical **name**, echoed back as stored — see §4.2 for why the name is the
public identifier. The board's UUID appears only inside artifact `url`s, as the immutable storage key.

```jsonc
{
  "version": "1.5.1",
  "boardId": "Wemos-D1-Mini-ESP32",
  "artifacts": [
    { "type": "merged",   "url": "https://firmware.openshock.org/1.5.1/3f1a5c8e-9d24-4b70-8e11-2c6b7a0f4d93/firmware.bin", "sha256Hash": "a1b2c3...", "fileSize": 1572864 },
    { "type": "app",      "url": "https://firmware.openshock.org/1.5.1/3f1a5c8e-9d24-4b70-8e11-2c6b7a0f4d93/app.bin",      "sha256Hash": "d4e5f6...", "fileSize": 1048576 },
    { "type": "staticfs", "url": "https://firmware.openshock.org/1.5.1/3f1a5c8e-9d24-4b70-8e11-2c6b7a0f4d93/staticfs.bin", "sha256Hash": "7a8b9c...", "fileSize":  262144 }
  ]
}
```

Consumers should treat `url` as opaque and use it verbatim rather than reconstructing the path.

### FirmwareManifestResponse

Bootstrap payload. Aggregates channels, latest versions, boards, chips, USB device filters, and security advisories.

```jsonc
{
  "channels": ["stable", "beta", "develop"],
  "latest": {
    "stable": "1.5.1",
    "beta": "1.6.0-beta.3",
    "develop": "1.6.0-develop+21e4"
  },
  "boards": [
    {
      "id": "OpenShock-Core-V1",
      "name": "OpenShock Core V1",
      "chipId": "esp32s3",
      "chipName": "ESP32-S3",
      "discontinued": false,
      "usbDevices": [
        { "id": "b9e1...", "vid": 6790, "pid": 29970, "name": "CH9102" }
      ]
    }
  ],
  "chips": [
    {
      "id": "esp32s3",
      "name": "ESP32-S3",
      "architecture": "xtensa",
      "usbDevices": [
        { "id": "77ac...", "vid": 12346, "pid": 4097, "name": "ESP32-S3 USB-JTAG" }
      ]
    }
  ],
  "usbSerialFilters": [
    { "vid": 6790 },
    { "vid": 4292 },
    { "vid": 12346, "pid": 4097 }
  ],
  "usbDevices": [
    { "id": "b9e1...", "vid": 6790,  "pid": 29970, "name": "CH9102" },
    { "id": "c3d4...", "vid": 4292,  "pid": 60000, "name": "CP2102" },
    { "id": "77ac...", "vid": 12346, "pid": 4097,  "name": "ESP32-S3 USB-JTAG" }
  ],
  "advisories": [
    {
      "severity": "critical",
      "title": "OTA bricking bug",
      "content": "Versions before 1.4.0 have a bug that can brick the device during OTA. Update manually via USB.",
      "affectedVersions": "<1.4.0",
      "url": "https://github.com/openshock/firmware/issues/123"
    }
  ]
}
```

**Fields**:

- `channels` — ordered list of available release channels
- `latest` — map of channel → latest version string. Channels with no published versions are omitted
- `boards` — all boards (including discontinued). `FirmwareBoardDto[]`. Each board carries its own `usbDevices` array listing **board-specific** USB identities (on-board USB-serial chips soldered to the PCB, board-unique VID/PIDs)
- `chips` — all chips. `FirmwareChipDto[]`. Each chip carries its own `usbDevices` array listing **chip-level** USB identities (native-USB modes inherent to the silicon, e.g. ESP32-S3's USB-JTAG)
- `usbSerialFilters` — WebSerial filter rules. Each entry has a required `vid` and optional `pid` (omitted for vendor-wide filters). Frontends map these to `navigator.serial.requestPort({ filters })`: `{ usbVendorId: vid }` for vendor-wide matches, `{ usbVendorId: vid, usbProductId: pid }` for specific matches. Broader than `usbDevices` — may include vendor-wide entries covering devices not individually cataloged
- `usbDevices` — recognition catalog of specific VID+PID pairs mapped to human-readable names. Used to label connected ports (e.g. "you plugged in a CH9102"), not for filtering. Every entry has both `vid` and `pid`

**Board USB identity inheritance**: a board's full USB identity set is the union of `board.usbDevices` (board-specific) and `chips[board.chipId].usbDevices` (chip-inherited). The server does not merge these at response time — each is exposed on its own DTO so consumers can distinguish high-confidence board matches from low-confidence chip-family-only matches. For auto-detect, a match against `board.usbDevices` uniquely identifies the board; a match against only the chip's entries narrows to the chip family and the user must pick which board.
- `advisories` — security/compatibility warnings. `severity` is `critical`, `warning`, or `info`. `affectedVersions` is a semver range string. `url` is optional

### FirmwareAdvisory

```jsonc
{
  "severity": "critical",
  "title": "OTA bricking bug",
  "content": "Versions before 1.4.0 have a bug that can brick the device during OTA.",
  "affectedVersions": "<1.4.0",
  "url": "https://github.com/openshock/firmware/issues/123"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `severity` | string | yes | `critical`, `warning`, or `info` |
| `title` | string | yes | Short heading |
| `content` | string | yes | Human-readable description |
| `affectedVersions` | string | yes | Semver range (e.g. `"<1.4.0"`, `">=1.3.0 <1.4.2"`) |
| `url` | string | no | Link to issue or advisory |

### FirmwareUsbSerialFilter

WebSerial filter rule. Either a vendor-wide match (omit `pid`) or a specific device match (provide both). Used only by the frontend port picker.

```jsonc
{ "vid": 6790 }
```

```jsonc
{ "vid": 12346, "pid": 4097 }
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `vid` | int | yes | USB Vendor ID (decimal, 0-65535) |
| `pid` | int | no | USB Product ID (decimal, 0-65535). Omit for a vendor-wide filter |

Stored in the `usb_serial_filters` table with a unique constraint on `(vid, pid)` treating NULLs as equal — at most one vendor-wide row per VID.

### FirmwareUsbDevice

Recognition catalog entry. Always carries a specific `vid`+`pid` pair plus a human-readable name. Used for labeling connected ports and for linking USB identity to specific boards and chips.

```jsonc
{
  "id": "b9e1a2c3-4d5e-6f7a-8b9c-0d1e2f3a4b5c",
  "vid": 6790,
  "pid": 29970,
  "name": "CH9102"
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | guid | yes | DB primary key — referenced by board/chip junctions |
| `vid` | int | yes | USB Vendor ID (decimal, 0-65535) |
| `pid` | int | yes | USB Product ID (decimal, 0-65535) |
| `name` | string | yes | Human-readable chip/converter name |

Unique constraint on `(vid, pid)`. Linked to chips via `firmware_chip_usb_devices` and to boards via `firmware_board_usb_devices` (both M:N).

**Chip-level vs board-level links**:

- **Chip link** (`firmware_chip_usb_devices`) — native USB modes inherent to the silicon. Examples: ESP32-S3's USB-JTAG at `0x303A/0x1001`, ESP32-C3's USB-JTAG at `0x303A/0x1001`. Link once at the chip level and every board using that chip inherits it.
- **Board link** (`firmware_board_usb_devices`) — on-board USB-serial converters or board-unique VID/PIDs. Examples: PiShock's CH9102 at `0x1A86/0x7522`, a Wemos variant shipped with a CP2104. Describe what's soldered to this specific PCB.

A given USB device should be linked at the level it actually belongs to — never both. Do not re-attach a chip-level entry onto every board using that chip; boards inherit it by virtue of their `chipId`.

---

## 4. Public Endpoints

### 4.1. Manifest

```
GET /2/firmware/manifest
```

Bootstrap endpoint. Returns everything a consumer needs before making version-specific requests: channels, latest versions, boards, chips, USB device filters, and security advisories.

**200 OK** → `FirmwareManifestResponse`

**Cache**: `Cache-Control: public, max-age=300`

**Server implementation notes**:
- `channels` is the hardcoded channel list
- `latest` requires one query per channel (latest version by release date)
- `boards` and `chips` are queried from the database (boards include discontinued)
- Each board's inline `usbDevices` array is joined via `firmware_board_usb_devices` → `usb_devices` (board-specific only; chip-inherited entries are not duplicated here)
- Each chip's inline `usbDevices` array is joined via `firmware_chip_usb_devices` → `usb_devices` (chip-level native USB modes)
- `usbSerialFilters` is queried from the `usb_serial_filters` table
- `usbDevices` is queried from the `usb_devices` table
- `advisories` is served from server config (appsettings), not the database

---

### 4.2. Latest release

#### All boards

```
GET /2/firmware/latest/{channel}
```

Returns the most recent published release for a channel, with all boards, their artifacts, and release notes. For single-board queries, use `GET /latest/{channel}/{board}` instead.

| Param | Location | Required | Description |
|-------|----------|----------|-------------|
| `channel` | path | yes | `stable`, `beta`, or `develop` |

**200 OK** → `FirmwareRelease`

**400** → invalid channel

**404** → no versions exist for that channel

**Cache**: `Cache-Control: public, max-age=300`

**Channels cascade.** A request for a channel returns the newest release visible to that channel,
where `stable ⊆ beta ⊆ develop`:

| Requested | Considers |
|-----------|-----------|
| `stable`  | stable |
| `beta`    | stable, beta |
| `develop` | stable, beta, develop |

This matches how releases are actually produced: shipping a stable build advances the stable, beta and
develop pointers together, so a stable release *is* the newest thing a beta subscriber should receive.
Strict per-channel equality would pin a beta hub to the last explicit release candidate and offer it a
downgrade. The cascade applies to `/latest/*`, `/manifest`, and the `channel` filter on `/versions`.

**Ordering is a total order.** `release_date` is client-supplied and not unique, so it is never the sole
sort key. Ties break on `version` descending, which is the primary key. Without that tiebreaker
`/manifest`, `/latest/{channel}` and `/latest/{channel}/{board}` could disagree with each other and flip
between requests — a hub sees that as firmware flapping — and offset pagination could duplicate and drop
rows across pages.

**Server implementation notes**:
- Query: `FirmwareVersions` WHERE channel IN (visible channels), ORDER BY release_date DESC, version DESC, LIMIT 1
- Join: `FirmwareArtifact` → `FirmwareBoard` → `FirmwareChip` to populate chip ref and discontinued flag
- Include: `FirmwareReleaseNotes` ordered by index

#### Single board (lightweight)

```
GET /2/firmware/latest/{channel}/{board}?version={currentVersion}
```

Minimal response for a single board. No release notes, no other boards. Designed for ESP32 hubs. Supports an optional `version` query param — if the hub is already on the latest version, returns 204 instead.

| Param | Location | Required | Description |
|-------|----------|----------|-------------|
| `channel` | path | yes | `stable`, `beta`, or `develop` |
| `board` | path | yes | Board **name** (e.g. `"Wemos-D1-Mini-ESP32"`) or board UUID |
| `version` | query | no | Hub's current semver string. If provided and matches latest, returns 204 |

**Board identity**: the board **name** is the public identifier, everywhere — path segment, the
`boardId` field in responses, and the board segment of artifact URLs and storage keys. A hub sends its
name because that is the only thing it can know about itself: the name is its PlatformIO env, compiled
in as `OPENSHOCK_FW_BOARD`.

The board's UUID remains the internal primary key and foreign-key target, and is still accepted on this
path segment for admin tooling that already holds one. Name matching is case-insensitive; the canonical
stored spelling comes back in the response.

Board names are validated on create/update against `^[A-Za-z0-9][A-Za-z0-9._-]*$` so that they are safe
as URL path segments, and uniqueness is enforced case-insensitively via a unique index on `lower(name)`.
Without that index, `ESP32-Core` and `esp32-core` could coexist while resolution silently picked one —
the other could never receive an upload, and a hub compiled with its spelling would be served the wrong
board's firmware.

**Renaming is safe.** Storage keys use the board UUID, so a name is a pure label — renaming a board
changes what clients call it without touching a single stored object. This is the whole reason the
storage key is not the name.

**Aliases (planned)**: the resolution step is deliberately a single server-side chokepoint, so alternate
names can be added later without touching storage layout, response shapes, or hubs. An alias table maps
additional names onto a canonical board; resolution accepts any of them and always returns the canonical
name, and because consumers use `url` verbatim they never observe the difference. This — not renaming —
is the supported way to retire a board name: keep the canonical name, add an alias, and hubs compiled
with either keep updating.

**200 OK** → `FirmwareBoardReleaseResponse`

**204 No Content** → `version` was provided and matches the latest version (no update needed)

**400** → invalid channel

**404** → unknown board, no versions exist for that channel, or board has no artifacts in the latest version

**Cache**: `Cache-Control: public, max-age=300`

**Update logic**:
1. Find the latest version in the given channel (by release date, not semver order)
2. If `version` query param is provided, compare it against the latest version string
3. If **equal** → return 204
4. Otherwise (different, or no `version` param) → return 200 with the board's artifacts

String comparison (not semver) is intentional. This ensures rollbacks work correctly: if a version is pulled and an older version becomes "latest", hubs will update to it.

**Edge cases**:
- Board is discontinued: still serves artifacts (discontinued boards are always included)
- Board exists but has no artifacts for this version: return 404
- Hub sends a version that doesn't exist in the DB: still compares against latest, returns 200 if different

---

### 4.3. Version history

```
GET /2/firmware/versions?channel={channel}&limit={n}&offset={n}
```

Paginated list of published releases, newest first.

| Param | Location | Required | Default | Description |
|-------|----------|----------|---------|-------------|
| `channel` | query | no | all | Filter by channel |
| `limit` | query | no | 20 | Max results (clamped to 1-100) |
| `offset` | query | no | 0 | Skip N results |

**200 OK**

```jsonc
{
  "versions": [
    {
      "version": "1.5.1",
      "channel": "stable",
      "releaseDate": "2026-04-15T00:00:00Z",
      "source": {
        "repository": { "id": "a3f1b2c4-...", "provider": "github", "owner": "openshock", "repo": "firmware" },
        "commitHash": "21e43623...",
        "ref": "refs/tags/v1.5.1",
        "runId": "12345678901",
        "commitUrl": "https://github.com/openshock/firmware/commit/21e43623...",
        "refUrl": "https://github.com/openshock/firmware/releases/tag/v1.5.1",
        "runUrl": "https://github.com/openshock/firmware/actions/runs/12345678901"
      },
      "releaseNotes": [...]
    }
  ],
  "total": 42
}
```

`versions` is `FirmwareVersionSummary[]`. `total` is the unfiltered count for the query (before limit/offset), for pagination.

**400** → invalid channel

**Cache**: `Cache-Control: public, max-age=3600`

---

### 4.4. Specific version

#### All boards

```
GET /2/firmware/versions/{version}
```

Full release details for a specific version including release notes. Same response shape as the latest endpoint.

| Param | Location | Required | Description |
|-------|----------|----------|-------------|
| `version` | path | yes | Exact semver string |

**200 OK** → `FirmwareRelease`

**404** → version not found

**Cache**: `Cache-Control: public, max-age=86400, immutable`

Published versions are immutable — artifacts and release notes never change after publish.

#### Single board (lightweight)

```
GET /2/firmware/versions/{version}/{board}
```

Minimal response for a single board. No release notes, no other boards. Used by hubs for directed
updates, where the backend names the exact version to install.

| Param | Location | Required | Description |
|-------|----------|----------|-------------|
| `version` | path | yes | Exact semver string (e.g. `"1.5.1"`) |
| `board` | path | yes | Board name (e.g. `"Wemos-D1-Mini-ESP32"`), or board UUID — see §4.2 |

**200 OK** → `FirmwareBoardReleaseResponse`

**404** → version not found, board not found, or board has no artifacts in this version

**Cache**: `Cache-Control: public, max-age=86400, immutable`

---

## 5. CI/CD Ingestion

### 5.1. Authentication

All ingestion endpoints require a **GitHub OIDC token** passed as a Bearer token. No long-lived secrets.

```
Authorization: Bearer <github-oidc-jwt>
```

**Server validation**:
1. Verify JWT signature against GitHub's JWKS at `https://token.actions.githubusercontent.com/.well-known/jwks`
2. Validate standard claims: `iss` == `https://token.actions.githubusercontent.com`, `aud` == configured audience, token not expired
3. Extract source identity from claims: `repository_owner`, `repository`, `sha`, `ref`, `run_id`
4. Look up the `repositories` table for a matching `(provider="github", owner, repo)` entry
5. Reject if no matching repository exists — only pre-registered repositories can publish

**Registering allowed repositories**: Use the admin endpoint `PUT /2/firmware/admin/repositories` (see section 6) to add a repository to the DB. The OIDC handler matches incoming tokens against registered repositories — no config file changes needed to onboard a new source repo.

On successful auth, the matched `repository_id` and extracted claims (`sha`, `ref`, `run_id`) are attached to the request context for use by the release endpoints.

**Scopes**: registration records what a repository may publish — `publish_firmware`,
`publish_modules`, or both. Firmware and desktop ingestion share this one authentication scheme, so
without scopes a repository onboarded to publish desktop modules could also initialise and publish
firmware releases. A repository registered with no scopes is authenticated but cannot publish
anything. Scopes are entirely server-side: a publishing workflow neither sends nor sees them.

**Per-release ownership**: authentication identifies *a* registered repository, not *which* release it
may act on. Every registered repository presents an equally valid principal, so `InitRelease` records
the caller's `repository_id` on the release, and upload, publish and abort each re-check it and return
**403** on mismatch. Without that check any registered repository could inject artifacts into another's
in-flight release, publish it under that repository's identity and commit hash, or abort it.

The same rule applies to desktop module ingestion: a module carries an owning `repository_id`, and a
module with no owner assigned is closed to every publisher rather than open to any.

**Server config** (appsettings — only the OIDC audience, not the repo list):

```jsonc
{
  "CiCd": {
    "Audience": "openshock-repository-server"
  }
}
```

Deliberately not nested under `Firmware`: the scheme it configures also guards desktop module
publishing.

**What the audience is for**: it makes a token non-transferable between services. `id-token: write` is
granted per job, so any action in a publishing workflow can mint a token; without this check a token
obtained for an unrelated vendor would still be a valid publishing credential here, since it carries
the same `repository` claim. It is not a secret — anyone can request it by name — so it authenticates
nothing about *who* is calling. That is the allowlist's job.

**GitHub Actions workflow setup**:

```yaml
permissions:
  id-token: write

steps:
  - run: |
      TOKEN=$(curl -s -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
        "$ACTIONS_ID_TOKEN_REQUEST_URL&audience=https://repo.openshock.org" | jq -r .value)
      # Use $TOKEN as Bearer token for all /releases endpoints
```

### 5.2. Release workflow

Two-phase release process with staging, artifact upload, and publish. Abandoned staging releases are cleaned up automatically — see section 5.4.

```
POST   /2/firmware/releases[?nofail]                     → InitRelease (creates staging release)
PUT    /2/firmware/releases/{releaseId}/boards/{board}    → UploadBoardArtifacts (multipart)
POST   /2/firmware/releases/{releaseId}/publish           → PublishRelease (promotes to live)
DELETE /2/firmware/releases/{releaseId}                   → AbortRelease (cleanup)
```

#### InitRelease

```
POST /2/firmware/releases[?nofail]
```

```jsonc
{
  "version": "1.5.1",
  "channel": "stable",
  "releaseDate": "2026-04-15T00:00:00Z",
  "boards": ["OpenShock-Core-V1", "Wemos-D1-Mini-ESP32"],   // board names (UUIDs also accepted)
  "changelog": "### Breaking\n**Config format** — Changed to TOML\n\n### Warning\nRequires hub reset\n\n### Info\n- Fixed WiFi reconnection\n- Improved battery life"
}
```

The server populates `source` automatically from the OIDC token claims — CI does not send it. Fields extracted:
- `provider`: always `"github"` (determined by OIDC issuer)
- `owner`: from `repository_owner` claim
- `repo`: from `repository` claim (stripped of owner prefix)
- `commitHash`: from `sha` claim
- `ref`: from `ref` claim
- `runId`: from `run_id` claim

The `changelog` field replaces the current `releaseNotes` structured array. See section 5.3 for parsing rules.

**Changelog validation**:
1. Server parses the `changelog` markdown per section 5.3 rules
2. **If parsing succeeds**: release status = `staging`, return **201 Created**
3. **If parsing fails and `?nofail` is NOT present**: return **400** (`firmware/invalid-changelog`). CI/CD build fails.
4. **If parsing fails and `?nofail` IS present**: release status = `editing`. The release is created with best-effort release notes (whatever was parseable, or empty). Return **201 Created**. CI/CD can log a warning but the build proceeds. Artifact uploads work normally. Repo janitors must fix the changelog via the admin endpoint (section 6.5) before the release can be published.

**201 Created**:

```jsonc
{
  "id": "<guid>",
  "status": "staging"       // or "editing"
}
```

#### UploadBoardArtifacts

```
PUT /2/firmware/releases/{releaseId}/boards/{board}
Content-Type: multipart/form-data
```

Accepts uploads in both `staging` and `editing` status. Form fields: `app`, `staticfs`, `merged`, `bootloader`, `partitions` — each a binary file. Must include all artifact types required by the board's `requiredArtifactTypes` configuration.

**Uploads are staged, not published.** Bytes land under an internal `_staging/{releaseId}/` prefix and
are copied to their published key only by `PublishRelease`. A published key is therefore written exactly
once, and an aborted or expired release is deleted wholesale from its own prefix with no possibility of
removing an object a live version is serving. The `url` in the response is the artifact's *eventual*
published URL — it does not resolve until the release is published.

The whole request is verified before anything is written: every file is hashed and checked against the
manifest first, so a request containing one bad file leaves no partial state behind and does not disturb
artifacts already staged for that board.

Additionally, a required `sha256` form field must be included containing a JSON object mapping each artifact type to its expected SHA-256 hex hash:

```jsonc
// sha256 form field value (JSON string):
{
  "merged": "a1b2c3d4e5f6789...",
  "app": "f6e5d4c3b2a1987..."
}
```

**Server validation**:
- Compute SHA-256 of each uploaded file and compare against the corresponding entry in the `sha256` manifest
- If any hash mismatches: return **400** (`firmware/sha256-mismatch`) with detail listing the mismatched artifact types and expected vs actual hashes
- The manifest must have exactly the same keys as the uploaded files — extra or missing entries are also 400

Size limit: 64 MB per request.

#### PublishRelease

```
POST /2/firmware/releases/{releaseId}/publish
```

Validates all declared boards have their required artifacts uploaded, then atomically promotes staging data to the live `firmware_versions` / `firmware_artifacts` / `firmware_release_notes` tables.

**Pre-publish checks**:
- Release status must be `staging`. If `editing`, return **409** (`firmware/release-notes-not-finalized`). Repo janitors must fix the release notes via the admin endpoint (section 6.5) first, which transitions the release back to `staging`.
- All declared boards have their required artifacts uploaded

#### AbortRelease

```
DELETE /2/firmware/releases/{releaseId}
```

Cleans up CDN artifacts for all staged artifacts, then marks the release as aborted.

**204 No Content**

### 5.3. Changelog markdown format

CI/CD submits a single markdown string. The server parses it into structured `FirmwareReleaseNote` entries.

#### Input format

```markdown
### Breaking
**Config format** — Changed config format to TOML
Multiple lines of content are concatenated into a single note.

### Warning
Requires hub reset after update

### Info
- Fixed WiFi reconnection
- Improved battery life
- **OTA** — Added automatic rollback on failed flash

### Features
Custom sections become type "section" with the heading as title.
- New web dashboard
- Bluetooth pairing support
```

#### Parsing rules

1. **Split on headings**: Each `### Heading` starts a new section
2. **Map heading to type**:
   - `Breaking` → `breaking`
   - `Warning` → `warning`
   - `Info` → `info`
   - Anything else → `section` (with heading stored as `title`)
3. **Extract items within a section**:
   - Lines starting with `- ` are individual items (strip the `- ` prefix)
   - Indented bullets are flattened into siblings — the note type carries no nesting, so depth is
     discarded but content is preserved
   - Non-bullet lines ("prose") are joined with newlines into a single item
   - A section may contain both: the prose item is emitted first, then the bullet items
   - Empty lines are ignored
4. **Extract title from items**: If an item starts with `**Title**`, extract it:
   - `**Config format** — Changed to TOML` → `title: "Config format"`, `content: "Changed to TOML"`
   - `**OTA** — Added rollback` → `title: "OTA"`, `content: "Added rollback"`
   - `Fixed WiFi` → `title: null`, `content: "Fixed WiFi"`
5. **Ordering**: Notes are stored with sequential indices preserving the order: all items from the first section, then all items from the second section, etc.

#### Resulting structured data (stored in DB, returned in API)

```jsonc
[
  { "type": "breaking", "title": "Config format", "content": "Changed config format to TOML\nMultiple lines of content are concatenated into a single note." },
  { "type": "warning", "title": null, "content": "Requires hub reset after update" },
  { "type": "info", "title": null, "content": "Fixed WiFi reconnection" },
  { "type": "info", "title": null, "content": "Improved battery life" },
  { "type": "info", "title": "OTA", "content": "Added automatic rollback on failed flash" },
  { "type": "section", "title": "Features", "content": "Custom sections become type \"section\" with the heading as title." },
  { "type": "section", "title": "Features", "content": "New web dashboard" },
  { "type": "section", "title": "Features", "content": "Bluetooth pairing support" }
]
```

#### Validation errors

The changelog is considered **invalid** if any of the following are true:

- No `### Heading` found anywhere in the input
- All sections are empty (headings present but no content after any of them)
- The entire input is empty or whitespace-only

Any other well-formed markdown that produces at least one note is considered valid. Unexpected formatting within a section (e.g. bold text not matching the `**Title** — content` pattern) is treated as literal content, not a validation error.

### 5.4. Staged release cleanup

A background job automatically aborts and cleans up staging releases that have been open longer than their TTL.

**Configuration** (appsettings):

```jsonc
{
  "Firmware": {
    "StagedReleaseTtl": "01:00:00",
    "EditingReleaseTtl": "7.00:00:00"
  }
}
```

- `StagedReleaseTtl` — TTL for releases in `staging` status (default: 1 hour). These are normal CI releases where the pipeline abandoned or crashed before publish.
- `EditingReleaseTtl` — TTL for releases in `editing` status (default: 7 days). These are waiting for manual review by repo janitors.

**Behavior**:
1. Runs on a periodic timer (every 5 minutes)
2. Queries `FirmwareReleases` where `Status` is `staging` or `editing` and `CreatedAt + TTL < now`, using the appropriate TTL based on status
3. For each expired release: deletes CDN artifacts for all staged artifacts (same cleanup as AbortRelease), then sets `Status = Aborted`
4. Logs each cleanup action with the release ID, version, and reason (TTL expired)

**Interaction with AbortRelease**: The background job performs the same cleanup as a manual `DELETE /releases/{releaseId}`. If CI/CD calls AbortRelease before the TTL expires, the job has nothing to clean up.

---

## 6. Administration

**There are no admin endpoints.** Administration is the management UI at `/admin`, served by the
repository server itself and rendered on the server, so an unauthenticated visitor is turned away
before any markup is produced. Access requires a GitHub session in the configured admin team; see
the README for the login flow.

The UI covers:

The UI is grouped as shared, firmware and desktop. Publishers and Discord webhooks are shared because
both domains use them: the `repositories` table is the publish allowlist for firmware and desktop
alike, and both notify through the same webhooks.

| Page | Group | What it manages |
|------|-------|-----------------|
| `/admin` | shared | Status across both domains, including releases waiting on a changelog fix |
| `/admin/repositories` | shared | The publish allowlist and its scopes |
| `/admin/discord-webhooks` | shared | Notification targets |
| `/admin/firmware/releases` | firmware | Fixing changelogs on staged releases, unpublishing versions |
| `/admin/firmware/boards` | firmware | Boards, their chip, required artifact types, discontinuation, USB devices |
| `/admin/firmware/chips` | firmware | Chips and their native USB devices |
| `/admin/firmware/usb-devices` | firmware | The VID/PID recognition catalog |
| `/admin/firmware/usb-serial-filters` | firmware | WebSerial port picker rules |
| `/admin/firmware/advisories` | firmware | Advisories served on the manifest |
| `/admin/desktop/modules` | desktop | Desktop modules, their owning repository, and published versions |

The rules these enforce are unchanged, and live in the admin services rather than in controllers:

- A chip cannot be deleted while a board references it; a board cannot be deleted while it has
  published artifacts; a USB device cannot be deleted while a chip or board links to it.
- A repository cannot be deleted while a version or release records it as their source. Revoking a
  publisher that has already published means clearing its scopes, which keeps provenance intact.
- Board names are restricted to `^[A-Za-z0-9][A-Za-z0-9._-]*$` and are unique case-insensitively,
  because the name is a path segment in artifact URLs and storage keys.
- Registering a repository is idempotent on `(provider, owner, repo)` matched case-insensitively, and
  scopes are authoritative on re-registration.
- A changelog fix re-parses the markdown and returns the release to `staging`, which is the only
  status publish accepts.

If a machine-readable admin surface is needed later, it will be a separate automation API with its own
tokens, built on the same services rather than beside them.

---

## 7. Error Handling

All errors use RFC 7807 problem details via `OpenShockProblem`:

```jsonc
{
  "type": "firmware/version-not-found",
  "title": "Version Not Found",
  "status": 404,
  "detail": "Firmware version '1.5.2' does not exist"
}
```

| Status | Usage |
|--------|-------|
| 204 | Successful DELETE with no response body |
| 400 | Invalid parameters (bad channel, invalid semver, malformed request, invalid changelog, SHA-256 mismatch) |
| 404 | Resource not found (version, board, chip, channel with no versions) |
| 409 | Conflict (release already staging, board in use, chip in use, release notes not finalized) |
| 429 | Rate limited |
| 500 | Server error |

**Problem types specific to the release workflow**:

| Problem type | Status | Description |
|-------------|--------|-------------|
| `firmware/invalid-changelog` | 400 | Changelog markdown failed validation (see section 5.3) |
| `firmware/sha256-mismatch` | 400 | Uploaded artifact hash does not match the provided SHA-256 manifest |
| `firmware/release-notes-not-finalized` | 409 | Cannot publish: release is in `editing` status — release notes must be finalized first |

---

## 8. Caching Strategy

| Endpoint | Cache-Control | Rationale |
|----------|---------------|-----------|
| `GET /manifest` | `public, max-age=300` | Contains latest versions; same refresh cadence |
| `GET /latest/{channel}` | `public, max-age=300` | New releases are infrequent; 5 min staleness is acceptable |
| `GET /latest/{channel}/{board}` | `public, max-age=300` | Same cadence as latest |
| `GET /versions` | `public, max-age=3600` | Historical list changes rarely |
| `GET /versions/{version}` | `public, max-age=86400, immutable` | Published versions never change |
| `GET /versions/{version}/{board}` | `public, max-age=86400, immutable` | Published versions never change |

All cache headers apply to successful (2xx) responses only. Error responses should not be cached (`Cache-Control: no-store`).

---

## 9. Consumer Integration Notes

### 9.1. Web flashtool (frontend)

**Bootstrap**: Call `/manifest` on page load → get channels, latest versions, boards, chips, USB VID/PIDs, and advisories. Use `usbDevices` as WebSerial filters for `navigator.serial.requestPort({ filters })`. Use `boards` and `chips` to populate the board picker. Show `advisories` as banners.

**Primary flow**: Call `/latest/{channel}` → get `FirmwareRelease` → let user pick a board → use `chip.name` as the esptool-js chip target → download artifacts from URLs → flash.

**Key fields**:
- `boards[boardName].chip.name` — pass directly to esptool-js as the chip identifier
- `boards[boardName].discontinued` — show a warning badge or hide the board in the UI
- `boards[boardName].artifacts` — all artifact types needed for flashing (`merged` for USB flashing a blank device, individual parts for OTA)
- `releaseNotes` — render grouped by `type`, use `title` as a bold prefix when present

**Version picker**: Use `/versions?channel=stable&limit=20` for the version dropdown. Link "view details" to `/versions/{version}`.

### 9.2. ESP32 hub OTA

> **The hub must flash `app` + `staticfs`, never `merged`.** A `merged` artifact is a full-flash image
> built by `esptool merge_bin`, containing the bootloader at `0x1000`, the partition table at `0x8000`,
> the app at `0x10000` and the filesystem at its partition offset, padded to the full flash size. It is
> for USB flashing a blank device — writing it into an OTA app slot would write a bootloader image into
> the app partition and produce an unbootable slot. OTA is per-partition by construction.

**Primary flow (auto-update)**: On boot or periodic timer, call `/latest/{channel}/{board}?version={current}`.
On 204, nothing to do. On 200, take the `app` and `staticfs` artifacts and flash each into its own
partition — verify SHA-256 before flashing, then reboot.

**Directed update flow**: When the backend names a specific version to install, call
`/versions/{version}/{board}` and flash the same two artifacts.

**Implementation guidance**:
- The hub knows its own board name — its PlatformIO env, compiled in as `OPENSHOCK_FW_BOARD` — and its
  `channel`, which is runtime-configurable. Send the name as the `{board}` path segment; see §4.2.
- Parse: `version` (string), `boardId` (string), `artifacts[]`. Select by `type`: `app` → the next OTA
  app partition (`esp_ota_get_next_update_partition`), `staticfs` → the filesystem partition.
- Treat `url` as opaque. Do not rebuild it from `version` and board name — the CDN layout is not part of
  this contract.
- Verify `sha256Hash` over the downloaded bytes *before* marking a partition bootable.
- Use `fileSize` to bounds-check against partition size before writing, and to confirm the download ran
  to completion.
- Flash the filesystem first, then the app. The app partition is made bootable last, so a failure part
  way through leaves the previous app in place.
- On HTTP 204: already current, skip.
- On HTTP 404: board or channel unknown — log a warning, do not retry until the next cycle.
- On HTTP 4xx/5xx: transient — back off exponentially.

**Version comparison is a string compare, not a semver compare** — see §4.2. The hub sends its current
version and lets the server decide; it must not skip an update because the offered version sorts lower,
or rollbacks will not reach it.

**Memory budget**: The response is small — `artifacts` is typically 3-5 entries. Parse it with the
bundled cJSON rather than buffering a large document.

### 9.3. Backend API (server-to-server)

Uses the same public endpoints. Structured `releaseNotes` allow programmatic filtering (e.g. "are there any breaking changes?") without parsing markdown.

- Use `/latest/{channel}` to check what's deployed
- Use `/versions/{version}` for specific version queries
- `source` provides full traceability: commit, ref, CI run — consumers construct URLs from the structured fields

### 9.4. CLI / external tools

All endpoints return structured JSON suitable for `jq` pipelines and scripting.

Example workflows:
- `GET /latest/stable` | jq `.boards | keys[]` → list available boards
- `GET /versions?channel=stable&limit=5` → recent stable releases
- `GET /versions/1.5.1` | jq `.releaseNotes[] | select(.type == "breaking")` → breaking changes in a version
- `GET /latest/stable/MyBoard?version=1.4.0` → check if a specific board needs updating
- `GET /versions/1.5.1/MyBoard` → get artifacts for a specific version

---

## 10. Schema Evolution

### Adding fields

New fields may be added to any response type at any time. Consumers **must** ignore unknown fields. This is the primary extension mechanism.

### Removing / renaming fields

Never remove or rename a field within an API version. If a field must change, add the new field alongside the old one, document the deprecation, and remove the old field in the next major API version.

### New artifact types

New values may be added to the `artifact type` enum (e.g. `filesystem`, `nvs`). Consumers that don't recognize an artifact type should skip it rather than fail.

### New release note section types

New values may be added to the `release note section type` enum. Consumers should render unknown types using the `type` string as a heading, same as `section` type.

### New source providers

New `provider` values may be added to `FirmwareSource` (e.g. `"gitlab"`, `"gitea"`). Each provider requires a corresponding OIDC validation handler on the server and a new repository entry via the admin API. Consumers should handle unknown providers gracefully when constructing URLs (e.g. skip linking).

### New channels

If a new channel is added (unlikely), it requires coordinated changes across all consumers. The channel set is considered fixed.

### New release statuses

New values may be added to the `release_status` enum. `archived` is planned but not yet implemented. Consumers should treat unknown statuses as non-publishable.

### Cold storage (future)

Published releases may eventually be moved to `archived` status, transferring artifacts from the CDN to cold storage (e.g. S3 Glacier, cheap object tier). Archived versions would:

- Remain visible in `/versions` responses (with a flag or absent artifacts indicating archived state)
- Not be served by `/latest/{channel}` (only `published` versions are candidates)
- Be restorable to `published` via an admin endpoint, which copies artifacts back to the CDN
- Retain all metadata (release notes, source traceability) — only binary artifacts move

This affects both firmware and desktop module releases. The `archived` status is already reserved in the enum for this purpose.

---

## 11. Summary of Backend Changes Required

| Area | Change | Scope |
|------|--------|-------|
| New `ManifestController` | `GET /manifest` — aggregate channels, latest versions, boards, chips, USB devices, advisories from config + DB | New controller + DTOs |
| `FirmwareConfig` | New config section: `advisories` served from appsettings | Config |
| New `usb_devices` table | `id` (GUID), `vid`, `pid`, `name`, unique `(vid, pid)` — recognition catalog | DB + migration |
| New `usb_serial_filters` table | `id` (GUID), `vid`, `pid` (nullable), `description`, unique `(vid, pid)` NULLS NOT DISTINCT — WebSerial filter rules | DB + migration |
| New `firmware_chip_usb_devices` junction | M:N chip → usb_device, cascade on chip delete, restrict on usb_device delete | DB + migration |
| New `firmware_board_usb_devices` junction | M:N board → usb_device, cascade on board delete, restrict on usb_device delete | DB + migration |
| New `FirmwareUsbSerialFilter` DTO | `vid` + optional `pid` (manifest shape); admin shape also carries `id` + `description` | New DTO |
| `FirmwareUsbDevice` DTO | Add `id` field (GUID); recognition-only catalog entry (always has `vid`+`pid`+`name`) | DTO |
| `FirmwareManifestResponse` | Split `usbDevices` → `usbSerialFilters` + `usbDevices`; both queried from DB | DTO + controller |
| `FirmwareBoardDto` | Add inline `usbDevices: FirmwareUsbDevice[]` array (board-specific only) joined via `firmware_board_usb_devices` | DTO + controller |
| `FirmwareChipDto` | Add inline `usbDevices: FirmwareUsbDevice[]` array (chip-level native USB modes) joined via `firmware_chip_usb_devices` | DTO + controller |
| USB catalog administration | Serial filters, USB devices, and board/chip attachment | Admin UI, see section 6 |
| Seed migration | Insert initial filter rows (WCH vendor-wide, SiLabs vendor-wide, Espressif S2/S3/C3 native-USB PIDs) and device catalog rows (CH340, CH9102, CP2102/04/05, FT232, ESP32-S2/S3/C3 USB-JTAG) | DB + migration |
| `FirmwareLatestResponse` | Replace with `FirmwareRelease`: add `source` (structured), `releaseNotes`, reshape `artifacts` → `boards` with chip/discontinued | DTO + controller |
| `LatestController` | Join Board→Chip, include ReleaseNotes, build new response shape | Controller |
| `FirmwareVersionResponse` | Replace with `FirmwareRelease` (same type as latest) | DTO + controller |
| `VersionsController.GetVersion` | Same joins as latest controller, return `FirmwareRelease` | Controller |
| `FirmwareVersionSummary` | Add `source` and `releaseNotes` fields | DTO |
| `VersionsController.ListVersions` | Add limit/offset/total pagination, include release notes, wrap in `{ versions, total }` | Controller |
| `LatestController` | Add `GET /latest/{channel}/{board}?version=` returning `FirmwareBoardReleaseResponse`, with 204 support | Controller + new DTO |
| `VersionsController` | Add `GET /versions/{version}/{board}` returning `FirmwareBoardReleaseResponse` | Controller + new DTO |
| New `repositories` table | `id` (GUID), `provider`, `owner`, `repo` with unique constraint on `(provider, owner, repo)` | DB + migration |
| New `RepositoryDto` | Repository identity DTO | New DTO |
| New `FirmwareSourceDto` | Repository ref + commitHash, ref, runId + server-constructed URLs | New DTO |
| `firmware_versions` table | Replace `commit_hash`/`release_url` columns with `repository_id` FK, `commit_hash`, `ref`, `run_id` | DB + migration |
| New `GitHubOidcAuthentication` | Validate GitHub OIDC JWTs, extract source claims, match against `repositories` table | New auth handler |
| `FirmwareConfig.CiCd` | Config: `Audience` only (allowed repos managed via admin API, not config) | Config |
| New admin repository endpoints | `PUT`/`GET`/`DELETE /2/firmware/admin/repositories` — manage allowed CI/CD sources | New controller |
| `LatestController` (all boards) | Remove `?board` query param, return 400 for invalid channel (not 404) | Controller |
| `InitReleaseRequest` | Remove `commitHash`/`releaseUrl`, replace `releaseNotes` with `changelog` markdown. Add `?nofail` query param. Source populated from OIDC claims | DTO |
| `CreateFirmwareVersionRequest` | Remove (legacy CI/CD endpoint dropped) | Delete |
| New changelog parser | Parse heading-based markdown into `FirmwareReleaseNote` entries | New utility |
| `ReleasesController.InitRelease` | Call changelog parser, populate source from OIDC token claims | Controller |
| `CiCdController` | Remove (legacy CI/CD endpoint dropped) | Delete |
| Cache headers | Add `Cache-Control` to all public endpoints, `no-store` on errors | Middleware or per-action |
| New `FirmwareChipRefDto` | Small DTO with `id` + `name` | New DTO |
| New `FirmwareBoardDetailDto` | DTO with `chip`, `discontinued`, `artifacts[]` | New DTO |
| `ReleaseStatus.Editing` | New status for releases whose notes need manual review/refinement before publish | Enum value |
| Changelog parser validation | Strict validation with `?nofail` bypass. Invalid changelog → 400 or status = `editing` | New utility logic |
| `PublishRelease` status gate | Reject publish when status is `editing` (409) | Controller |
| Changelog repair | Fix malformed changelogs on staged releases | Admin UI, see section 6 |
| SHA-256 manifest on upload | `sha256` form field required on `UploadBoardArtifacts`, server validates hashes | Controller |
| Staged release cleanup job | Background job aborts expired releases (1h staging, 7d editing) | New hosted service |
| DELETE → 204 | All DELETE endpoints return 204 No Content instead of 200 | Controllers |
| `FirmwareReleaseStatus` → `ReleaseStatus` | Rename enum — staging/published/aborted is a shared workflow concept | Enum rename |
| `repositories` table | Shared infrastructure for source traceability across firmware and desktop modules | DB + migration |
| Desktop `modules` table | Add `repository_id` FK → `repositories` | DB + migration |
| Desktop `versions` table | Add `release_date`, `repository_id` FK, `commit_hash`, `ref`, `run_id` for source traceability | DB + migration |
| V1 `CiCdController` | Populate source traceability fields from GitHub OIDC claims on desktop module publish | Controller |
| Discord notifications | `DiscordConfig` with webhook URLs, `IDiscordNotificationService` for admin channel notifications | Config + service |

---

## 12. Server Notifications

The server optionally sends Discord webhook notifications on certain events. This is a server-side implementation detail, not an API contract — consumers do not interact with this system.

**Configuration** (appsettings):

```jsonc
{
  "Discord": {
    "WebhookUrls": [
      "https://discord.com/api/webhooks/..."
    ]
  }
}
```

If `Discord` is not configured or `WebhookUrls` is empty, all notifications are silently skipped.

**Events**:

| Event | Trigger | Embed color |
|-------|---------|-------------|
| Firmware release published | `PublishRelease` completes successfully | Green |
| Desktop module version published | V1 `CiCdController.PublishVersion` completes | Green |
| Release notes need editing | `InitRelease` with `?nofail` creates release in `editing` status | Yellow |
| Staged release expired | Cleanup job aborts an expired staging release | Red |

**Behavior**:
- Notifications are fire-and-forget — errors are logged but never fail the triggering operation
- Each webhook URL receives the same payload
- Payloads use Discord embed format with color-coded severity and relevant metadata (version, channel, commit hash, release ID)
