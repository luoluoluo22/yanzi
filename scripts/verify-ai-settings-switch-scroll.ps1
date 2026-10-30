$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$path = Join-Path $root 'src\OpenQuickHost\SettingsWindow.xaml'
[xml]$doc = Get-Content -LiteralPath $path -Raw -Encoding UTF8
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
$providers = $doc.SelectSingleNode("//w:ListBox[@x:Name='ProvidersListBox']", $ns)
if (!$providers) { throw 'AI provider list missing' }
$toggle = $providers.SelectSingleNode('.//w:DataTemplate//w:CheckBox', $ns)
if (!$toggle) { throw 'Provider row switch missing' }
if ($toggle.GetAttribute('Style') -ne '{DynamicResource Yanzi.Switch.Shadcn}') { throw 'Provider row switch does not share the detail switch style' }
if ($toggle.GetAttribute('IsChecked') -notmatch 'Binding IsEnabled, Mode=TwoWay') { throw 'Provider row switch is not two-way bound' }
if ($toggle.GetAttribute('Click') -ne 'SaveSettingsToggle_Click') { throw 'Provider row switch does not persist changes' }
if ($providers.SelectSingleNode(".//w:TextBlock[@Text='ON']", $ns)) { throw 'Legacy ON badge remains' }
$section = $doc.SelectSingleNode("//w:Grid[@x:Name='AiSectionRoot']", $ns)
if (!$section) { throw 'AI section missing' }
$detail = $section.SelectSingleNode("./w:ScrollViewer[@Grid.Column='2']", $ns)
if (!$detail) { throw 'AI detail scroll viewer missing' }
if ($detail.GetAttribute('CanContentScroll') -ne 'False') { throw 'AI detail is not pixel-scrolled' }
if (!$detail.SelectSingleNode(".//w:CheckBox[@Click='SaveSettingsToggle_Click']", $ns)) { throw 'Detail switch binding missing' }
Write-Host 'PASS: provider switch matches detail switch and persists two-way changes'
Write-Host 'PASS: AI details use pixel scrolling rather than whole-card logical scrolling'
