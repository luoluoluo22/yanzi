#!/usr/bin/env bash
set -euo pipefail
if [[ "$(id -u)" != "0" ]]; then echo "root required" >&2; exit 1; fi
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
install -d -m 0700 /etc/yanzi-cloud-monitor
install -d -m 0700 /var/lib/yanzi-cloud-monitor
install -d -m 0755 /opt/yanzi-cloud-monitor
install -m 0755 "$repo/scripts/cloudflare/cloudflare_usage_monitor.py" /opt/yanzi-cloud-monitor/monitor.py
if [[ ! -e /etc/yanzi-cloud-monitor/credentials.env ]]; then
  install -m 0600 /dev/null /etc/yanzi-cloud-monitor/credentials.env
fi
chmod 0600 /etc/yanzi-cloud-monitor/credentials.env
cat > /etc/systemd/system/yanzi-cloud-usage-daily.service <<'UNIT'
[Unit]
Description=Yanzi Cloudflare prior complete UTC day usage audit
Wants=network-online.target
After=network-online.target
[Service]
Type=oneshot
User=root
UMask=0077
Environment=PYTHONDONTWRITEBYTECODE=1
ExecStart=/usr/bin/python3 /opt/yanzi-cloud-monitor/monitor.py --mode daily
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/lib/yanzi-cloud-monitor
UNIT
cat > /etc/systemd/system/yanzi-cloud-usage-daily.timer <<'UNIT'
[Unit]
Description=Yanzi Cloudflare daily usage collector after UTC reset
[Timer]
OnCalendar=*-*-* 00:25:00 UTC
RandomizedDelaySec=0
Persistent=true
Unit=yanzi-cloud-usage-daily.service
[Install]
WantedBy=timers.target
UNIT
cat > /etc/systemd/system/yanzi-cloud-usage-hourly.service <<'UNIT'
[Unit]
Description=Yanzi Cloudflare near-real-time usage watcher
Wants=network-online.target
After=network-online.target
[Service]
Type=oneshot
User=root
UMask=0077
Environment=PYTHONDONTWRITEBYTECODE=1
ExecStart=/usr/bin/python3 /opt/yanzi-cloud-monitor/monitor.py --mode hourly
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/var/lib/yanzi-cloud-monitor
UNIT
cat > /etc/systemd/system/yanzi-cloud-usage-hourly.timer <<'UNIT'
[Unit]
Description=Yanzi Cloudflare hourly usage watcher
[Timer]
OnCalendar=*-*-* *:10:00 UTC
RandomizedDelaySec=0
Persistent=true
Unit=yanzi-cloud-usage-hourly.service
[Install]
WantedBy=timers.target
UNIT
systemctl daemon-reload
systemctl enable --now yanzi-cloud-usage-daily.timer yanzi-cloud-usage-hourly.timer
printf 'TIMERS_INSTALLED\n'
systemctl list-timers --no-pager --all 'yanzi-cloud-usage*'
if [[ -s /etc/yanzi-cloud-monitor/credentials.env ]]; then
  echo "CREDENTIAL_STATE=present"
else
  echo "CREDENTIAL_STATE=missing; jobs will fail visibly until a scoped read-only token is configured"
fi
