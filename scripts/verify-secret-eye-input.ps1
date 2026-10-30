param([int]$ProcessId = 0)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$path = Join-Path $root 'src\OpenQuickHost\SettingsWindow.xaml'
[xml]$doc = Get-Content -LiteralPath $path -Raw -Encoding UTF8
$ns = [System.Xml.XmlNamespaceManager]::new($doc.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$ns.AddNamespace('local','clr-namespace:OpenQuickHost')
$env = $doc.SelectSingleNode("//w:StackPanel[@x:Name='EnvironmentSectionRoot']//w:ItemsControl[@ItemsSource='{Binding EnvironmentVariables}']//local:SecretValueEditor", $ns)
$ai = $doc.SelectSingleNode("//w:Grid[@x:Name='AiSectionRoot']//local:SecretValueEditor", $ns)
if (!$env -or !$ai) { throw 'Secret editor missing from environment or AI settings' }
if ($env.GetAttribute('Value') -notmatch 'Binding Value, Mode=TwoWay') { throw 'Environment value binding not two-way' }
if ($ai.GetAttribute('Value') -notmatch 'Binding ApiKey, Mode=TwoWay') { throw 'AI provider API key binding not two-way' }
foreach ($node in @($env,$ai)) {
    if ($node.GetAttribute('ValueEdited') -ne 'SecretValueEditor_ValueEdited') { throw 'Secret edits do not trigger persistence' }
}
[xml]$widget = Get-Content (Join-Path $root 'src\OpenQuickHost\SecretValueEditor.xaml') -Raw -Encoding UTF8
$masked = $widget.SelectSingleNode('//w:PasswordBox', $ns)
$plain = $widget.SelectSingleNode('//w:TextBox', $ns)
$eye = $widget.SelectSingleNode('//w:Button[@x:Name="EyeButton"]', $ns)
if (!$masked -or !$plain -or !$eye) { throw 'Native password, optional plaintext or eye button missing' }
if ($plain.GetAttribute('Visibility') -ne 'Collapsed') { throw 'Plaintext must not render by default' }
if ($eye.GetAttribute('HorizontalAlignment') -ne 'Right' -or $eye.GetAttribute('Margin') -ne '0,0,4,0') { throw 'Eye must be inside the right edge of the input' }
$cs = Get-Content (Join-Path $root 'src\OpenQuickHost\SecretValueEditor.xaml.cs') -Raw -Encoding UTF8
if ($cs -notmatch 'BindsTwoWayByDefault' -or $cs -notmatch 'ValueEdited\?\.Invoke' -or $cs -notmatch 'DataContextChanged') {
    throw 'Secret binding, edit notification, or provider-switch auto-hide missing'
}
Write-Output 'PASS: shared inline eye control, native masked default, two-way bindings and edit notifications'
if (!$ProcessId) { return }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$a = [System.Windows.Automation.AutomationElement]
$t = [System.Windows.Automation.ControlType]
$window = $a::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new($a::NameProperty, '燕子设置 · 统一 UI（开发版）'))
if (!$window -or $window.Current.ProcessId -ne $ProcessId) { throw 'Isolated preview not available' }
$proc = Get-CimInstance Win32_Process -Filter "ProcessId=$ProcessId"
if (!$proc -or $proc.CommandLine -notmatch '--dev' -or $proc.CommandLine -notmatch '--settings-preview') {
    throw 'Refusing to inspect non-dev application'
}
$nav = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new($a::ControlTypeProperty, $t::ListItem))
foreach ($page in @('environment','ai')) {
    $item = @($nav | Where-Object { $_.Current.Name -match "Key = $page," }) | Select-Object -First 1
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 180
    $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new($a::ControlTypeProperty, $t::Button))
    $eyeButton = @($buttons | Where-Object { $_.Current.Name -eq '显示内容' -and !$_.Current.IsOffscreen }) | Select-Object -First 1
    if (!$eyeButton) { throw "Missing visible eye for $page" }
    $edits = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new($a::ControlTypeProperty, $t::Edit))
    if (!@($edits | Where-Object { !$_.Current.IsOffscreen -and $_.Current.IsPassword }).Count) {
        throw "Unmasked default in $page"
    }
    $eyeButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 110
    if ($eyeButton.Current.Name -ne '隐藏内容') { throw "Reveal click failed on $page" }
    $eyeButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 110
    if ($eyeButton.Current.Name -ne '显示内容') { throw "Hide click failed on $page" }
    Write-Output "PASS: $page eye click, reveal and hide"
}
