create table if not exists auth_users (
  user_id text primary key,
  username text not null unique,
  password_hash text not null,
  password_salt text not null,
  password_iterations integer not null,
  created_at text not null,
  updated_at text not null,
  foreign key (user_id) references users(user_id)
);

create unique index if not exists idx_auth_users_username
  on auth_users (username);
