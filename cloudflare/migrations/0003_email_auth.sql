alter table auth_users add column email text;
alter table auth_users add column email_verified_at text;

create unique index if not exists idx_auth_users_email
  on auth_users (email);

create table if not exists auth_email_verifications (
  email text primary key,
  username text not null,
  code_hash text not null,
  code_salt text not null,
  expires_at text not null,
  created_at text not null,
  updated_at text not null
);
