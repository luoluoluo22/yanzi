create table if not exists user_sync_revisions (
  user_id text primary key,
  revision integer not null default 0,
  updated_at text not null,
  foreign key (user_id) references users(user_id)
);

create table if not exists user_sync_objects (
  user_id text not null,
  object_id text not null,
  schema_version integer not null default 1,
  object_revision integer not null,
  updated_at text not null,
  updated_by_device_id text,
  updated_by_device_name text,
  deleted integer not null default 0,
  payload_json text not null default '{}',
  primary key (user_id, object_id),
  foreign key (user_id) references users(user_id)
);

create index if not exists idx_user_sync_objects_revision
  on user_sync_objects (user_id, object_revision);
