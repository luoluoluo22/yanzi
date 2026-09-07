-- VIP and license key management
alter table users add column vip_expire_at text;
alter table users add column vip_type text;

create table if not exists license_keys (
  code text primary key,
  type text not null, -- 'month', 'year', 'lifetime'
  duration_days integer not null default 0,
  batch_tag text,
  status text not null default 'unused', -- 'unused', 'used', 'revoked'
  used_by_user_id text,
  used_at text,
  created_at text not null,
  foreign key (used_by_user_id) references users(user_id)
);

create index if not exists idx_license_keys_status on license_keys(status);
create index if not exists idx_license_keys_used_by on license_keys(used_by_user_id);
create index if not exists idx_license_keys_batch_tag on license_keys(batch_tag);
