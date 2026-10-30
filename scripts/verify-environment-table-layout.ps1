$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$file = Join-Path $root 'src\OpenQuickHost\SettingsWindow.xaml'
[xml]$doc = Get-Content -LiteralPath $file -Raw -Encoding UTF8
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace('w','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$ns.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
$ns.AddNamespace('local','clr-namespace:OpenQuickHost')
$section = $doc.SelectSingleNode("//w:StackPanel[@x:Name='EnvironmentSectionRoot']", $ns)
if (!$section) { throw 'Environment settings section missing' }
$rows = $section.SelectSingleNode(".//w:ItemsControl[@ItemsSource='{Binding EnvironmentVariables}']", $ns)
if (!$rows) { throw 'Variable list missing' }
$grid = $rows.SelectSingleNode('./w:ItemsControl.ItemTemplate/w:DataTemplate/w:Border/w:Grid', $ns)
if (!$grid) { throw 'Compact variable table row missing' }
$header = $rows.ParentNode.SelectSingleNode("./w:Border/w:Grid", $ns)
if (!$header) { throw 'Shared table header missing' }
$colSizes = @('1.2*','14','1.8*','14','1.5*','76')
foreach ($g in @($header,$grid)) {
  $cols = @($g.SelectNodes('./w:Grid.ColumnDefinitions/w:ColumnDefinition', $ns))
  if ($cols.Count -ne 6) { throw 'Column count mismatch' }
  for ($i=0;$i -lt 6;$i++) { if ($cols[$i].GetAttribute('Width') -ne $colSizes[$i]) { throw "Column width mismatch $i" } }
}
foreach ($name in @('名称','值','说明','操作')) {
  if (@($header.SelectNodes("./w:TextBlock[@Text='$name']", $ns)).Count -ne 1) { throw "Missing header $name" }
}
if (@($rows.SelectNodes(".//w:TextBlock[@Text='名称' or @Text='值' or @Text='说明']", $ns)).Count -ne 0) { throw 'Repeated labels remain inside rows' }
foreach ($field in @('Name','Description')) {
  if (!$grid.SelectSingleNode("./w:TextBox[contains(@Text,'Binding $field, Mode=TwoWay')]", $ns)) { throw "Missing editable binding: $field" }
}
# Secret values use a dedicated masked editor rather than an ordinary TextBox.
$secret = $grid.SelectSingleNode("./local:SecretValueEditor[contains(@Value,'Binding Value, Mode=TwoWay')]", $ns)
if (!$secret) { throw 'Missing two-way secret value binding' }
if ($secret.GetAttribute('ValueEdited') -ne 'SecretValueEditor_ValueEdited') { throw 'Secret edits no longer trigger persistence' }
if (!$grid.SelectSingleNode("./w:Button[@Click='RemoveEnvironmentVariableButton_Click']", $ns)) { throw 'Remove action lost' }
if (!$section.SelectSingleNode(".//w:Button[@Click='AddEnvironmentVariableButton_Click']", $ns)) { throw 'Add action lost' }
Write-Host 'PASS: shared environment variable table header and aligned editable rows'
Write-Host 'PASS: no repeated field labels; add/remove and three existing bindings retained'
