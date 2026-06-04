#!/usr/bin/env bash
# Installed to: /opt/<APP_USER>/bin/deploy-public.sh
# Deployed by: deploy-public Gitea Actions workflow (ssh forced-command)
#
# Expects in ${APP_HOME}/staging/:
#   deepdrft-public.tar.gz   -- published self-contained linux-x64 binary tree
#
# DeepDrftPublic receives its API URL credential via systemd LoadCredential
# (api-public.json -> $CREDENTIALS_DIRECTORY/api at runtime). No env file copy needed.
#
# Paths are derived at runtime — no hardcoded usernames or home dirs.
# APP_HOME comes from $HOME (sshd sets this for the app user).

set -euo pipefail

export XDG_RUNTIME_DIR="/run/user/$(id -u)"

APP_HOME="${HOME}"
export PATH="${APP_HOME}/.local/bin:${PATH}"

STAGING="${APP_HOME}/staging"
APPROOT="${APP_HOME}/public"
ARCHIVE="deepdrft-public.tar.gz"

echo "[deploy-public] $(date -u +%Y-%m-%dT%H:%M:%SZ) starting"

# ── Swap in new binary tree ────────────────────────────────────────────────
rm -rf "${APPROOT}/bin.prev"
if [[ -d "${APPROOT}/bin" ]]; then
  mv "${APPROOT}/bin" "${APPROOT}/bin.prev"
fi
mkdir -p "${APPROOT}/bin"

tar -xzf "${STAGING}/${ARCHIVE}" -C "${APPROOT}/bin"
rm -f "${STAGING}/${ARCHIVE}"

echo "[deploy-public] archive extracted"

# ── Enable and restart service ─────────────────────────────────────────────
systemctl --user enable deepdrftpublic.service
systemctl --user restart deepdrftpublic.service
systemctl --user is-active --quiet deepdrftpublic.service \
  && echo "[deploy-public] service is active" \
  || { echo "[deploy-public] ERROR: service failed to start" >&2; exit 1; }

echo "[deploy-public] done"
