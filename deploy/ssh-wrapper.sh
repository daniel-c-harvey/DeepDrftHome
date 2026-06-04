#!/usr/bin/env bash
# Installed to: /opt/<APP_USER>/bin/ssh-wrapper
#
# Forced-command wrapper for the CI deploy key.
# Install in ~<APP_USER>/.ssh/authorized_keys as:
#
#   command="/opt/<APP_USER>/bin/ssh-wrapper",restrict ssh-ed25519 AAAA... gitea-ci-deploy
#
# The 'restrict' keyword covers no-port-forwarding, no-agent-forwarding,
# no-X11-forwarding, no-pty, no-user-rc in one token.
#
# Supported commands dispatched by SSH_ORIGINAL_COMMAND:
#   rsync --server ...    -> rrsync jail (staging uploads)
#   deploy-public         -> <OPT_DIR>/deploy-public.sh
#   deploy-manager        -> <OPT_DIR>/deploy-manager.sh
#   deploy-api            -> <OPT_DIR>/deploy-api.sh  (no trailing arg — reads creds from host)
#
# Paths are derived at runtime — no hardcoded usernames or home dirs.
# APP_HOME comes from $HOME (sshd sets this for the app user).
# OPT_DIR is the directory containing this script.

set -euo pipefail

# Derive paths from runtime context — no hardcoded APP_USER or APP_HOME.
# sshd sets $HOME to the app user's home directory for forced-command sessions.
APP_HOME="${HOME}"
OPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

CMD="${SSH_ORIGINAL_COMMAND:-}"

case "$CMD" in
  "rsync --server"*)
    exec rrsync "${APP_HOME}/staging"
    ;;
  deploy-public)
    exec "${OPT_DIR}/deploy-public.sh"
    ;;
  deploy-manager)
    exec "${OPT_DIR}/deploy-manager.sh"
    ;;
  deploy-api)
    exec "${OPT_DIR}/deploy-api.sh"
    ;;
  *)
    echo "ssh-wrapper: unknown command: ${CMD}" >&2
    exit 1
    ;;
esac
