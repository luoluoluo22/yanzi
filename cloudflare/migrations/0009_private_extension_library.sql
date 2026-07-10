create index if not exists idx_extensions_publisher_updated
  on extensions (publisher_user_id, updated_at desc);

create index if not exists idx_extensions_archive_key
  on extensions (archive_key);
