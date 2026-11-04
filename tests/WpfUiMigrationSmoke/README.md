# WPF migrated-dialog runtime smoke verification

This standalone Windows test runner instantiates **13 real migrated WPF windows** against the current built Yanzi host DLL and validates their XAML resource resolution and a subset of runtime interaction.

Run on a Windows machine with .NET 9 and an interactive desktop, in this order:


~~~powershell
dotnet build OpenQuickHost.sln -c Release --nologo
dotnet restore tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj --configfile tests/WpfUiMigrationSmoke/NuGet.Config
dotnet run --project tests/WpfUiMigrationSmoke/WpfUiMigrationSmoke.csproj -c Release --no-restore
~~~

The verification flag \`App.IsVerificationHarness\` is enabled via reflection before any dialog is shown, to bypass the host's normal background service startup and single-instance behavior. The script uses synthetic values for text, models, WebDAV credentials and notes, and does not save real user credentials or settings.

Coverage: input validation, WebDAV required fields and public styles, AI model already-added protection and selection, Skill export select options, mouse-trigger window, hotkey disabled-confirm state, process picker, synthetic note reading, color picker swatches and HEX/alpha inputs, installed-application picker, and search scope UI resource resolution.

**Important limitations:** the search-scope window uses a synthetic uninitialized \`MainWindow\` dependency solely to test XAML/resource loading; this is not a test of saving actual search settings. Similarly, color eyedropper/dragging, fullscreen window pick, hotkey recording, application scanning completion, real storage writes, light/dark screenshots, and keyboard/accessibility behavior still require targeted independent QA before final acceptance. No application installation or global configuration is performed.

The report is written to \`%TEMP%\yanzi-wpf-ui-smoke-<PID>.log\`. Successful completion prints \`RESULT pass=15 fail=0\` and exits with code 0. Keep this tool limited to trusted Windows devices and synthetic test data.

Additional system prompt editor tests verify cancel/non-persistence and exact multiline confirmation (including tab/newline) using the WPF shared button styles. Full application-level AI settings storage and visual/a11y checks are still pending.
