# Targeted running-state lookup

The main launcher, quick-panel slots and settings view model previously called RunningExtensionRegistry.GetSnapshot().Any(...) for each IsRunning binding. Each call rechecked every registered process/managed extension, reflected external PID fields, allocated records and sorted the entire snapshot.

RunningExtensionRegistry.IsRunning now examines only matching local extension entries, cleaning up expired matches as before. A connected shell continues using its already-received RuntimeConnection.Running snapshot. The three UI bindings use this direct query. Full GetSnapshot remains available unchanged for list/API consumers.

Verification: `scripts/verify-running-state-lookup.ps1` compiles the Windows host and requires explicit targeted regression output before host startup (which may exit on an already-running user desktop). Regression asserts active, missing and expired extension statuses, cleanup, and no liveness callbacks for unrelated extensions. This is a local code-path optimization, not a measured whole-app speedup. No release or formal installer is triggered.
