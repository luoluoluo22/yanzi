create table if not exists wishes (
  id text primary key,
  user_id text not null,
  username text not null,
  title text not null,
  description text not null,
  category text not null default '通用',
  status text not null default 'open',
  accepted_reply_id text,
  reward_points integer not null default 50,
  reply_count integer not null default 0,
  created_at text not null,
  updated_at text not null
);

create table if not exists wish_replies (
  id text primary key,
  wish_id text not null,
  user_id text not null,
  username text not null,
  content text not null,
  code_snippet text,
  is_accepted integer not null default 0,
  created_at text not null
);

create table if not exists user_points (
  user_id text primary key,
  username text not null,
  points integer not null default 0,
  wishes_count integer not null default 0,
  accepted_count integer not null default 0,
  updated_at text not null
);

create table if not exists point_transactions (
  id text primary key,
  user_id text not null,
  amount integer not null,
  action_type text not null,
  description text not null,
  reference_id text,
  created_at text not null
);

create index if not exists idx_wishes_status_created
  on wishes (status, created_at desc);

create index if not exists idx_wish_replies_wish_id
  on wish_replies (wish_id, created_at asc);

create index if not exists idx_user_points_rank
  on user_points (points desc, accepted_count desc);
