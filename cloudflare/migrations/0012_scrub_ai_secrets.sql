-- AI API keys are device-local DPAPI secrets from desktop sync protocol v2 onward.
-- Scrub both current objects and retained history so old plaintext does not remain queryable.
update user_sync_objects
set payload_json = json_set(
  json_set(payload_json, '$.aiApiKey', ''),
  '$.aiServiceProviders',
  json(coalesce((
    select json_group_array(json_remove(provider.value, '$.apiKey'))
    from json_each(json_extract(user_sync_objects.payload_json, '$.aiServiceProviders')) provider
  ), '[]'))
)
where object_id = 'settings.ai'
  and json_valid(payload_json);

update user_sync_object_history
set payload_json = json_set(
  json_set(payload_json, '$.aiApiKey', ''),
  '$.aiServiceProviders',
  json(coalesce((
    select json_group_array(json_remove(provider.value, '$.apiKey'))
    from json_each(json_extract(user_sync_object_history.payload_json, '$.aiServiceProviders')) provider
  ), '[]'))
)
where object_id = 'settings.ai'
  and json_valid(payload_json);

update user_extensions
set settings_json = json_set(
  json_set(settings_json, '$.aiApiKey', ''),
  '$.aiServiceProviders',
  json(coalesce((
    select json_group_array(json_remove(provider.value, '$.apiKey'))
    from json_each(json_extract(user_extensions.settings_json, '$.aiServiceProviders')) provider
  ), '[]'))
)
where extension_id in ('yanzi-quickpanel-settings', 'yanzi-ai-settings')
  and json_valid(settings_json);
