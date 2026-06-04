#!/usr/bin/env bash
# Installed to: /opt/<APP_USER>/bin/deploy-manager.sh
# Deployed by: deploy-manager Gitea Actions workflow (ssh forced-command)
#
# Expects in ${APP_HOME}/staging/:
#   deepdrft-manager.tar.gz   -- published self-contained linux-x64 binary tree
#
# DeepDrftManager receives its API URL + API key credential via systemd LoadCredential
# (api-manager.json -> $CREDENTIALS_DIRECTORY/api at runtime). No env file copy needed.
#
# Paths are derived at runtime — no hardcoded usernames or home dirs.
# APP_HOME comes from $HOME (sshd sets this for the app user).

set -euo pipefail

export XDG_RUNTIME_DIR="/run/user/$(id -u)"

APP_HOME="${HOME}"
export PATH="${APP_HOME}/.local/bin:${PATH}"

STAGING="${APP_HOME}/staging"
APPROOT="${APP_HOME}/manager"
ARCHIVE="deepdrft-manager.tar.gz"

echo "[deploy-manager] $(date -u +%Y-%m-%dT%H:%M:%SZ) starting"

# ── Swap in new binary tree ────────────────────────────────────────────────
rm -rf "${APPROOT}/bin.prev"
if [[ -d "${APPROOT}/bin" ]]; then
  mv "${APPROOT}/bin" "${APPROOT}/bin.prev"
fi
mkdir -p "${APPROOT}/bin"

tar -xzf "${STAGING}/${ARCHIVE}" -C "${APPROOT}/bin"
rm -f "${STAGING}/${ARCHIVE}"

echo "[deploy-manager] archive extracted"

# ── Enable and restart service ─────────────────────────────────────────────
systemctl --user enable deepdrftmanager.service
systemctl --user restart deepdrftmanager.service
systemctl --user is-active --quiet deepdrftmanager.service \
  && echo "[deploy-manager] service is active" \
  || { echo "[deploy-manager] ERROR: service failed to start" >&2; exit 1; }

echo "[deploy-manager] done"
