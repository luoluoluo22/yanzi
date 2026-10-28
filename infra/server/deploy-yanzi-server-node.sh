#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="${REPO_ROOT:-/opt/yanzi}"
SERVICE_USER="${YANZI_SERVER_USER:-yanzi-server}"
STATE_DIR="${YANZI_SERVER_STATE_DIR:-/var/lib/yanzi-server-node}"
WORKSPACE="${YANZI_SERVER_WORKSPACE:-/srv/yanzi-workspace}"
ENV_FILE="/etc/yanzi-server-node.env"

if [[ $(id -u) -ne 0 ]]; then
  echo "Run as root." >&2
  exit 1
fi

node -e "const [major]=process.versions.node.split('.').map(Number); if(major<22) process.exit(1)" \
  || { echo "Node.js 22+ is required." >&2; exit 1; }

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --home-dir "$STATE_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

install -d -m 0700 -o "$SERVICE_USER" -g "$SERVICE_USER" "$STATE_DIR"
install -d -m 0750 -o "$SERVICE_USER" -g "$SERVICE_USER" "$WORKSPACE"
install -m 0644 "$REPO_ROOT/infra/server/yanzi-server-node.service" /etc/systemd/system/yanzi-server-node.service

if [[ ! -f "$ENV_FILE" ]]; then
  {
    echo "# Add all three cloud values after issuing a scoped device credential."
    echo "# YANZI_CLOUD_BASE_URL=https://sync.example.com"
    echo "# YANZI_ACCOUNT_ID=..."
    echo "# YANZI_DEVICE_TOKEN=..."
    echo "YANZI_SERVER_STATE_DIR=$STATE_DIR"
    echo "YANZI_SERVER_WORKSPACE=$WORKSPACE"
    echo "YANZI_SERVER_LOCAL_PORT=8789"
  } > "$ENV_FILE"
  chmod 0600 "$ENV_FILE"
fi

systemctl daemon-reload
systemctl enable --now yanzi-server-node.service
systemctl --no-pager --full status yanzi-server-node.service
