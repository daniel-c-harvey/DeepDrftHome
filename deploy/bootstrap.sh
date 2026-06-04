#!/usr/bin/env bash
# deploy/bootstrap.sh
#
# Self-contained first-time host bootstrap for the DeepDrft vertical.
# Curl this onto a bare Ubuntu host and pipe to bash as root:
#
# For private repos (typical): download the release asset from the Gitea UI
# on your local machine, scp it to the host, then run with INSTALL_PKG_PATH:
#
#   scp deepdrft-install.tar.gz root@<host>:/tmp/
#   curl -fsSL .../deploy/bootstrap.sh | INSTALL_PKG_PATH=/tmp/deepdrft-install.tar.gz sudo bash
#
# For public repos: set INSTALL_PKG_URL to the release asset URL instead.
#
#   curl -fsSL https://gitea.example.com/danielharvey/DeepDrftHome/raw/branch/master/deploy/bootstrap.sh \
#     | INSTALL_PKG_URL=https://gitea.example.com/danielharvey/DeepDrftHome/releases/download/<tag>/deepdrft-install.tar.gz \
#       sudo bash

set -euo pipefail

# ── Output helpers ──────────────────────────────────────────────────────────────
step() { echo; echo "=== $* ==="; }
die()  { echo; echo "[ERROR] $*" >&2; exit 1; }

# ── Preflight ──────────────────────────────────────────────────────────────────
step "DeepDrft bootstrap"

if [[ "${EUID}" -ne 0 ]]; then
  die "Must run as root: sudo bash bootstrap.sh"
fi

# ── OS prereqs ─────────────────────────────────────────────────────────────────
step "Installing OS prereqs"

export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get install -y --no-install-recommends \
  postgresql \
  nginx \
  rsync \
  openssl \
  jq \
  wget \
  ca-certificates

echo "  [ok] apt packages installed"

# Ensure postgres is running — may need a moment after fresh install
systemctl enable postgresql
systemctl start postgresql
sleep 2
if ! pg_isready &>/dev/null; then
  die "PostgreSQL did not start. Check: journalctl -u postgresql"
fi
echo "  [ok] PostgreSQL running"

# ── Install package resolution ─────────────────────────────────────────────────
step "Install package"

TARBALL="/tmp/deepdrft-install.tar.gz"
EXTRACT_DIR="/tmp/deepdrft-install"

if [[ -n "${INSTALL_PKG_PATH:-}" ]]; then
  # Case 1: caller supplied an explicit local path
  if [[ ! -f "${INSTALL_PKG_PATH}" ]]; then
    die "INSTALL_PKG_PATH is set but file not found: ${INSTALL_PKG_PATH}"
  fi
  echo "  Using local package: ${INSTALL_PKG_PATH}"
  TARBALL="${INSTALL_PKG_PATH}"
elif [[ -n "${INSTALL_PKG_URL:-}" ]]; then
  # Case 2: URL supplied — download it
  echo "  Downloading ${INSTALL_PKG_URL} ..."
  wget -q --show-progress -O "${TARBALL}" "${INSTALL_PKG_URL}"
  echo "  [ok] Downloaded to ${TARBALL}"
else
  # Nothing provided — prompt, explaining the private-repo workflow
  echo
  echo "  The install package is a Gitea release asset (deepdrft-install.tar.gz)."
  echo "  Because the repo is private, download it from the Gitea UI on your local"
  echo "  machine and scp it to this host before continuing."
  echo
  echo "  Option A — local path (recommended for private repos):"
  echo "    scp deepdrft-install.tar.gz root@<this-host>:/tmp/"
  echo "    Then re-run: INSTALL_PKG_PATH=/tmp/deepdrft-install.tar.gz bash bootstrap.sh"
  echo
  echo "  Option B — URL (only works if the release is publicly accessible):"
  echo "    Enter the URL below."
  echo
  read -rp "  Local path to tarball (leave blank to enter a URL): " INSTALL_PKG_PATH_INPUT
  if [[ -n "${INSTALL_PKG_PATH_INPUT}" ]]; then
    if [[ ! -f "${INSTALL_PKG_PATH_INPUT}" ]]; then
      die "File not found: ${INSTALL_PKG_PATH_INPUT}"
    fi
    echo "  Using local package: ${INSTALL_PKG_PATH_INPUT}"
    TARBALL="${INSTALL_PKG_PATH_INPUT}"
  else
    echo
    read -rp "  INSTALL_PKG_URL: " INSTALL_PKG_URL
    if [[ -z "${INSTALL_PKG_URL}" ]]; then
      die "INSTALL_PKG_URL is required."
    fi
    echo "  Downloading ${INSTALL_PKG_URL} ..."
    wget -q --show-progress -O "${TARBALL}" "${INSTALL_PKG_URL}"
    echo "  [ok] Downloaded to ${TARBALL}"
  fi
fi

rm -rf "${EXTRACT_DIR}"
mkdir -p "${EXTRACT_DIR}"
tar -xzf "${TARBALL}" -C "${EXTRACT_DIR}"
echo "  [ok] Extracted to ${EXTRACT_DIR}"

# ── Hand off to install.sh ─────────────────────────────────────────────────────
step "Running install.sh"

if [[ ! -f "${EXTRACT_DIR}/install.sh" ]]; then
  die "install.sh not found in ${EXTRACT_DIR}. Tarball layout unexpected."
fi

chmod +x "${EXTRACT_DIR}/install.sh"
exec bash "${EXTRACT_DIR}/install.sh"
