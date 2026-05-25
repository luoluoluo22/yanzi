create table if not exists user_devices (
  device_id text primary key,
  user_id text not null,
  platform text not null,
  display_name text not null,
  push_token text,
  capabilities_json text not null default '{}',
  last_seen_at text not null,
  created_at text not null,
  updated_at text not null,
  foreign key (user_id) references users(user_id)
);

create index if not exists idx_user_devices_user_platform
  on user_devices (user_id, platform, updated_at desc);

create table if not exists device_messages (
  message_id text primary key,
  user_id text not null,
  source_device_id text,
  target_device_id text,
  target_platform text,
  kind text not null,
  title text,
  body_text text,
  payload_json text not null default '{}',
  status text not null default 'pending',
  created_at text not null,
  delivered_at text,
  acked_at text,
  expires_at text,
  foreign key (user_id) references users(user_id)
);

create index if not exists idx_device_messages_user_target
  on device_messages (user_id, status, target_device_id, target_platform, created_at desc);

create index if not exists idx_device_messages_source
  on device_messages (user_id, source_device_id, created_at desc);
