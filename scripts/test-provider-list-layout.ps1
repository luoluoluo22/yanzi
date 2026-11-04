# WPF regression: measure the *production* provider DataTemplate with narrow widths.
# Run: powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File scripts/test-provider-list-layout.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$sourcePath = Join-Path $PSScriptRoot '..\src\OpenQuickHost\SettingsWindow.xaml'
$source = [IO.File]::ReadAllText((Resolve-Path $sourcePath), [Text.Encoding]::UTF8)
$boxStart = $source.IndexOf('x:Name="ProvidersListBox"', [StringComparison]::Ordinal)
if ($boxStart -lt 0) { throw 'ProvidersListBox missing' }
$blockEnd = $source.IndexOf('</ListBox>', $boxStart, [StringComparison]::Ordinal)
$listSection = $source.Substring($boxStart, $blockEnd - $boxStart)
if ($listSection -notmatch 'ScrollViewer.HorizontalScrollBarVisibility="Disabled"') { throw 'Provider list must not scroll horizontally' }
$start = $source.IndexOf('<DataTemplate>', $boxStart, [StringComparison]::Ordinal)
$end = $source.IndexOf('</DataTemplate>', $start, [StringComparison]::Ordinal) + '</DataTemplate>'.Length
if ($start -lt 0 -or $end -lt 0) { throw 'Provider template missing' }
$xaml = $source.Substring($start, $end-$start)
$xaml = $xaml.Replace('<DataTemplate>', '<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">')
# Remove the code-behind handler; substitute the production switch's 40-DIP footprint.
$xaml = $xaml.Replace('Click="SaveSettingsToggle_Click"', '')
$xaml = $xaml.Replace('Style="{DynamicResource Yanzi.Switch.Shadcn}"', 'Width="40" Height="22"')
$template = [Windows.Markup.XamlReader]::Parse($xaml)
$cases = @(
    @{ Width=280; Name='a1137583371@gmail.com' },
    @{ Width=280; Name=('VeryLong-Provider-Name-' * 9) },
    @{ Width=240; Name=('VeryLong-Provider-Name-' * 9) },
    @{ Width=210; Name='Short name' }
)
foreach ($test in $cases) {
    # 20-DIP list-item margin + 24-DIP template padding from SidebarListBoxItemStyle.
    $available = $test.Width - 44
    $root = [Windows.Controls.Grid]$template.LoadContent()
    $root.DataContext = [PSCustomObject]@{ Name=$test.Name; AvatarChar='A'; IsEnabled=$true }
    $root.Measure([Windows.Size]::new($available, 40))
    $root.Arrange([Windows.Rect]::new(0,0,$available,40))
    $root.UpdateLayout()
    $switch = $root.Children | Where-Object { $_ -is [Windows.Controls.CheckBox] } | Select-Object -First 1
    $text = $root.Children | Where-Object { $_ -is [Windows.Controls.TextBlock] } | Select-Object -First 1
    if ($null -eq $switch -or $null -eq $text) { throw 'Missing switch or name TextBlock' }
    $switchX = ($switch.TransformToAncestor($root).Transform([Windows.Point]::new(0,0))).X
    $textX = ($text.TransformToAncestor($root).Transform([Windows.Point]::new(0,0))).X
    $switchRight = $switchX + $switch.ActualWidth
    $textRight = $textX + $text.ActualWidth
    if ($switch.ActualWidth -lt 39 -or $switchRight -gt ($available + 0.1) -or $switchX -lt 0) {
        throw "Switch overflow: width=$($test.Width), available=$available, switch=[$switchX,$switchRight]"
    }
    if ($text.TextTrimming -ne [Windows.TextTrimming]::CharacterEllipsis -or $textRight -gt ($switchX - 7)) {
        throw "Name overlap: width=$($test.Width), nameRight=$textRight, switchLeft=$switchX"
    }
    if ($text.ToolTip -ne $test.Name) { throw "Full provider name tooltip not bound: width=$($test.Width)" }
    Write-Host ("PASS listWidth={0}, switchRight={1:N1}, nameRight={2:N1}, switchWidth={3:N1}, nameLength={4}" -f $test.Width,$switchRight,$textRight,$switch.ActualWidth,$test.Name.Length)
}
