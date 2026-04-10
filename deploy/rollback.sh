#!/usr/bin/env bash
# ============================================================================
# rollback.sh — Stop and remove the terraform-api service on a remote host
#
# Usage:
#   ./deploy/rollback.sh --host 10.0.0.5
#   ./deploy/rollback.sh                   # reads REMOTE_HOST from .env
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

REMOTE_HOST="${REMOTE_HOST:-}"
REMOTE_USER="${REMOTE_USER:-deploy}"
REMOTE_PORT="${REMOTE_PORT:-22}"
REMOTE_DIR="${REMOTE_DIR:-/opt/terraform-api}"
SSH_KEY="${SSH_KEY:-}"

for env_file in "$PROJECT_ROOT/.env" "$SCRIPT_DIR/.env"; do
    if [[ -f "$env_file" ]]; then
        # shellcheck disable=SC1090
        source "$env_file"
        break
    fi
done

while [[ $# -gt 0 ]]; do
    case "$1" in
        --host) REMOTE_HOST="$2"; shift 2 ;;
        --user) REMOTE_USER="$2"; shift 2 ;;
        --port) REMOTE_PORT="$2"; shift 2 ;;
        --key)  SSH_KEY="$2";     shift 2 ;;
        --dir)  REMOTE_DIR="$2";  shift 2 ;;
        *)      echo "Unknown option: $1"; exit 1 ;;
    esac
done

if [[ -z "$REMOTE_HOST" ]]; then
    echo "REMOTE_HOST is required. Pass --host or set in .env"
    exit 1
fi

SSH_OPTS="-o StrictHostKeyChecking=accept-new -o ConnectTimeout=10 -p ${REMOTE_PORT}"
[[ -n "$SSH_KEY" ]] && SSH_OPTS+=" -i $SSH_KEY"

echo "[rollback] Stopping service on ${REMOTE_HOST}..."

# shellcheck disable=SC2086,SC2029
ssh ${SSH_OPTS} "${REMOTE_USER}@${REMOTE_HOST}" bash -s <<REMOTE_SCRIPT
set -euo pipefail
cd "${REMOTE_DIR}" 2>/dev/null || { echo "Directory ${REMOTE_DIR} not found"; exit 1; }

echo "[remote] Stopping containers..."
docker compose down --remove-orphans

echo "[remote] Removing dangling images..."
docker image prune -f

echo "[remote] Current state:"
docker ps -a --filter "label=com.docker.compose.project" --format "table {{.Names}}\t{{.Status}}"
REMOTE_SCRIPT

echo "[rollback] Service stopped on ${REMOTE_HOST}."
