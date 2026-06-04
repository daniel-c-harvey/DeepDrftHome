#!/usr/bin/env bash
# deploy/install.sh
#
# One-shot installer for the DeepDrft vertical on Linux.
# Runs from inside the extracted install tarball (or the deploy/ directory).
# Invoked directly or via bootstrap.sh:
#
#   sudo bash install.sh
#
# All file references use ${SCRIPT_DIR}/... — no repo checkout required on the host.
#
# HOME=${APP_HOME} is load-bearing: the systemd %h specifier must resolve to the app
# home dir so that unit WorkingDirectory/ExecStart paths agree with the deploy scripts.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ── Output helpers ─────────────────────────────────────────────────────────────
step()  { echo; echo "=== Step $1: $2 ==="; }
info()  { echo "  [info] $*"; }
ok()    { echo "  [ok]   $*"; }
die()   { echo; echo "  [ERROR] $*" >&2; exit 1; }

# ── Step 0: Preflight ──────────────────────────────────────────────────────────
step 0 "Preflight"

if [[ "${EUID}" -ne 0 ]]; then
  die "Must run as root: sudo bash install.sh"
fi

if ! command -v psql &>/dev/null; then
  die "psql not found on PATH. Install PostgreSQL before running this script."
fi

if ! pg_isready &>/dev/null; then
  die "PostgreSQL is not accepting connections. Start it with: systemctl start postgresql"
fi

if ! command -v nginx &>/dev/null; then
  die "nginx not found on PATH. Install nginx before running this script."
fi

# rrsync ships in the rsync package; path varies by distro version
RRSYNC_BIN=""
if command -v rrsync &>/dev/null; then
  RRSYNC_BIN="$(command -v rrsync)"
elif [[ -x "/usr/lib/rsync/rrsync" ]]; then
  RRSYNC_BIN="/usr/lib/rsync/rrsync"
else
  die "rrsync not found. Install the rsync package: apt-get install -y rsync"
fi

ok "psql present, PostgreSQL active, nginx present"
ok "rrsync found at: ${RRSYNC_BIN}"

# ── Step 0b: Parameter collection ─────────────────────────────────────────────
step "0b" "Configuration parameters"

echo
echo "  Press Enter to accept the default shown in [brackets]."
echo

read -rp "  App system username [deepdrft]: " APP_USER
APP_USER="${APP_USER:-deepdrft}"

read -rp "  App home directory [/${APP_USER}]: " APP_HOME
APP_HOME="${APP_HOME:-/${APP_USER}}"

read -rp "  PostgreSQL role name [${APP_USER}]: " PG_ROLE
PG_ROLE="${PG_ROLE:-${APP_USER}}"

read -rp "  Metadata database name [deepdrft-meta]: " DB_META
DB_META="${DB_META:-deepdrft-meta}"

read -rp "  Auth database name [deepdrft-auth]: " DB_AUTH
DB_AUTH="${DB_AUTH:-deepdrft-auth}"

read -rp "  Public domain [deepdrft.com]: " DOMAIN_PUBLIC
DOMAIN_PUBLIC="${DOMAIN_PUBLIC:-deepdrft.com}"

read -rp "  App subdomain [app.${DOMAIN_PUBLIC}]: " DOMAIN_APP
DOMAIN_APP="${DOMAIN_APP:-app.${DOMAIN_PUBLIC}}"

CERTBOT_EMAIL=""
while [[ -z "${CERTBOT_EMAIL}" ]]; do
  read -rp "  Email for certbot TLS cert (required): " CERTBOT_EMAIL
done

# Derived paths
OPT_DIR="/opt/${APP_USER}/bin"
AK_FILE="${APP_HOME}/.ssh/authorized_keys"

echo
echo "  ┌──────────────────────────────────────────────────────────────┐"
echo "  │                   Installation settings                      │"
echo "  ├──────────────────────────────────────────────────────────────┤"
printf "  │  %-22s  %-37s│\n" "APP_USER"      "${APP_USER}"
printf "  │  %-22s  %-37s│\n" "APP_HOME"      "${APP_HOME}"
printf "  │  %-22s  %-37s│\n" "PG_ROLE"       "${PG_ROLE}"
printf "  │  %-22s  %-37s│\n" "DB_META"       "${DB_META}"
printf "  │  %-22s  %-37s│\n" "DB_AUTH"       "${DB_AUTH}"
printf "  │  %-22s  %-37s│\n" "DOMAIN_PUBLIC" "${DOMAIN_PUBLIC}"
printf "  │  %-22s  %-37s│\n" "DOMAIN_APP"    "${DOMAIN_APP}"
printf "  │  %-22s  %-37s│\n" "CERTBOT_EMAIL" "${CERTBOT_EMAIL}"
printf "  │  %-22s  %-37s│\n" "OPT_DIR"       "${OPT_DIR}"
echo "  └──────────────────────────────────────────────────────────────┘"
echo

