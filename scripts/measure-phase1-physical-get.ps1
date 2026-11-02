<# Read-only count of physical Gitee GET dispatches in reported, CLOSED handler windows.
This is NOT an exact wall-clock window count: a window is logged on the NEXT GET,
and final open windows are not reported. Never treat logical Read requested as HTTP.
#>
[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][datetime]$Start,
 [Parameter(Mandatory=$true)][datetime]$End,
 [string]$LogRoot=(Join-Path $env:LOCALAPPDATA 'OpenQuickHost\logs'),
 [switch]$IncludeRotated
)
$ErrorActionPreference='Stop'
if($End -le $Start){throw 'End must be later than Start'}
$names=@('cloud-sync-diagnostics.log')
if($IncludeRotated){$names+=@('cloud-sync-diagnostics.log.1')}
$counts=[ordered]@{reportedWindows=0; physicalGetSentClosedWindows=0; physicalGetCompletedClosedWindows=0; success2xx=0; notFound404=0; retryable429Or5xx=0; transportErrors=0; canceled=0; otherHttp=0; missingFields=0; invalidWindows=0; invalidTimestamps=0}
$files=@()
foreach($name in $names){
 $path=Join-Path $LogRoot $name
 if(-not [IO.File]::Exists($path)){continue}
 $matched=0
 foreach($line in [IO.File]::ReadLines($path)){
  if($line -notmatch '^\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3})\]'){continue}
  $t=[datetime]::MinValue
  if(-not [datetime]::TryParseExact($Matches[1],'yyyy-MM-dd HH:mm:ss.fff',[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::None,[ref]$t)){$counts.invalidTimestamps++;continue}
  if($t -lt $Start -or $t -ge $End -or $line -notmatch '\[GiteeBackend\.Http\] GET transport window \|'){continue}
  $fields=@{}
  foreach($m in [regex]::Matches($line,'(?:^|[,|] )([A-Za-z][A-Za-z0-9]*)=(\d+)')){$fields[$m.Groups[1].Value]=[long]$m.Groups[2].Value}
  $keys=@('sent','completed','success2xx','notFound404','retryable429Or5xx','otherHttp','transportErrors','canceled')
  if(@($keys | Where-Object {-not $fields.ContainsKey($_)}).Count -gt 0){$counts.missingFields++;continue}
  # In-flight requests can cross windows: completed may exceed sent in one window.
  # Status/exception categories must nevertheless partition all completed GETs.
  if($fields.completed -ne ($fields.success2xx+$fields.notFound404+$fields.retryable429Or5xx+$fields.otherHttp+$fields.transportErrors+$fields.canceled)){$counts.invalidWindows++;continue}
  $counts.reportedWindows++;$matched++
  $counts.physicalGetSentClosedWindows+=$fields.sent
  $counts.physicalGetCompletedClosedWindows+=$fields.completed
  foreach($key in @('success2xx','notFound404','retryable429Or5xx','otherHttp','transportErrors','canceled')){$counts[$key]+=$fields[$key]}
 }
 $files+=@{name=$name;reportedWindows=$matched}
}
[pscustomobject]@{schema='yanzi.phase1.physical-get-closed-windows.v1';startBeijing=$Start.ToString('yyyy-MM-dd HH:mm:ss');endBeijingExclusive=$End.ToString('yyyy-MM-dd HH:mm:ss');counts=$counts;sourceFiles=$files;exactWallClockCount=$false;coverageComplete=$false;coverageStatus=$(if($counts.reportedWindows -eq 0){'INSUFFICIENT_NO_CLOSED_WINDOWS'}else{'PARTIAL_CLOSED_WINDOWS_ONLY'});note='Sum of logged CLOSED handler windows only; last unflushed window, handler restarts, and logs outside interval are not captured. Compare only matching workloads and equivalent complete log coverage.'}|ConvertTo-Json -Depth 5 -Compress
