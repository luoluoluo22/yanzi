create table if not exists users (
  user_id text primary key,
  created_at text not null,
  updated_at text not null
);

create table if not exists extensions (
  extension_id text primary key,
  display_name text not null,
  latest_version text,
  manifest_json text,
  archive_key text,
  archive_sha256 text,
  updated_at text not null
);

create table if not exists user_extensions (
  user_id text not null,
  extension_id text not null,
  installed_version text not null,
  enabled integer not null default 1,
  settings_json text not null default '{}',
  updated_at text not null,
  primary key (user_id, extension_id),
  foreign key (user_id) references users(user_id),
  foreign key (extension_id) references extensions(extension_id)
);

create index if not exists idx_user_extensions_user_id
  on user_extensions (user_id, updated_at desc);

