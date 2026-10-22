# Read-only acceptance test for two concurrently attached browser extensions.
# Does not log local API credentials or change browser profiles.
param([int]$Port = 42980)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$settings = Get-Content (Join-Path $env:LOCALAPPDATA 'OpenQuickHost\appsettings.local.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($settings.agentApiPort -and $Port -eq 42980) { $Port = [int]$settings.agentApiPort }
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(35)
$client.DefaultRequestHeaders.Add('X-Yanzi-Token', [string]$settings.agentApiToken)
$root = "http://127.0.0.1:$Port/v1/browser"
function Get-Json([string]$url) {
 $r=$client.GetAsync($url).GetAwaiter().GetResult()
 if (-not $r.IsSuccessStatusCode) {throw "HTTP status $([int]$r.StatusCode)"}
 return ($r.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json)
}
function Post-Json([object]$data) {
 $body=$data|ConvertTo-Json -Depth 8 -Compress
 $content=[System.Net.Http.StringContent]::new($body,[Text.Encoding]::UTF8,'application/json')
 $r=$client.PostAsync("$root/execute",$content).GetAwaiter().GetResult()
 $json=$r.Content.ReadAsStringAsync().GetAwaiter().GetResult()|ConvertFrom-Json
 return [pscustomobject]@{http=[int]$r.StatusCode;response=$json}
}
try {
 $status=Get-Json "$root/status"
 $connections=@($status.connections)
 if ($connections.Count -lt 2) {
   Write-Output ('INSUFFICIENT_CONNECTIONS=' + $connections.Count + ' (start Edge and Ego with distinct Yanzi helpers)')
   exit 2
 }
 $completed=@()
 foreach ($session in $connections | Select-Object -First 3) {
   $body=@{
      connectionId=$session.connectionId
      action='scrape'
      url='https://www.iana.org/domains/reserved'
      selectors=@{title='h1'}
      closeOnComplete=$true
      timeoutSeconds=25
   }
   $r=Post-Json $body
   $head=@($r.response.data.title)[0]
   $good=$r.http -eq 200 -and $r.response.status -eq 'success' -and
         $r.response.connectionId -eq $session.connectionId -and
         -not [string]::IsNullOrWhiteSpace($head)
   $completed+= [pscustomobject]@{browser=$session.browser;connectionId=$session.connectionId;success=$good}
   if(-not $good){throw ('Read-only workflow failed for '+$session.connectionId)}
 }
 Write-Output ('PASS MULTI_BROWSER='+$completed.Count)
 $completed|Format-Table -AutoSize|Out-String|Write-Output
} finally { $client.Dispose() }
