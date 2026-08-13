#!/usr/bin/env bash
#
# Bootstraps the firmware catalog: chips, boards, and the publishing allowlist.
#
# A fresh database has no chips and no boards, and InitRelease rejects any board it does not know,
# so the very first CI ingestion 404s until this has run. Every call is idempotent, so re-running
# after adding a board is safe.
#
# The board list mirrors the PlatformIO environments in the firmware repository — a board's name here
# must match its `[env:...]` name exactly, because that is what a hub compiles in as
# OPENSHOCK_FW_BOARD and sends as its board reference.
#
# Usage:
#   REPO_SERVER_URL=https://repo.openshock.org \
#   ADMIN_TOKEN=... \
#   ./scripts/seed-catalog.sh
#
# Optionally also register the firmware repository as authorized to publish:
#   PUBLISH_OWNER=OpenShock PUBLISH_REPO=firmware ./scripts/seed-catalog.sh
#
# PUBLISH_SCOPES defaults to publish_firmware. Use publish_modules for a desktop module repository,
# or a comma-separated pair to grant both. A repository registered with no scopes cannot publish.

set -euo pipefail

: "${REPO_SERVER_URL:?REPO_SERVER_URL must be set}"
: "${ADMIN_TOKEN:?ADMIN_TOKEN must be set}"

BASE="${REPO_SERVER_URL%/}/v2/firmware/admin"

api() {
  local method=$1 path=$2 body=${3:-}
  local args=(-fsS -X "$method" "$BASE$path" -H "Authorization: $ADMIN_TOKEN")
  if [ -n "$body" ]; then
    args+=(-H "Content-Type: application/json" --data-binary "$body")
  fi
  curl "${args[@]}"
}

# ---- Chips -------------------------------------------------------------------------------------
# Names must match esptool-js chip identifiers exactly; the web flashtool passes them straight
# through. Architecture is xtensa for the ESP32/S2/S3 family, riscv for the C3.

declare -A CHIP_ARCH=(
  ["ESP32"]="xtensa"
  ["ESP32-S2"]="xtensa"
  ["ESP32-S3"]="xtensa"
  ["ESP32-C3"]="riscv"
)

declare -A CHIP_IDS=()

echo "==> Chips"
for chip in "${!CHIP_ARCH[@]}"; do
  payload=$(printf '{"name":"%s","architecture":"%s"}' "$chip" "${CHIP_ARCH[$chip]}")
  # POST returns 201 with the new id; a duplicate name is rejected by the unique index, in which
  # case fall back to looking the existing row up.
  if id=$(api POST /chips "$payload" 2>/dev/null | jq -r '.id'); then
    echo "    created $chip"
  else
    id=$(curl -fsS "${REPO_SERVER_URL%/}/v2/firmware/chips" | jq -r --arg n "$chip" '.[] | select(.name == $n) | .id')
    echo "    exists  $chip"
  fi
  if [ -z "$id" ] || [ "$id" = "null" ]; then
    echo "::error::could not resolve chip id for $chip" >&2
    exit 1
  fi
  CHIP_IDS[$chip]=$id
done

# ---- Boards ------------------------------------------------------------------------------------
# name:chip. Keep in sync with [env:...] blocks in the firmware repo's platformio.ini.
#
# requiredArtifactTypes is app+staticfs: those are the two an OTA update must supply, and publish
# refuses a board that is missing them. 'merged' is additionally produced for USB flashing but is
# not required, since it is not part of the OTA path.

BOARDS=(
  "Wemos-D1-Mini-ESP32:ESP32"
  "Wemos-Lolin-S2-Mini:ESP32-S2"
  "Wemos-Lolin-S3:ESP32-S3"
  "Wemos-Lolin-S3-Mini:ESP32-S3"
  "Waveshare_esp32_s3_zero:ESP32-S3"
  "Pishock-2023:ESP32"
  "Pishock-Lite-2021:ESP32"
  "Seeed-Xiao-ESP32C3:ESP32-C3"
  "Seeed-Xiao-ESP32S3:ESP32-S3"
  "DFRobot-Firebeetle2-ESP32E:ESP32"
  "OpenShock-Core-V1:ESP32-S3"
  "OpenShock-Core-V2:ESP32-S3"
  "NodeMCU-32S:ESP32"
)

echo "==> Boards"
for entry in "${BOARDS[@]}"; do
  board="${entry%%:*}"
  chip="${entry##*:}"
  payload=$(printf '{"name":"%s","chipId":"%s","requiredArtifactTypes":["app","staticfs"]}' \
    "$board" "${CHIP_IDS[$chip]}")
  if api POST /boards "$payload" >/dev/null 2>&1; then
    echo "    created $board ($chip)"
  else
    echo "    exists  $board ($chip)"
  fi
done

# ---- Publish allowlist -------------------------------------------------------------------------
# Registration here is the authorization decision for CI publishing: a GitHub OIDC token proves only
# that some workflow somewhere on GitHub requested it.

if [ -n "${PUBLISH_OWNER:-}" ] && [ -n "${PUBLISH_REPO:-}" ]; then
  echo "==> Publish allowlist"
  scopes_json=$(printf '%s' "${PUBLISH_SCOPES:-publish_firmware}" \
    | tr ',' '\n' | jq -R . | jq -sc 'map(select(length > 0))')
  payload=$(jq -nc --arg o "$PUBLISH_OWNER" --arg r "$PUBLISH_REPO" --argjson s "$scopes_json" \
    '{provider: "github", owner: $o, repo: $r, scopes: $s}')
  api PUT /repositories "$payload" >/dev/null
  echo "    registered $PUBLISH_OWNER/$PUBLISH_REPO with scopes ${PUBLISH_SCOPES:-publish_firmware}"
fi

echo "==> Done"