read -rp "  Proceed with these settings? [y/N] " CONFIRM
if [[ "${CONFIRM}" != "y" && "${CONFIRM}" != "Y" ]]; then
  die "Aborted by user."
fi

# ── Step 1: Create app system user ─────────────────────────────────────────────
step 1 "Create '${APP_USER}' system user"

if id "${APP_USER}" &>/dev/null; then
  ok "user '${APP_USER}' already exists — skipping"
  if [[ ! -d "${APP_HOME}" ]]; then
    info "Creating ${APP_HOME} (home dir missing)"
    mkdir -p "${APP_HOME}"
  fi
  chown "${APP_USER}:${APP_USER}" "${APP_HOME}"
  ok "${APP_HOME} owned by ${APP_USER}:${APP_USER}"
else
  echo
  echo "  About to create system user '${APP_USER}' with home directory ${APP_HOME}"
  echo "  and shell /bin/bash (required for SSH forced-command dispatch)."
  echo
  read -rp "  Proceed? [y/N] " CONFIRM_USER
  if [[ "${CONFIRM_USER}" != "y" && "${CONFIRM_USER}" != "Y" ]]; then
    die "Aborted by user."
  fi

  useradd \
    --system \
    --home-dir "${APP_HOME}" \
    --create-home \
    --shell /bin/bash \
    "${APP_USER}"

  ok "User created:"
  id "${APP_USER}"
fi

# ── Step 2: Enable linger ──────────────────────────────────────────────────────
step 2 "Enable linger for '${APP_USER}'"

if loginctl show-user "${APP_USER}" 2>/dev/null | grep -q "Linger=yes"; then
  ok "linger already enabled"
else
  loginctl enable-linger "${APP_USER}"
  ok "linger enabled"
fi

# ── Step 3: Directory layout ───────────────────────────────────────────────────
step 3 "Directory layout"

# Run as app user so files are created with correct ownership
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/public/bin"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/public/environment"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/manager/bin"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/manager/environment"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/api/deepdrft/bin"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/api/deepdrft/environment"
# FileDatabase vault: persistent data, created once, never touched on deploy
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/api/deepdrft/vaults"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/staging"
sudo -u "${APP_USER}" mkdir -p "${APP_HOME}/.ssh"
sudo -u "${APP_USER}" chmod 700 "${APP_HOME}/.ssh"

ok "Directory layout ready"

# ── Step 4: Deploy scripts ─────────────────────────────────────────────────────
step 4 "Deploy scripts -> ${OPT_DIR}/"

mkdir -p "${OPT_DIR}"

# Always overwrite — idempotent for re-runs that pick up script changes.
# ssh-wrapper is installed without .sh extension (authorized_keys points to it).
install -m 750 -o "${APP_USER}" -g "${APP_USER}" \
  "${SCRIPT_DIR}/ssh-wrapper.sh"    "${OPT_DIR}/ssh-wrapper"
install -m 750 -o "${APP_USER}" -g "${APP_USER}" \
  "${SCRIPT_DIR}/deploy-public.sh"  "${OPT_DIR}/deploy-public.sh"
install -m 750 -o "${APP_USER}" -g "${APP_USER}" \
  "${SCRIPT_DIR}/deploy-manager.sh" "${OPT_DIR}/deploy-manager.sh"
install -m 750 -o "${APP_USER}" -g "${APP_USER}" \
  "${SCRIPT_DIR}/deploy-api.sh"     "${OPT_DIR}/deploy-api.sh"

ok "Scripts installed:"
ls -la "${OPT_DIR}/"

# ── Step 5: Systemd user units ─────────────────────────────────────────────────
step 5 "Systemd user units"

APP_UID="$(id -u "${APP_USER}")"
APP_RUNTIME_DIR="/run/user/${APP_UID}"

