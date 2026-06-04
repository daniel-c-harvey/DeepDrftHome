#!/usr/bin/env bash
# deploy/setup-step10-creds.sh
# Run as the app user on the host. Implements Step 6 of install.sh.
# Prompts interactively for every secret, writes JSON credential files
# directly to ${APP_HOME}/.config/credentials/.
#
# Called by install.sh (which exports APP_USER, APP_HOME, PG_ROLE, DB_META,
# DB_AUTH, DOMAIN_PUBLIC, DOMAIN_APP before invoking). Can also be run
# standalone — falls back to defaults so the script remains independently useful.
#
# Idempotency: safe to re-run. By default, credentials that already exist in
# ${CREDDIR} are left alone (no re-prompting). Pass --force as the first arg to
# re-prompt for every secret and overwrite all credential files.
#
# Credential files and their consumers:
#   filedatabase.json  -> DeepDrftAPI  (LoadCredential id: filedatabase)
#   apikey.json        -> DeepDrftAPI  (LoadCredential id: apikey)
#   connections.json   -> DeepDrftAPI  (LoadCredential id: connections)
#   authblocks.json    -> DeepDrftAPI  (LoadCredential id: authblocks)
#   api-public.json    -> DeepDrftPublic  (LoadCredential id: api)
#   api-manager.json   -> DeepDrftManager (LoadCredential id: api)
#
# The LoadCredential ids (left of colon in the unit file) must exactly match
# CredentialTools.ResolvePathOrThrow keys in the application code.

set -euo pipefail

# Escape a string for safe use as inline JSON value.
# Escapes backslash then double-quote (the two characters that break JSON string literals).
# Does not handle embedded newlines or control chars — avoid those in secrets.
json_escape() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

# Accept vars from environment (exported by install.sh) or fall back to defaults.
APP_USER="${APP_USER:-deepdrft}"
export XDG_RUNTIME_DIR="/run/user/$(id -u "${APP_USER}" 2>/dev/null || id -u)"
APP_HOME="${APP_HOME:-/${APP_USER}}"
PG_ROLE="${PG_ROLE:-deepdrft}"
DB_META="${DB_META:-deepdrft-meta}"
DB_AUTH="${DB_AUTH:-deepdrft-auth}"
DOMAIN_PUBLIC="${DOMAIN_PUBLIC:-deepdrft.com}"
DOMAIN_APP="${DOMAIN_APP:-app.${DOMAIN_PUBLIC}}"

CREDDIR="${APP_HOME}/.config/credentials"
EXPECTED_COUNT=6

# ── Parse args ────────────────────────────────────────────────────────────────
FORCE=0
if [[ "${1:-}" == "--force" ]]; then
  FORCE=1
  echo "[setup-step10-creds] --force: will re-prompt and overwrite all credentials"
fi

# ── Credential directory ──────────────────────────────────────────────────────
if [[ ! -d "${CREDDIR}" ]]; then
  mkdir -p "${CREDDIR}"
  chmod 700 "${CREDDIR}"
fi
echo "[setup-step10-creds] ${CREDDIR} ready"

# ── Helpers ───────────────────────────────────────────────────────────────────
# write_cred <name> <content>
# Writes JSON content to ${CREDDIR}/<name>.json (mode 600, owner APP_USER).
write_cred() {
  local name="$1"
  local content="$2"
  local tmp
  tmp="$(mktemp /tmp/deepdrft-cred-XXXXXXXX.json)"
  printf '%s\n' "${content}" > "${tmp}"
  install -m 600 -o "${APP_USER}" -g "${APP_USER}" "${tmp}" "${CREDDIR}/${name}.json"
  rm -f "${tmp}"
  echo "[setup-step10-creds] wrote ${name}.json"
}

# need_cred <name>
# Returns 0 (true) when the named credential should be (re)written:
#   - --force was passed, OR
#   - ${CREDDIR}/<name>.json does not yet exist.
need_cred() {
  local name="$1"
  if [[ "${FORCE}" -eq 1 ]]; then
    return 0
  fi
  if [[ ! -f "${CREDDIR}/${name}.json" ]]; then
    return 0
  fi
  return 1
}

