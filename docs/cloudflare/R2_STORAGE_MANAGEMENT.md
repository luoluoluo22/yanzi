# R2 storage management (2026-10-13)

## Verified inventory

The two buckets contain 4,910 objects and roughly 7.62 GB of payload bytes.
The main bucket has 4,906 objects and 7.61 GB, while the img bucket has four
objects and 3.46 MB. Private account extensions hold about 6.43 GB:
yanzi-notes archive history contains 1,111 ZIP versions totalling roughly
5.42 GB, yanzi-capture has 57 ZIPs (about 375 MB), and yanzi-album has
19 ZIPs (about 248 MB). Android release downloads occupy about 1.13 GB.

An ETag and length match makes about 472 MB of same-account/same-extension
objects POSSIBLE byte duplicates; it does not establish that any objects are
unreferenced or safe to remove.

The inventory script is at scripts/cloudflare/r2_storage_inventory.py.
The exact-key manifest stays privately on the Ubuntu server at
/var/lib/yanzi-cloud-monitor/r2/inventory-YYYY-MM-DD.json (mode 0600).
It must not be committed, published, or printed in plaintext.

## Archive safety

Cloudflare stores revision history in user_extension_archive_history and
the active revision in user_extension_archive_heads. Every historical revision
may be restored by its R2 archive_key, so no automatic expiration or
age-based deletion is permitted. The current monitoring token cannot inspect
production D1 archive references; NEVER assume an old object is unreferenced.

Uploads now attempt same-user, same-extension ZIP reuse by SHA256 and size,
with R2 HEAD confirmation. The new revision is still written to history;
a failed compare-and-swap must never delete an old shared object.
Cross-account object reuse is prohibited.

## Hourly monitoring

The read-only source scripts/cloudflare/r2_storage_guard.py is installed on
Ubuntu as /opt/yanzi-cloud-monitor/r2_storage_guard.py and called by the
existing hourly systemd collection service through a drop-in. It reads
Cloudflare R2 storage GraphQL analytics, not private contents.
Output: /var/lib/yanzi-cloud-monitor/r2-live.json (mode 0600).
Failure: /var/lib/yanzi-cloud-monitor/r2-last-error.json, never fake 0.
A storage sample older than two hours is rejected.
Alert conditions: total live storage at least 9,000,000,000 bytes;
growth since previous UTC-day final snapshot at least 200,000,000 bytes.
Per-day dedup state: r2-alert-state.json. The 9 GB condition is an
operational threshold, NOT a 90-percent-used monthly GB-month bill.

## No-deletion policy until reference validation

To clean old R2 objects, first obtain a scoped D1 read-only reference
snapshot for every archive head/history and extension pointer. Prove each
candidate is unreferenced, preserve a recoverable copy with checksum, dry run,
obtain approval for any historical retention policy change, then remove
only a reviewed whitelist in batches and verify rollback. No automatic
lifecycle delete rules are installed.

Tests:
python3 -m unittest scripts/cloudflare/test_r2_storage_guard.py
node --test cloudflare/src/private-archive-reuse.test.mjs
