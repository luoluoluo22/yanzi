create table if not exists auth_password_resets (
  email text primary key,
  user_id text not null,
  code_hash text not null,
  code_salt text not null,
  expires_at text not null,
  created_at text not null,
  updated_at text not null,
  foreign key (user_id) references auth_users(user_id)
);

create index if not exists idx_auth_password_resets_user_id
  on auth_password_resets (user_id);