# ── 1. filedatabase.json — no prompts, path fully templated ──────────────────
if need_cred "filedatabase"; then
  write_cred "filedatabase" \
    "{\"FileDatabaseSettings\":{\"VaultPath\":\"${APP_HOME}/api/deepdrft/vaults\"}}"
else
  echo "[setup-step10-creds] filedatabase.json already exists, skipping"
fi

# ── 2. apikey.json — prompt or generate; value reused in api-manager.json ────
API_KEY=""
if need_cred "apikey"; then
  echo
  read -rp "  API key (leave blank to generate with openssl rand -hex 32): " API_KEY_INPUT
  if [[ -z "${API_KEY_INPUT}" ]]; then
    API_KEY="$(openssl rand -hex 32)"
    echo "  [generated] API key: ${API_KEY}"
  else
    API_KEY="${API_KEY_INPUT}"
  fi
  unset API_KEY_INPUT
  write_cred "apikey" \
    "{\"ApiKeySettings\":{\"ApiKey\":\"$(json_escape "${API_KEY}")\"}}"
else
  echo "[setup-step10-creds] apikey.json already exists, skipping"
  # Still need the value for api-manager.json if that's also being written.
  # Read it back from the existing file if jq is available, otherwise prompt again.
  if need_cred "api-manager"; then
    if command -v jq &>/dev/null; then
      API_KEY="$(jq -r '.ApiKeySettings.ApiKey' "${CREDDIR}/apikey.json")"
      echo "[setup-step10-creds] API key read from existing apikey.json"
    else
      echo
      echo "  apikey.json exists but api-manager.json is missing."
      read -rp "  Re-enter the API key (needed for api-manager.json): " API_KEY
    fi
  fi
fi

# ── 3. connections.json — prompt for PG password; DB names from env vars ─────
if need_cred "connections"; then
  echo
  echo "  PostgreSQL connection strings for DeepDrftAPI."
  echo "  DB names: meta='${DB_META}', auth='${DB_AUTH}', role='${PG_ROLE}'"
  echo
  read -rsp "  PostgreSQL password for the '${PG_ROLE}' role: " PG_PASSWORD
  echo
  read -rsp "  Confirm password: " PG_PASSWORD_CONFIRM
  echo
  if [[ "${PG_PASSWORD}" != "${PG_PASSWORD_CONFIRM}" ]]; then
    echo "[setup-step10-creds] ERROR: passwords do not match" >&2
    exit 1
  fi
  if [[ -z "${PG_PASSWORD}" ]]; then
    echo "[setup-step10-creds] ERROR: password cannot be empty" >&2
    exit 1
  fi
  unset PG_PASSWORD_CONFIRM

  META_CONN="Host=localhost;Database=${DB_META};Username=${PG_ROLE};Password=$(json_escape "${PG_PASSWORD}")"
  AUTH_CONN="Host=localhost;Database=${DB_AUTH};Username=${PG_ROLE};Password=$(json_escape "${PG_PASSWORD}")"
  write_cred "connections" \
    "{\"ConnectionStrings\":{\"DefaultConnection\":\"${META_CONN}\",\"Auth\":\"${AUTH_CONN}\"}}"
  unset PG_PASSWORD META_CONN AUTH_CONN
else
  echo "[setup-step10-creds] connections.json already exists, skipping"
fi

