# AI chat diagnostics log window: read path optimization

## Hotspot
The diagnostics window refreshed every two seconds. Previously it ran `File.ReadAllLines` synchronously on the WPF dispatcher, even when no log entries changed. The log file is bounded by rotation, but repeated filesystem reads and parsing could still stall the UI, particularly when the file was near its 2 MB rotation threshold.

## Changes
- `AiChatLogSnapshotCache` reuses the parsed lines and filtered text for identical file stamp, local write revision, conversation and display mode. Metadata, append revisions, log rotation, file deletion and manual refresh invalidate cached data. The existing last-250-lines behavior and content redaction remain unchanged.
- The window probes the file and reads it on a background worker. The UI refresh loop coalesces duplicate requests, ignores stale results when the conversation or display mode changes, and never updates a closed window. Window close stops its timer.
- Existing diagnostic writes still use the original serialized writer gate. This scope does not change the on-disk log format or copy-to-clipboard behavior.

## Verification
Run `scripts/verify-ai-chat-log-performance.ps1`. The new isolated regression uses temporary log files to verify unchanged refreshes, topic and all-topic filters, rotation, deletion, manual-invalidation revisions, same-size/same-timestamp writes, 250-line retention and overlapping writes. Build the production Windows host, run the existing desktop/AI chat regressions, and perform hidden native WPF host acceptance. A/B performance figures are deliberately not claimed.

Release and installed-app verification are separate from PR merge.
