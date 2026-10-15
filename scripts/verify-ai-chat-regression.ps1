$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$chat = [IO.File]::ReadAllText((Join-Path $root 'src\OpenQuickHost\MainWindow.AiChat.cs'))
$hooks = [IO.File]::ReadAllText((Join-Path $root 'src\OpenQuickHost\MainWindow.Extensions.cs'))
$xaml = [IO.File]::ReadAllText((Join-Path $root 'src\OpenQuickHost\MainWindow.xaml'))

# The fix from ea4fc48/d8c7310 must not vanish when branches or releases are merged.
if (-not $hooks.Contains('if (_isPinned || IsAiChatMode || !IsVisible || IsRadialPickerMode)')) {
    throw 'Clipboard popup must not dismiss the AI chat workspace.'
}
if ($chat.Contains('private bool _isAiChatRequestInFlight;') -or
    $chat.Contains('private bool _isAiChatSubmissionPending;')) {
    throw 'Global AI chat request gate has been reintroduced.'
}
foreach ($item in @(
    '_aiRequestTracker.TryPrepare(topic.Id)',
    '_aiRequestTracker.Start(topic.Id, cancellation)',
    'BuildAiRequestMessages(mode, topic)',
    'AiChatSseParser.ReadAsync(stream, onDelta, cancellationToken)',
    'topic.IsGenerating = true',
    'topic.IsGenerating = false')) {
    if (-not $chat.Contains($item)) { throw "AI chat missing topic-scoped requirement: $item" }
}
if (-not $xaml.Contains('Visibility="{Binding AiChatCancelVisibility}"') -or
    -not $xaml.Contains('Visibility="{Binding StreamingTextVisibility}"') -or
    -not $xaml.Contains('Binding IsGenerating')) {
    throw 'AI chat streaming and per-conversation controls have regressed.'
}
Write-Output 'AI_CHAT_CLIENT_REGRESSION_PASSED'