# ── 4. authblocks.json — prompt for all AuthBlocks secrets ───────────────────
if need_cred "authblocks"; then
  echo
  echo "  AuthBlocks configuration (JWT, email, admin account)."
  echo

  # JWT secret
  read -rp "  JWT secret (leave blank to generate): " JWT_SECRET_INPUT
  if [[ -z "${JWT_SECRET_INPUT}" ]]; then
    JWT_SECRET="$(openssl rand -hex 32)"
    echo "  [generated] JWT secret"
  else
    JWT_SECRET="${JWT_SECRET_INPUT}"
  fi
  unset JWT_SECRET_INPUT

  # JWT issuer / audience
  read -rp "  JWT issuer (e.g. https://${DOMAIN_PUBLIC}): " JWT_ISSUER
  read -rp "  JWT audience (e.g. https://${DOMAIN_PUBLIC}): " JWT_AUDIENCE

  # Email
  echo
  echo "  Email provider (SMTP/API — used by AuthBlocks for verification emails)."
  read -rp "  Email host (SMTP server or API host): " EMAIL_HOST
  read -rsp "  Email token (API key / SMTP password): " EMAIL_TOKEN
  echo

  # Admin account
  echo
  echo "  Initial admin account for DeepDrft."
  read -rp "  Admin username: " ADMIN_USERNAME
  read -rp "  Admin email: "    ADMIN_EMAIL
  read -rsp "  Admin password: " ADMIN_PASSWORD
  echo
  read -rsp "  Confirm admin password: " ADMIN_PASSWORD_CONFIRM
  echo
  if [[ "${ADMIN_PASSWORD}" != "${ADMIN_PASSWORD_CONFIRM}" ]]; then
    echo "[setup-step10-creds] ERROR: admin passwords do not match" >&2
    exit 1
  fi
  unset ADMIN_PASSWORD_CONFIRM

  # Support email
  read -rp "  Support email address: " SUPPORT_EMAIL

  write_cred "authblocks" "$(cat <<JSON
{"AuthBlocks":{"Jwt":{"Secret":"$(json_escape "${JWT_SECRET}")","Issuer":"$(json_escape "${JWT_ISSUER}")","Audience":"$(json_escape "${JWT_AUDIENCE}")"},"Email":{"Host":"$(json_escape "${EMAIL_HOST}")","Token":"$(json_escape "${EMAIL_TOKEN}")"},"Admin":{"UserName":"$(json_escape "${ADMIN_USERNAME}")","Email":"$(json_escape "${ADMIN_EMAIL}")","Password":"$(json_escape "${ADMIN_PASSWORD}")"},"SupportEmail":"$(json_escape "${SUPPORT_EMAIL}")"}}
JSON
)"
  unset JWT_SECRET JWT_ISSUER JWT_AUDIENCE EMAIL_HOST EMAIL_TOKEN
  unset ADMIN_USERNAME ADMIN_EMAIL ADMIN_PASSWORD SUPPORT_EMAIL
else
  echo "[setup-step10-creds] authblocks.json already exists, skipping"
fi

# ── 5. api-public.json — no prompts, static localhost URL ────────────────────
if need_cred "api-public"; then
  write_cred "api-public" \
    '{"Api":{"ContentApiUrl":"http://localhost:5002"}}'
else
  echo "[setup-step10-creds] api-public.json already exists, skipping"
fi

# ── 6. api-manager.json — reuses API key from step 2 ─────────────────────────
if need_cred "api-manager"; then
  if [[ -z "${API_KEY}" ]]; then
    echo
    echo "  api-manager.json needs the same API key as apikey.json."
    read -rp "  Enter the API key: " API_KEY
  fi
  write_cred "api-manager" \
    "{\"Api\":{\"ContentApiUrl\":\"http://localhost:5002\",\"ContentApiKey\":\"$(json_escape "${API_KEY}")\"}}"
  unset API_KEY
else
  echo "[setup-step10-creds] api-manager.json already exists, skipping"
  unset API_KEY 2>/dev/null || true
fi

# ── Verify ────────────────────────────────────────────────────────────────────
echo ""
echo "[setup-step10-creds] verifying ${CREDDIR}:"
ls -la "${CREDDIR}/"

ACTUAL_COUNT="$(ls "${CREDDIR}/"*.json 2>/dev/null | wc -l)"
if [[ "${ACTUAL_COUNT}" -ne "${EXPECTED_COUNT}" ]]; then
  echo "[setup-step10-creds] ERROR: expected ${EXPECTED_COUNT} .json files, found ${ACTUAL_COUNT}" >&2
  exit 1
fi

echo "[setup-step10-creds] all ${EXPECTED_COUNT} credentials present — step 6 complete"
