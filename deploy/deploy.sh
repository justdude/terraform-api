#!/usr/bin/env bash
# ============================================================================
# deploy.sh — Build, push, and deploy terraform-api via SSH
#
# Usage:
#   ./deploy/deploy.sh                          # uses defaults from .env
#   ./deploy/deploy.sh --host 10.0.0.5 --user deploy
#   ./deploy/deploy.sh --help
#
# Prerequisites on the remote host:
#   - Docker Engine 20.10+ and docker compose v2
#   - SSH key-based auth configured (or ssh-agent running)
#   - Target user added to the 'docker' group (no sudo needed)
#
# What this script does:
#   1. Builds the Docker image locally
#   2. Saves it to a tar archive
#   3. Copies the archive + compose file to the remote host via SCP
#   4. Loads the image and starts/restarts the service via SSH
# ============================================================================
set -euo pipefail

# ---- Defaults (override via .env or CLI flags) ----
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

IMAGE_NAME="${IMAGE_NAME:-terraform-api}"
IMAGE_TAG="${IMAGE_TAG:-latest}"
REMOTE_HOST="${REMOTE_HOST:-}"
REMOTE_USER="${REMOTE_USER:-deploy}"
REMOTE_PORT="${REMOTE_PORT:-22}"
REMOTE_DIR="${REMOTE_DIR:-/opt/terraform-api}"
SSH_KEY="${SSH_KEY:-}"
COMPOSE_FILE="${COMPOSE_FILE:-docker-compose.yml}"

# Load .env if present (project root or deploy dir)
for env_file in "$PROJECT_ROOT/.env" "$SCRIPT_DIR/.env"; do
    if [[ -f "$env_file" ]]; then
        # shellcheck disable=SC1090
        source "$env_file"
        break
    fi
done

# ---- CLI argument parsing ----
usage() {
    cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Options:
  --host HOST        Remote host IP or hostname  (required)
  --user USER        SSH username                 (default: deploy)
  --port PORT        SSH port                     (default: 22)
  --key  PATH        SSH private key path         (default: ssh-agent)
  --dir  PATH        Remote install directory     (default: /opt/terraform-api)
  --tag  TAG         Docker image tag             (default: latest)
  --build-only       Build the image but do not deploy
  --help             Show this help message

Environment variables (or .env file):
  REMOTE_HOST, REMOTE_USER, REMOTE_PORT, SSH_KEY,
  REMOTE_DIR, IMAGE_NAME, IMAGE_TAG
EOF
    exit 0
}

BUILD_ONLY=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --host)       REMOTE_HOST="$2"; shift 2 ;;
        --user)       REMOTE_USER="$2"; shift 2 ;;
        --port)       REMOTE_PORT="$2"; shift 2 ;;
        --key)        SSH_KEY="$2";     shift 2 ;;
        --dir)        REMOTE_DIR="$2";  shift 2 ;;
        --tag)        IMAGE_TAG="$2";   shift 2 ;;
        --build-only) BUILD_ONLY=true;  shift   ;;
        --help|-h)    usage ;;
        *)            echo "Unknown option: $1"; usage ;;
    esac
done

FULL_IMAGE="${IMAGE_NAME}:${IMAGE_TAG}"
ARCHIVE="/tmp/${IMAGE_NAME}-${IMAGE_TAG}.tar.gz"

# SSH options
SSH_OPTS="-o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -p ${REMOTE_PORT}"
SCP_OPTS="-o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -P ${REMOTE_PORT}"
if [[ -n "$SSH_KEY" ]]; then
    SSH_OPTS+=" -i $SSH_KEY"
    SCP_OPTS+=" -i $SSH_KEY"
fi

log() { echo -e "\033[1;34m[deploy]\033[0m $*"; }
err() { echo -e "\033[1;31m[error]\033[0m $*" >&2; }

# ---- Step 1: Build ----
log "Building Docker image: ${FULL_IMAGE}"
docker build -t "${FULL_IMAGE}" "${PROJECT_ROOT}"

if $BUILD_ONLY; then
    log "Build complete (--build-only). Image: ${FULL_IMAGE}"
    exit 0
fi

# ---- Validate remote host ----
if [[ -z "$REMOTE_HOST" ]]; then
    err "REMOTE_HOST is required. Pass --host or set REMOTE_HOST in .env"
    exit 1
fi

# ---- Step 2: Save image to compressed archive ----
log "Saving image to ${ARCHIVE}"
docker save "${FULL_IMAGE}" | gzip > "${ARCHIVE}"
ARCHIVE_SIZE=$(du -h "${ARCHIVE}" | cut -f1)
log "Archive size: ${ARCHIVE_SIZE}"

# ---- Step 3: Copy files to remote host ----
log "Copying to ${REMOTE_USER}@${REMOTE_HOST}:${REMOTE_DIR}"

# shellcheck disable=SC2086
ssh ${SSH_OPTS} "${REMOTE_USER}@${REMOTE_HOST}" "mkdir -p ${REMOTE_DIR}"

# shellcheck disable=SC2086
scp ${SCP_OPTS} \
    "${ARCHIVE}" \
    "${PROJECT_ROOT}/${COMPOSE_FILE}" \
    "${REMOTE_USER}@${REMOTE_HOST}:${REMOTE_DIR}/"

# Copy .env if it exists (contains runtime config like APP_PORT)
if [[ -f "${PROJECT_ROOT}/.env" ]]; then
    # shellcheck disable=SC2086
    scp ${SCP_OPTS} "${PROJECT_ROOT}/.env" "${REMOTE_USER}@${REMOTE_HOST}:${REMOTE_DIR}/"
fi

# ---- Step 4: Load image and start service on remote host ----
log "Deploying on remote host..."

# shellcheck disable=SC2086,SC2029
ssh ${SSH_OPTS} "${REMOTE_USER}@${REMOTE_HOST}" bash -s <<REMOTE_SCRIPT
set -euo pipefail
cd "${REMOTE_DIR}"

echo "[remote] Loading Docker image..."
docker load < "${IMAGE_NAME}-${IMAGE_TAG}.tar.gz"

echo "[remote] Stopping existing service (if any)..."
docker compose down --remove-orphans 2>/dev/null || true

echo "[remote] Starting service..."
docker compose up -d

echo "[remote] Waiting for health check..."
for i in {1..30}; do
    if docker compose ps --format json 2>/dev/null | grep -q '"healthy"'; then
        echo "[remote] Service is healthy!"
        break
    fi
    if [[ \$i -eq 30 ]]; then
        echo "[remote] WARNING: Health check not passing after 30 seconds"
        docker compose logs --tail 30
        exit 1
    fi
    sleep 1
done

echo "[remote] Cleaning up archive..."
rm -f "${IMAGE_NAME}-${IMAGE_TAG}.tar.gz"

echo "[remote] Deployment complete."
docker compose ps
REMOTE_SCRIPT

# ---- Cleanup local archive ----
rm -f "${ARCHIVE}"

log "Deployment to ${REMOTE_HOST} successful!"
log "Service available at: http://${REMOTE_HOST}:${APP_PORT:-8080}"
