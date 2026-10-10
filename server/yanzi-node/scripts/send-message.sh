#!/usr/bin/env bash
set -euo pipefail
if [ "$(id -u)" -ne 0 ]; then echo "Run as root or through sudo." >&2; exit 1; fi
set -a
# The secret is read locally from the root-only service EnvironmentFile.
source /etc/yanzi-server-node.env
set +a
exec runuser -u yanzi-server -- /usr/bin/node /opt/yanzi/server/yanzi-node/src/send-message.mjs
