-- Personal repository credentials are device-local DPAPI secrets. Account cloud keeps metadata only.
update user_extensions
set settings_json = json_set(
  json_set(
    json_set(
      json_set(settings_json, '$.secrets', json('{}')),
      '$.webDavPassword', ''
    ),
    '$.password', ''
  ),
  '$.token', ''
)
where extension_id in ('yanzi-personal-sync-settings', 'yanzi-webdav-settings')
  and json_valid(settings_json);