mkdir -p "${APP_HOME}/.config/systemd/user"

# Always overwrite — idempotent for re-runs that pick up unit changes.
cp "${SCRIPT_DIR}/systemd/deepdrftpublic.service"  "${APP_HOME}/.config/systemd/user/"
cp "${SCRIPT_DIR}/systemd/deepdrftmanager.service" "${APP_HOME}/.config/systemd/user/"
cp "${SCRIPT_DIR}/systemd/deepdrftapi.service"     "${APP_HOME}/.config/systemd/user/"

chown -R "${APP_USER}:${APP_USER}" "${APP_HOME}/.config/systemd"

# daemon-reload and enable. XDG_RUNTIME_DIR must be set explicitly — PAM may not
# have set it for this non-interactive context, and it varies by distro.
sudo -u "${APP_USER}" env "XDG_RUNTIME_DIR=${APP_RUNTIME_DIR}" systemctl --user daemon-reload

sudo -u "${APP_USER}" env "XDG_RUNTIME_DIR=${APP_RUNTIME_DIR}" systemctl --user enable \
  deepdrftpublic.service \
  deepdrftmanager.service \
  deepdrftapi.service

ok "Units installed and enabled (not started — binaries don't exist yet)"

# ── Step 6: Credentials ────────────────────────────────────────────────────────
step 6 "Credentials"

install -d -m 700 -o "${APP_USER}" -g "${APP_USER}" "${APP_HOME}/.config/credentials"

CRED_COUNT="$(find "${APP_HOME}/.config/credentials/" -maxdepth 1 -name '*.json' 2>/dev/null | wc -l)"

if [[ "${CRED_COUNT}" -ge 6 ]]; then
  ok "All credential files present (${CRED_COUNT} found) — skipping"
  info "To refresh credentials, run: bash ${SCRIPT_DIR}/setup-step10-creds.sh"
else
  info "${CRED_COUNT}/6 credential files present — running setup-step10-creds.sh"
  sudo -u "${APP_USER}" \
    env APP_USER="${APP_USER}" \
        APP_HOME="${APP_HOME}" \
        PG_ROLE="${PG_ROLE}" \
        DB_META="${DB_META}" \
        DB_AUTH="${DB_AUTH}" \
        DOMAIN_PUBLIC="${DOMAIN_PUBLIC}" \
        DOMAIN_APP="${DOMAIN_APP}" \
    bash "${SCRIPT_DIR}/setup-step10-creds.sh"
fi

# ── Step 7: PostgreSQL role and databases ──────────────────────────────────────
step 7 "PostgreSQL role and databases"

echo
echo "  The '${PG_ROLE}' PostgreSQL role needs a password for TCP connections."
echo "  Local peer-auth connections (used by the deploy scripts) do not use it,"
echo "  but the credential JSON files reference it for app TCP connections."
echo

read -rsp "  PostgreSQL password for the '${PG_ROLE}' role: " PG_PASSWORD
echo
read -rsp "  Confirm password: " PG_PASSWORD_CONFIRM
echo

if [[ "${PG_PASSWORD}" != "${PG_PASSWORD_CONFIRM}" ]]; then
  die "Passwords do not match."
fi

if [[ -z "${PG_PASSWORD}" ]]; then
  die "Password cannot be empty."
fi

unset PG_PASSWORD_CONFIRM

# Create role if it doesn't exist, then always set the password.
# Password is embedded in the heredoc body (psql stdin) — not in argv,
# so it does not appear in `ps aux`. PGPASSWORD env var is not used here
# because postgres peer auth (the default for the postgres superuser) does
# not require a connection password.
info "Creating/updating '${PG_ROLE}' PostgreSQL role..."

sudo -u postgres psql <<SQL
DO \$\$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '${PG_ROLE}') THEN
    CREATE ROLE "${PG_ROLE}" WITH LOGIN PASSWORD '${PG_PASSWORD}';
    RAISE NOTICE 'Role ${PG_ROLE} created.';
  ELSE
    RAISE NOTICE 'Role ${PG_ROLE} already exists — updating password.';
  END IF;
END
\$\$;
ALTER ROLE "${PG_ROLE}" WITH PASSWORD '${PG_PASSWORD}';
SQL

unset PG_PASSWORD

info "Creating databases if not present..."

