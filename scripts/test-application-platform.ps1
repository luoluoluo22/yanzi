param([int]$Port=8814)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'dev-test-worker.ps1')
$root=Split-Path -Parent $PSScriptRoot
$config=Join-Path $root 'cloudflare\wrangler.toml'
$secret='application-test-'+[Guid]::NewGuid().ToString('N')
$user='application-test-'+[Guid]::NewGuid().ToString('N')
function Encode([byte[]]$bytes){[Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')}
$header=Encode ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$payload=Encode ([Text.Encoding]::UTF8.GetBytes((@{sub=$user;username='test';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+1200}|ConvertTo-Json -Compress)))
$unsigned="$header.$payload"
$hmac=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes($secret))
$token=$unsigned+'.'+(Encode ($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($unsigned))))
$base="http://127.0.0.1:$Port"
function Call([string]$path,[string]$method='GET',$value=$null,[string]$credential=$token){
    $options=@{Uri=$base+$path;Method=$method;Headers=@{Authorization='Bearer '+$credential};TimeoutSec=15}
    if($null -ne $value){$options.Body=$value|ConvertTo-Json -Depth 15 -Compress;$options.ContentType='application/json; charset=utf-8'}
    Invoke-RestMethod @options
}
function Denied([scriptblock]$action,[int]$status){
    try{& $action|Out-Null;throw 'Request unexpectedly succeeded'}catch{if([int]$_.Exception.Response.StatusCode -ne $status){throw}}
}
Stop-YanziLocalWorkerPort -Port $Port
& npx.cmd wrangler d1 migrations apply openquickhost-sync-db --local --config $config | Out-Null
if($LASTEXITCODE -ne 0){throw 'Local migrations failed'}
$worker=Start-Process npx.cmd -ArgumentList @('wrangler','dev','--local','--ip','127.0.0.1','--port',$Port,'--config',$config,'--var',"AUTH_TOKEN_SECRET:$secret",'--var','SYNC_OBJECTS_AUTHORITATIVE:true') -WorkingDirectory $root -WindowStyle Hidden -PassThru
try{
    $ready=$false
    for($i=0;$i -lt 50;$i++){try{if((Invoke-RestMethod "$base/health" -TimeoutSec 1).ok){$ready=$true;break}}catch{Start-Sleep -Milliseconds 400}}
    if(-not $ready){throw 'Worker not ready'}
    $written=Call '/v1/extension-data/quick-notes?key=notes.v1.json' 'PUT' @{accountId=$user;expectedRevision=0;content='integration-data'}
    $grant=Call '/v1/applications/quick-notes/grants' 'POST' @{userConsent=$true;clientName='integration';access='read-write'}
    $read=Call '/v1/extension-data/quick-notes?key=notes.v1.json' 'GET' $null $grant.accessToken
    if($read.content -ne 'integration-data' -or $read.revision -ne $written.revision){throw 'Scoped read failed'}
    Denied {Call '/v1/sync/objects' 'GET' $null $grant.accessToken} 401
    Denied {Call '/v1/extension-data/taskbar-calendar?key=calendar.v1.json' 'GET' $null $grant.accessToken} 403
    Denied {Call '/v1/extension-data/quick-notes?key=notes.v1.json' 'PUT' @{accountId=$user;expectedRevision=0;content='stale'} $grant.accessToken} 409
    Call '/v1/applications/library/taskbar-calendar' 'PUT' @{enabled=$true;expectedRevision=0}|Out-Null
    $library=Call '/v1/applications/library'
    if($library.applications[0].applicationId -ne 'taskbar-calendar'){throw 'Library selection missing'}
    Call ('/v1/applications/quick-notes/grants/'+$grant.grantId) 'DELETE'|Out-Null
    Denied {Call '/v1/extension-data/quick-notes?key=notes.v1.json' 'GET' $null $grant.accessToken} 403
    $invite=Call '/v1/applications/access-invites' 'POST' @{extensionId='quick-notes';key='notes.v1.json';access='read-write'}
    $path=([Uri]$invite.address).AbsolutePath
    $discovery=Call $path 'GET' $null ''
    if(-not $discovery.authorizationRequired){throw 'Discovery missing consent requirement'}
    $pending=Call ($path+'/requests') 'POST' @{clientName='Local integration AI';access='read-write'} ''
    $poll=([Uri]$pending.poll.url).AbsolutePath
    $waiting=Call $poll 'GET' $null $pending.requestSecret
    if($waiting.status -ne 'pending'){throw 'Unapproved request returned data'}
    Denied {Call ('/v1/applications/access-requests/'+$pending.requestId+'/decision') 'POST' @{approve=$true} ''} 401
    Call ('/v1/applications/access-requests/'+$pending.requestId+'/decision') 'POST' @{approve=$true}|Out-Null
    Start-Sleep -Seconds 3
    $approved=Call $poll 'GET' $null $pending.requestSecret
    if($approved.status -ne 'approved'){throw 'Approved request did not yield scoped token'}
    $external=Call '/v1/extension-data/quick-notes?key=notes.v1.json' 'GET' $null $approved.accessToken
    if($external.content -ne 'integration-data'){throw 'External consent data read failed'}
    Denied {Call '/v1/extension-data/quick-notes?key=other.json' 'GET' $null $approved.accessToken} 403
    Denied {Call ('/v1/applications/access-requests/'+$pending.requestId+'/decision') 'POST' @{approve=$false}} 409
    Write-Host 'APPLICATION_PLATFORM_INTEGRATION_PASSED: legacy storage, account library, scoped grant, consent discovery/polling/approval, key isolation, CAS and revocation'
}finally{Stop-YanziLocalWorkerPort -Port $Port}
