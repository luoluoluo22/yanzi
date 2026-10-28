param([switch]$Quick)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$setting=Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw -Encoding UTF8|ConvertFrom-Json
$port=[int]$setting.agentApiPort
if($port -le 0){$port=53919}
$hdr=@{'X-Yanzi-Token'=[string]$setting.agentApiToken;'Content-Type'='application/json; charset=utf-8'}
$uri="http://127.0.0.1:$port/v1/browser/execute"
$ids=New-Object 'System.Collections.Generic.List[int]'
$reports=New-Object System.Collections.ArrayList
function Call([hashtable]$payload,[int]$timeout=140){
  $sw=[Diagnostics.Stopwatch]::StartNew()
  $raw=$payload|ConvertTo-Json -Depth 10 -Compress
  $data=Invoke-RestMethod -Uri $uri -Method Post -Headers $hdr -Body ([Text.Encoding]::UTF8.GetBytes($raw)) -TimeoutSec $timeout
  $sw.Stop()
  if($data.status -ne 'success'){throw "response not successful: $($data.status): $($data.message)"}
  return [pscustomobject]@{Data=$data.data;Seconds=[math]::Round($sw.Elapsed.TotalSeconds,2)}
}
function Mark([string]$name,[bool]$pass,[string]$notes){
  [void]$reports.Add([pscustomobject]@{test=$name;pass=$pass;details=$notes;at=(Get-Date -Format s)})
  $flag=if($pass){'PASS'}else{'FAIL'}
  Write-Output "[$flag] $name :: $notes"
  if(-not $pass){throw "assertion failure: $name"}
}
function TestText($response,[string]$must,[string]$name){
  $t=[string]$response.Data.text
  Mark $name ($t.Contains($must)) ("text="+$t.Substring(0,[Math]::Min(100,$t.Length))+"; elapsed="+$response.Seconds)
}
try {
  $status=Call @{action='gemini_status'} 30
  Mark 'status' ($status.Data.extensionVersion -eq '0.5.58') ('version='+$status.Data.extensionVersion+' tabs='+@($status.Data.tabs).Count)
  $token='QZ1479'
  $first=Call @{action='gemini_send';tabPolicy='new';prompt="Return exactly $token";timeoutSeconds=110} 130
  $id=[int]$first.Data.tabId; $ids.Add($id)
  TestText $first $token 'first-real-message'
  Mark 'tab-id' ($id -gt 0 -and $first.Data.lifecycle.created -eq $true) ("id=$id")
  $s=Call @{action='gemini_status'} 25
  $tab=@($s.Data.tabs|Where-Object {$_.tabId -eq $id})|Select-Object -First 1
  Mark 'managed-tab-ownership' ($tab.managed -eq $true) ("managed=$($tab.managed) url=$($tab.url)")
  $hist=Call @{action='gemini_messages';tabId=$id} 30
  $foundUser=@($hist.Data.messages|Where-Object {$_.role -eq 'user' -and $_.text -match $token})
  $foundReply=@($hist.Data.messages|Where-Object {$_.role -eq 'assistant' -and $_.text -match $token})
  Mark 'history-correlation' ($foundUser.Count -ge 1 -and $foundReply.Count -ge 1) ("user=$($foundUser.Count) assistant=$($foundReply.Count)")
  $second=Call @{action='gemini_send';tabId=$id;prompt='What code did I give in the previous message? Reply only the code.';timeoutSeconds=110} 130
  TestText $second $token 'multi-turn-memory'
  $json=Call @{action='gemini_send';tabId=$id;prompt='Return a JSON code block containing exactly {"status":"ok","count":7,"service":"YanziGemini"} and nothing else.';timeoutSeconds=110} 130
  $j=$json.Data.json
  Mark 'json-parse' ($null -ne $j -and $j.status -eq 'ok' -and $j.count -eq 7 -and $j.service -eq 'YanziGemini') ("codeBlocks="+@($json.Data.codeBlocks).Count+" jsonPresent="+($null -ne $j))
  $switch=Call @{action='gemini_model_switch';tabId=$id;model='Flash'} 30
  Mark 'model-selection' ([string]$switch.Data.currentModel -match 'Flash') ("model=$($switch.Data.currentModel); changed=$($switch.Data.switched)")
  if(-not $Quick){
    $long=Call @{action='gemini_send';tabId=$id;prompt='Write a markdown table with 3 rows comparing Python, C# and Rust. Include columns language, strengths and common application, followed by a paragraph of more than 150 characters.';timeoutSeconds=110} 130
    $text=[string]$long.Data.text
    Mark 'markdown-long-reply' ($text.Length -ge 160 -and $text.Contains('Python') -and $text.Contains('Rust') -and $text.Contains('|')) ("chars="+$text.Length+"; elapsed="+$long.Seconds)
  }
  $rejected=$false
  try{ $ignore=Call @{action='gemini_send';tabId=$id;prompt='';timeoutSeconds=20} 32 }catch{$rejected=$true}
  Mark 'reject-empty-prompt' $rejected 'invalid input rejects instead of returning success'
  $diag=Call @{action='gemini_diagnostics';tabId=$id} 30
  Mark 'diagnostic-structure' ([string]$diag.Data.url -match '^https://gemini.google.com/' -and $diag.Data.readyState -eq 'complete') ("editorEngine=$($diag.Data.editorEngine); messages=$($diag.Data.messagesCount)")
  $cleanup=Call @{action='gemini_close';tabId=$id} 30
  Mark 'close-managed-tab' (@($cleanup.Data.closedTabIds) -contains $id) ("closed="+@($cleanup.Data.closedTabIds -join ',')+" skipped="+@($cleanup.Data.skippedTabIds -join ','))
  Write-Output 'REGRESSION_ALL_PASSED'
} catch {
  Write-Output ('REGRESSION_FAILED: ' + $_.Exception.Message)
} finally {
  $output=Join-Path $env:LOCALAPPDATA 'OpenQuickHost\GeminiFixBackup-20261014\regression-report.json'
  @($reports)|ConvertTo-Json -Depth 6|Set-Content -Path $output -Encoding UTF8
  Write-Output ('REPORT='+$output+' passed='+@($reports|Where-Object pass).Count+'/'+$reports.Count+' ownedTabIds='+($ids -join ','))
}