# Databases may have hyphens in their names — always double-quote in SQL.
DB_META_EXISTS=$(sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname = '${DB_META}'")
if echo "$DB_META_EXISTS" | grep -q 1; then
  info "Database '${DB_META}' already exists — skipping"
else
  sudo -u postgres psql -c "CREATE DATABASE \"${DB_META}\" OWNER \"${PG_ROLE}\";"
  ok "Database '${DB_META}' created"
fi

DB_AUTH_EXISTS=$(sudo -u postgres psql -tc "SELECT 1 FROM pg_database WHERE datname = '${DB_AUTH}'")
if echo "$DB_AUTH_EXISTS" | grep -q 1; then
  info "Database '${DB_AUTH}' already exists — skipping"
else
  sudo -u postgres psql -c "CREATE DATABASE \"${DB_AUTH}\" OWNER \"${PG_ROLE}\";"
  ok "Database '${DB_AUTH}' created"
fi

ok "Databases ready"

info "Verifying peer auth connections..."

if ! sudo -u "${APP_USER}" psql -U "${PG_ROLE}" -d "${DB_META}" -c '\conninfo'; then
  die "Peer auth failed for ${DB_META}. Check /etc/postgresql/*/main/pg_hba.conf has: local all all peer"
fi
ok "Peer auth verified for ${DB_META}"

if ! sudo -u "${APP_USER}" psql -U "${PG_ROLE}" -d "${DB_AUTH}" -c '\conninfo'; then
  die "Peer auth failed for ${DB_AUTH}. Check /etc/postgresql/*/main/pg_hba.conf has: local all all peer"
fi
ok "Peer auth verified for ${DB_AUTH}"

# ── Step 8: SSH authorized_keys ────────────────────────────────────────────────
step 8 "SSH authorized_keys"

echo
echo "  Generate a CI deploy key on your LOCAL machine (not this host):"
echo
echo "    ssh-keygen -t ed25519 -C \"gitea-ci-${APP_USER}-dch7\" -f ~/.ssh/gitea_${APP_USER}_dch7"
echo
echo "  Then paste the PUBLIC key (.pub file contents) at the prompt below."
echo "  After setup completes, add the PRIVATE key to:"
echo "    Gitea -> repo -> Settings -> Secrets -> DEEPDRFT_DCH7_SSH_DEPLOY"
echo "    (or DEEPDRFT_PROD_SSH_DEPLOY for the production host)"
echo

read -rp "  Paste public key here: " PUBKEY

# Strip carriage returns and leading/trailing whitespace (Windows clipboard, paste artifacts)
PUBKEY="$(printf '%s' "${PUBKEY}" | tr -d '\r' | xargs)"

if [[ -z "${PUBKEY}" ]]; then
  die "No public key provided."
fi

# Validate it looks like an SSH public key
if [[ "${PUBKEY}" != ssh-ed25519* ]]; then
  die "Key does not start with 'ssh-ed25519'. Only ed25519 keys are accepted for CI deploy."
fi

if [[ -f "${AK_FILE}" ]] && grep -qF "${PUBKEY}" "${AK_FILE}"; then
  ok "Key already present in authorized_keys — skipping"
else
  # Write the forced-command + restrict prefix before the key.
  # The 'restrict' keyword bundles no-port-forwarding, no-agent-forwarding,
  # no-X11-forwarding, no-pty, no-user-rc into one short token.
  AK_ENTRY="command=\"${OPT_DIR}/ssh-wrapper\",restrict ${PUBKEY}"
  echo "${AK_ENTRY}" >> "${AK_FILE}"
  ok "Key written to ${AK_FILE}"
fi

chown "${APP_USER}:${APP_USER}" "${AK_FILE}"
chmod 600 "${AK_FILE}"

unset PUBKEY

# ── Step 9: nginx ──────────────────────────────────────────────────────────────
step 9 "nginx"

# Read templates, substitute domain placeholders, write to sites-available.
# Templates use __DOMAIN_PUBLIC__ and __DOMAIN_APP__ so the files in the tarball
# don't contain real hostnames — substitution happens at install time.
sed -e "s|__DOMAIN_PUBLIC__|${DOMAIN_PUBLIC}|g" \
  "${SCRIPT_DIR}/nginx/deepdrft-public.conf" \
  > "/etc/nginx/sites-available/${DOMAIN_PUBLIC}.conf"

sed -e "s|__DOMAIN_APP__|${DOMAIN_APP}|g" \
  "${SCRIPT_DIR}/nginx/deepdrft-manager.conf" \
  > "/etc/nginx/sites-available/${DOMAIN_APP}.conf"

ln -sf "/etc/nginx/sites-available/${DOMAIN_PUBLIC}.conf" /etc/nginx/sites-enabled/
ln -sf "/etc/nginx/sites-available/${DOMAIN_APP}.conf"    /etc/nginx/sites-enabled/

rm -f /etc/nginx/sites-enabled/default

if ! nginx -t; then
  die "nginx config test failed — check ${SCRIPT_DIR}/nginx/*.conf"
fi

systemctl reload nginx

ok "nginx configured and reloaded"

# ── Step 10: Summary ───────────────────────────────────────────────────────────
step 10 "Summary"

cat <<SUMMARY

========================================================================
  DeepDrft vertical installation complete.
========================================================================

Installed / configured:
  - System user '${APP_USER}' with HOME=${APP_HOME}
  - Linger enabled (user units survive without an active session)
  - Directory layout: ${APP_HOME}/{public,manager,api/deepdrft}/{bin,environment}
  - FileDatabase vault: ${APP_HOME}/api/deepdrft/vaults (persistent — never touched on deploy)
  - Deploy scripts in ${OPT_DIR}/ (mode 750, owner ${APP_USER})
  - Systemd user units installed and enabled (not started):
      deepdrftpublic.service
      deepdrftmanager.service
      deepdrftapi.service
  - Credentials in ${APP_HOME}/.config/credentials/ (6 x mode 600)
  - PostgreSQL role '${PG_ROLE}' and databases: ${DB_META}, ${DB_AUTH}
  - SSH authorized_keys with forced-command + restrict
  - nginx sites-available + sites-enabled, default site removed

------------------------------------------------------------------------
Remaining manual steps:
------------------------------------------------------------------------

1. Add the PRIVATE deploy key to Gitea:
   Gitea -> repo -> Settings -> Secrets -> DEEPDRFT_DCH7_SSH_DEPLOY
   (Value: full contents of ~/.ssh/gitea_${APP_USER}_dch7 on your local machine,
    including -----BEGIN and -----END lines)

2. Smoke-test the SSH forced-command chain from your LOCAL machine:

   Layer 1 — connectivity and dispatch:
     ssh -i ~/.ssh/gitea_${APP_USER}_dch7 ${APP_USER}@<host> deploy-public
     # Expect: "[deploy-public] ... starting" then error about missing archive
     # (staging is empty at this stage). The exit non-zero is expected.

   Layer 2 — catch-all blocks arbitrary commands:
     ssh -i ~/.ssh/gitea_${APP_USER}_dch7 ${APP_USER}@<host> id
     # Expect: "ssh-wrapper: unknown command: id" — you must NOT get a shell.

   Layer 3 — rsync jail:
     echo test > /tmp/smoke-test.txt
     rsync -e "ssh -i ~/.ssh/gitea_${APP_USER}_dch7 -o StrictHostKeyChecking=yes" \\
       /tmp/smoke-test.txt ${APP_USER}@<host>:
     # Then verify on host: ls ${APP_HOME}/staging/smoke-test.txt

   Layer 4 — all three trigger words:
     for cmd in deploy-public deploy-manager deploy-api; do
       echo "--- \$cmd ---"
       ssh -i ~/.ssh/gitea_${APP_USER}_dch7 ${APP_USER}@<host> "\$cmd" 2>&1 | head -5
     done

3. TLS via certbot (run AFTER DNS is pointing at this host):

   snap install --classic certbot
   ln -sf /snap/bin/certbot /usr/bin/certbot

   certbot --nginx \\
     --email ${CERTBOT_EMAIL} \\
     --agree-tos \\
     --no-eff-email \\
     -d ${DOMAIN_PUBLIC} \\
     -d ${DOMAIN_APP}

   nginx -t && systemctl reload nginx
   certbot renew --dry-run
   systemctl status snap.certbot.renew.timer

   Note: use the certbot SNAP (not apt) — the snap installs the correct
   snap.certbot.renew.timer systemd unit. The apt package leaves auto-renewal
   silently broken on Ubuntu.

4. First deploy — push to Gitea master to trigger CI.
   Services will start on first successful deploy.

------------------------------------------------------------------------
SUMMARY
