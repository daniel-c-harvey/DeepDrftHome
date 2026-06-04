#!/usr/bin/env bash
# Installed to: /opt/<APP_USER>/bin/deploy-api.sh
# Deployed by: deploy-api Gitea Actions workflow (ssh forced-command)
#
# Expects in ${APP_HOME}/staging/:
#   deepdrft-api.tar.gz          -- published self-contained linux-x64 binary tree
#   deepdrft-migrations-bundle   -- self-contained EF bundle (pre-built in CI)
#   deepdrftapi.service          -- systemd unit file (optional)
#
# Migrations are applied BEFORE the service restarts via the EF bundle binary.
# The bundle covers DeepDrftContext (track metadata DB) only.
# AuthBlocks' Identity DB is NOT bundled here — UseAuthBlocksStartupAsync() applies
# its own migrations and seeds the admin user on first service boot.
#
# The DB connection string is read from the host credential file at:
#   ${APP_HOME}/.config/credentials/connections.json
# The DefaultConnection value is extracted and passed to the bundle.
# No DB name is passed by CI — the host credential is the source of truth.
#
# The vault directory (${APP_HOME}/api/deepdrft/vaults) is NEVER touched on deploy.
# It is persistent host state created by the installer. Do not add cleanup logic here.
#
# Paths are derived at runtime — no hardcoded usernames or home dirs.
# APP_HOME comes from $HOME (sshd sets this for the app user).

set -euo pipefail

export XDG_RUNTIME_DIR="/run/user/$(id -u)"

APP_HOME="${HOME}"
export PATH="${APP_HOME}/.local/bin:${PATH}"

STAGING="${APP_HOME}/staging"
APPROOT="${APP_HOME}/api/deepdrft"
ARCHIVE="deepdrft-api.tar.gz"
BUNDLE="${STAGING}/deepdrft-migrations-bundle"
CREDS_FILE="${APP_HOME}/.config/credentials/connections.json"

echo "[deploy-api] $(date -u +%Y-%m-%dT%H:%M:%SZ) starting"

# ── Read DB connection string from host credential ────────────────────────
if [[ ! -f "${CREDS_FILE}" ]]; then
  echo "[deploy-api] ERROR: credentials file not found: ${CREDS_FILE}" >&2
  echo "[deploy-api] Run setup-step10-creds.sh to create it." >&2
  exit 1
fi

if command -v jq &>/dev/null; then
  DB_CONN="$(jq -r '.ConnectionStrings.DefaultConnection' "${CREDS_FILE}")"
else
  # Fallback: extract with grep/sed if jq is not installed
  DB_CONN="$(grep -o '"DefaultConnection"[[:space:]]*:[[:space:]]*"[^"]*"' "${CREDS_FILE}" \
    | sed 's/.*"DefaultConnection"[[:space:]]*:[[:space:]]*"\([^"]*\)"/\1/')"
fi

if [[ -z "${DB_CONN}" || "${DB_CONN}" == "null" ]]; then
  echo "[deploy-api] ERROR: could not parse DefaultConnection from ${CREDS_FILE}" >&2
  exit 1
fi

echo "[deploy-api] connection string resolved from ${CREDS_FILE}"

# ── Apply migrations (before touching the binary) ─────────────────────────
chmod +x "${BUNDLE}"
echo "[deploy-api] applying DeepDrftContext migrations via EF bundle"
"${BUNDLE}" --connection "${DB_CONN}"
rm -f "${BUNDLE}"
echo "[deploy-api] migrations done"

# ── Swap in new binary tree ────────────────────────────────────────────────
rm -rf "${APPROOT}/bin.prev"
if [[ -d "${APPROOT}/bin" ]]; then
  mv "${APPROOT}/bin" "${APPROOT}/bin.prev"
fi
mkdir -p "${APPROOT}/bin"

tar -xzf "${STAGING}/${ARCHIVE}" -C "${APPROOT}/bin"
rm -f "${STAGING}/${ARCHIVE}"

echo "[deploy-api] archive extracted"

# ── Apply environment files (host-managed, not in archive) ────────────────
# ${APP_HOME}/api/deepdrft/environment/ may contain appsettings overrides.
# These must NOT live inside the rsync-writable staging area so a bad deploy
# cannot overwrite them.
if [[ -d "${APPROOT}/environment" ]]; then
  shopt -s nullglob
  env_files=("${APPROOT}/environment/"*)
  shopt -u nullglob
  if [[ ${#env_files[@]} -gt 0 ]]; then
    mkdir -p "${APPROOT}/bin/environment"
    cp "${env_files[@]}" "${APPROOT}/bin/environment/"
    echo "[deploy-api] environment files applied"
  fi
fi

# ── Install systemd unit file (if present in staging) ─────────────────────
if [[ -f "${STAGING}/deepdrftapi.service" ]]; then
  mkdir -p "${APP_HOME}/.config/systemd/user"
  cp "${STAGING}/deepdrftapi.service" "${APP_HOME}/.config/systemd/user/deepdrftapi.service"
  rm -f "${STAGING}/deepdrftapi.service"
  systemctl --user daemon-reload
  echo "[deploy-api] systemd unit file installed"
fi

# ── Enable and restart service ─────────────────────────────────────────────
systemctl --user enable deepdrftapi.service
systemctl --user restart deepdrftapi.service
systemctl --user is-active --quiet deepdrftapi.service \
  && echo "[deploy-api] service is active" \
  || { echo "[deploy-api] ERROR: service failed to start" >&2; exit 1; }

echo "[deploy-api] done"
