#!/usr/bin/env python3
"""Install a scoped Yanzi device credential received over an authenticated SSH session.

The account-owner token must NEVER be sent to this host. This accepts only
the device-specific credential that may send to one preselected Android device.
"""
import base64
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import time

CONFIG = pathlib.Path('/etc/yanzi-server-node.env')
IDENTITY = pathlib.Path('/var/lib/yanzi-server-node/device-id')
TARGET = 'android-f85961cb-a306-46aa-8125-33f9d8614a7e'
BASE_URL = 'https://sync.luoluoluo.cc.cd'

def main():
    raw = sys.stdin.buffer.read(16001)
    if len(raw) > 16000:
        raise ValueError('payload_too_large')
    grant = json.loads(raw)
    if not isinstance(grant, dict):
        raise ValueError('invalid_payload')
    device_id = str(grant.get('deviceId', ''))
    account_id = str(grant.get('accountId', ''))
    target = str(grant.get('targetDeviceId', ''))
    token = str(grant.get('accessToken', ''))
    expiration = int(grant.get('expiresAt', 0))
    if device_id != IDENTITY.read_text().strip() or target != TARGET:
        raise ValueError('device_or_target_mismatch')
    if grant.get('baseUrl') != BASE_URL or not account_id or len(account_id) > 150:
        raise ValueError('invalid_account_or_host')
    if not re.fullmatch(r'[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+', token):
        raise ValueError('invalid_credential_format')
    try:
        claims = json.loads(base64.urlsafe_b64decode(token.split('.')[1] + '==='))
    except (ValueError, TypeError, UnicodeDecodeError) as exc:
        raise ValueError('invalid_credential_claims') from exc
    if claims.get('type') != 'device' or claims.get('deviceId') != device_id or str(claims.get('sub')) != account_id or int(claims.get('exp', 0)) != expiration:
        raise ValueError('not_scoped_device_credential')
    now = int(time.time())
    if expiration < now + 60 or expiration > now + 30 * 86400 + 300:
        raise ValueError('invalid_credential_expiration')
    if '--validate-only' in sys.argv:
        print(json.dumps({'valid': True, 'expiresAt': expiration, 'target': target}))
        return
    if os.geteuid() != 0:
        raise PermissionError('root_required')
    updates = {
        'YANZI_CLOUD_BASE_URL': BASE_URL,
        'YANZI_ACCOUNT_ID': account_id,
        'YANZI_DEVICE_TOKEN': token,
        'YANZI_MESSAGE_TARGET_DEVICE_ID': target,
        'YANZI_DEVICE_CREDENTIAL_EXPIRES_AT': str(expiration),
    }
    settings = {}
    for line in CONFIG.read_text().splitlines():
        if '=' in line and not line.lstrip().startswith('#'):
            key, value = line.split('=', 1)
            if key.isidentifier():
                settings[key] = value
    settings.update(updates)
    fd, temp = tempfile.mkstemp(prefix='.yanzi-node-', dir=CONFIG.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            for key, value in settings.items():
                if '\n' in value or '\r' in value:
                    raise ValueError('invalid_environment_value')
                stream.write(f'{key}={value}\n')
        os.replace(temp, CONFIG)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    subprocess.run(['systemctl', 'restart', 'yanzi-server-node.service'], check=True)
    print(json.dumps({'installed': True, 'expiresAt': expiration, 'target': target}))

if __name__ == '__main__':
    try:
        main()
    except Exception as exc:
        print(json.dumps({'installed': False, 'error': type(exc).__name__ + ':' + str(exc)}), file=sys.stderr)
        sys.exit(1)
