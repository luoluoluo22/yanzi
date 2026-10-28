create table if not exists user_sync_object_history (
  user_id text not null,
  object_id text not null,
  revision integer not null,
  schema_version integer not null default 1,
  updated_at text not null,
  updated_by_device_id text,
  updated_by_device_name text,
  deleted integer not null default 0,
  payload_json text not null default '{}',
  operation text not null default 'update',
  restored_from_revision integer,
  primary key (user_id, revision),
  foreign key (user_id) references users(user_id)
);

create index if not exists idx_user_sync_object_history_object
  on user_sync_object_history (user_id, object_id, revision desc);
