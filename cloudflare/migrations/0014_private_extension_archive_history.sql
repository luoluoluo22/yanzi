create table if not exists user_extension_archive_heads (
  user_id text not null,
  extension_id text not null,
  revision integer not null,
  version text not null,
  archive_key text not null,
  archive_sha256 text not null,
  updated_at text not null,
  updated_by_device_id text not null default '',
  updated_by_device_name text not null default '',
  primary key (user_id, extension_id),
  foreign key (user_id) references users(user_id)
);

create table if not exists user_extension_archive_history (
  user_id text not null,
  extension_id text not null,
  revision integer not null,
  version text not null,
  archive_key text not null,
  archive_sha256 text not null,
  updated_at text not null,
  updated_by_device_id text not null default '',
  updated_by_device_name text not null default '',
  operation text not null default 'put',
  restored_from_revision integer,
  primary key (user_id, extension_id, revision),
  foreign key (user_id) references users(user_id)
);

create index if not exists idx_user_extension_archive_history_lookup
  on user_extension_archive_history (user_id, extension_id, revision desc);
