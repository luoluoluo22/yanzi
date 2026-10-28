param([Parameter(Mandatory=$true)][string]$FixturePath)
$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
$base = $fixture.baseUrl
if ($base -notmatch '^http://127\.0\.0\.1:[0-9]+$') { throw 'Use only an isolated local Worker fixture.' }
function B64([byte[]]$Value) { [Convert]::ToBase64String($Value).TrimEnd('=').Replace('+','-').Replace('/','_') }
$user = 'protocol-' + [Guid]::NewGuid().ToString('N')
$header = B64 ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$payload = B64 ([Text.Encoding]::UTF8.GetBytes((@{sub=$user;username='protocol-test';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+300} | ConvertTo-Json -Compress)))
$signer = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('local-phone-message-verification'))
$token = "$header.$payload." + (B64 ($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$payload"))))
$signer.Dispose()
$checks = 0
$deviceNamespace = [Guid]::NewGuid().ToString('N') + '-'
$fixtureDevices = @('phone-origin','phone-second','desktop-alpha','desktop-bravo','panel-device','sensor-device')
function ReadFixtureResponse([string]$Raw) {
    foreach ($name in $fixtureDevices) { $Raw=$Raw.Replace($deviceNamespace+$name,$name) }
    return $Raw | ConvertFrom-Json
}
function Check($Condition,[string]$Label) { if (-not $Condition) { throw "Protocol assertion failed: $Label" }; $script:checks++ }
function Request([string]$Path,[string]$Method='GET',$Body=$null,[bool]$Authorized=$true) {
    foreach ($name in $fixtureDevices) { $Path=$Path.Replace($name,$deviceNamespace+$name) }
    $options = @{Uri=$base+$Path;Method=$Method;UseBasicParsing=$true}
    if ($Authorized) { $options.Headers = @{Authorization="Bearer $token"} }
    if ($null -ne $Body) {
        $json=$Body | ConvertTo-Json -Depth 12 -Compress
        foreach ($name in $fixtureDevices) { $json=$json.Replace(('"'+$name+'"'),('"'+$deviceNamespace+$name+'"')) }
        $options.ContentType='application/json'; $options.Body=[Text.Encoding]::UTF8.GetBytes($json)
    }
    try { $response=Invoke-WebRequest @options; return @{status=[int]$response.StatusCode;body=(ReadFixtureResponse $response.Content)} }
    catch {
        $failure=$_.Exception
        while ($failure.InnerException -and -not $failure.Response) { $failure=$failure.InnerException }
        if (-not $failure.Response) { throw }
        if ($_.ErrorDetails.Message) {
            $raw=$_.ErrorDetails.Message
        } elseif ($failure.Response -is [System.Net.Http.HttpResponseMessage]) {
            $raw=$failure.Response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        } else {
            $reader=New-Object IO.StreamReader($failure.Response.GetResponseStream())
            try { $raw=$reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        if ([string]::IsNullOrWhiteSpace($raw) -and $_.ErrorDetails.Message) { $raw=$_.ErrorDetails.Message }
        return @{status=[int]$failure.Response.StatusCode;body=(ReadFixtureResponse $raw)}
    }
}
function Inbox([string]$Id) { return @((Request ('/v1/me/mobile/messages?deviceId='+$Id)).body.items) }
function ContainsMessage([string]$Device,[string]$Id) { return @(Inbox $Device | Where-Object {$_.messageId -eq $Id}).Count -eq 1 }
Check ((Request '/v1/me/devices/protocol' 'GET' $null $false).status -eq 401) 'protocol discovery requires auth'
$protocol=Request '/v1/me/devices/protocol'
Check ($protocol.status -eq 200 -and $protocol.body.version -eq 1) 'protocol version discovery'
foreach ($device in @(
    @{deviceId='phone-origin';platform='android';displayName='Sender'},
    @{deviceId='phone-second';platform='android';displayName='Second phone'},
    @{deviceId='desktop-alpha';platform='desktop';displayName='Desktop A'},
    @{deviceId='desktop-bravo';platform='desktop';displayName='Desktop B'},
    @{deviceId='panel-device';platform='iot';displayName='Future device';capabilities=@{receiveAccountChat=$true}},
    @{deviceId='sensor-device';platform='sensor';displayName='No chat capability'})) {
    Check ((Request '/v1/me/devices' 'POST' $device).status -eq 200) 'register known and extensible device classes'
}
$targeted=@{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='text';title='YanziChat';text='private target';payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')}
$lanCapabilities=@{autoAccountLan=$true;lanPort=42982}
Request '/v1/me/devices' 'POST' @{deviceId='phone-origin';platform='android';displayName='Sender';capabilities=$lanCapabilities} | Out-Null
Request '/v1/me/devices' 'POST' @{deviceId='desktop-alpha';platform='desktop';displayName='Desktop A';capabilities=@{autoAccountLan=$true;lanPort=42980}} | Out-Null
$phoneLinks=(Request '/v1/me/devices/lan-links' 'POST' @{deviceId='phone-origin'}).body
$desktopLinks=(Request '/v1/me/devices/lan-links' 'POST' @{deviceId='desktop-alpha'}).body
Check ($phoneLinks.items[0].pairId -eq $desktopLinks.items[0].pairId -and $phoneLinks.items[0].key -eq $desktopLinks.items[0].key) 'same account automatically receives matching device link keys'
Check (@($phoneLinks.items).Count -eq 1 -and $phoneLinks.items[0].desktopDeviceId -eq 'desktop-alpha') 'automatic links include only enabled same-account devices'
Check ((Request '/v1/me/devices/lan-links' 'POST' @{deviceId='sensor-device'}).status -eq 409) 'unregistered auto-LAN source cannot fetch links'
$sent=Request '/v1/me/mobile/messages' 'POST' $targeted
Check ($sent.status -eq 200) 'explicit chat enqueue'
$id=$sent.body.messageId
Check (ContainsMessage 'desktop-alpha' $id) 'explicit recipient receives'
foreach($device in @('desktop-bravo','panel-device','phone-origin')) { Check (-not (ContainsMessage $device $id)) 'explicit recipient isolation' }
Check ((Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-bravo'}).status -eq 404) 'wrong recipient cannot ack'
Check ((Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha'}).status -eq 200) 'correct recipient ack'
$broadcast=@{sourceDeviceId='phone-origin';targetPlatform='desktop';kind='text';title='YanziChat';text='account chat';payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')}
$sent=Request '/v1/me/mobile/messages' 'POST' $broadcast
$id=$sent.body.messageId
foreach($device in @('desktop-alpha','desktop-bravo','panel-device','phone-second')) { Check (ContainsMessage $device $id) 'account chat capability routing' }
foreach($device in @('phone-origin','sensor-device')) { Check (-not (ContainsMessage $device $id)) 'sender and nonparticipating device excluded' }
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha'} | Out-Null
Check (ContainsMessage 'desktop-bravo' $id) 'one ack cannot consume another recipient message'
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='panel-device'} | Out-Null
Check (@((Request "/v1/me/mobile/messages/$id").body.receipts).Count -eq 2) 'per-device receipts'
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$true;result='saved'} | Out-Null
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$false;resultState='unknown'} | Out-Null
Check ((Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha'}).body.status -eq 'completed') 'late generic or uncertain chat ack cannot downgrade device completion'
$chatReceipts=@((Request "/v1/me/mobile/messages/$id").body.receipts)
Check (@($chatReceipts | Where-Object { $_.deviceId -eq 'desktop-alpha' -and $_.status -eq 'completed' }).Count -eq 1 -and @($chatReceipts | Where-Object { $_.deviceId -eq 'panel-device' -and $_.status -eq 'acked' }).Count -eq 1) 'each device retains its own monotonic chat receipt'
$savedAccountToken=$token
$otherPayload=B64 ([Text.Encoding]::UTF8.GetBytes((@{sub=('other-account-'+[Guid]::NewGuid().ToString('N'));username='other-account';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+300} | ConvertTo-Json -Compress)))
$otherSigner=[Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('local-phone-message-verification'))
try {
    $token="$header.$otherPayload."+(B64 ($otherSigner.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$otherPayload"))))
    Check ((Request "/v1/me/mobile/messages/$id").status -eq 404) 'another account cannot read message or receipts'
    Check ((Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$true}).status -eq 404) 'another account cannot acknowledge a known device message'
    Check ((Request '/v1/me/devices/lan-links' 'POST' @{deviceId='phone-origin'}).status -eq 404) 'another account cannot fetch device link keys'
} finally { $token=$savedAccountToken;$otherSigner.Dispose() }
$repeat=@{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='notify';text='stable';payload=[ordered]@{z=1;a=[ordered]@{y=2;x=3}};clientMessageId=[Guid]::NewGuid().ToString('N')}
$first=Request '/v1/me/mobile/messages' 'POST' $repeat
$repeat.payload=[ordered]@{a=[ordered]@{x=3;y=2};z=1}
$second=Request '/v1/me/mobile/messages' 'POST' $repeat
Check ($second.status -eq 200 -and $second.body.deduplicated -and $first.body.messageId -eq $second.body.messageId) 'canonical idempotency'
$repeat.text='changed'
Check ((Request '/v1/me/mobile/messages' 'POST' $repeat).status -eq 409) 'same id different content rejected'
$repeat.protocolVersion=99
$version=Request '/v1/me/mobile/messages' 'POST' $repeat
Check ($version.status -eq 426 -and $version.body.details -and @($version.body.details.supportedVersions) -contains 1) 'unsupported version negotiation'
$command=@{sourceDeviceId='phone-origin';targetPlatform='desktop';kind='run-shell';text='Write-Output test';payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')}
$ambiguous=Request '/v1/me/mobile/messages' 'POST' $command
Check ($ambiguous.status -eq 409 -and $ambiguous.body.error -eq 'target_device_required') 'ambiguous command cannot broadcast'
$command.targetDeviceId='desktop-alpha'
$sent=Request '/v1/me/mobile/messages' 'POST' $command
Check ($sent.status -eq 200 -and (ContainsMessage 'desktop-alpha' $sent.body.messageId) -and -not (ContainsMessage 'desktop-bravo' $sent.body.messageId)) 'command target isolation'
$command.Remove('targetDeviceId');$command.targetPlatform='iot';$command.clientMessageId=[Guid]::NewGuid().ToString('N')
$sent=Request '/v1/me/mobile/messages' 'POST' $command
Check ($sent.status -eq 200 -and (Request ("/v1/me/mobile/messages/"+$sent.body.messageId)).body.targetDeviceId -eq 'panel-device') 'single legacy platform command is pinned'
$retry=Request '/v1/me/mobile/messages' 'POST' $command
Check ($retry.status -eq 200 -and $retry.body.messageId -eq $sent.body.messageId) 'pinned command retry identity'
$command.targetDeviceId='desktop-alpha';$command.clientMessageId=[Guid]::NewGuid().ToString('N')
$command.traceId='trace-lifecycle';$command.correlationId='request-lifecycle'
$sent=Request '/v1/me/mobile/messages' 'POST' $command
$id=$sent.body.messageId
$detail=(Request "/v1/me/mobile/messages/$id").body
Check (([DateTimeOffset]::Parse($detail.expiresAt)-[DateTimeOffset]::UtcNow).TotalSeconds -le 121) 'commands receive a short server deadline'
Check ($detail.traceId -eq 'trace-lifecycle' -and $detail.clientMessageId -eq $command.clientMessageId) 'trace and logical identity survive transport'
Check ((Request "/v1/me/mobile/messages/$id/cancel" 'POST' @{deviceId='desktop-bravo'}).status -eq 403) 'only source device may cancel'
Check ((Request "/v1/me/mobile/messages/$id/cancel" 'POST' @{deviceId='phone-origin'}).body.status -eq 'cancelled') 'sender cancellation terminal'
Check (-not (ContainsMessage 'desktop-alpha' $id)) 'cancelled message never replayed'
Check (-not (Request "/v1/me/mobile/messages/$id/claim" 'POST' @{deviceId='desktop-alpha'}).body.acquired) 'cancel prevents execution claim'
Check ((Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$true}).body.status -eq 'cancelled') 'late ack cannot revive cancelled execution'
$command.clientMessageId=[Guid]::NewGuid().ToString('N')
$sent=Request '/v1/me/mobile/messages' 'POST' $command
$id=$sent.body.messageId
Check ((Request "/v1/me/mobile/messages/$id/claim" 'POST' @{deviceId='desktop-bravo'}).status -eq 404) 'wrong device cannot claim'
Check ((Request "/v1/me/mobile/messages/$id/claim" 'POST' @{deviceId='desktop-alpha'}).body.acquired) 'single execution claim acquired'
Check (-not (Request "/v1/me/mobile/messages/$id/claim" 'POST' @{deviceId='desktop-alpha'}).body.acquired) 'lost claim ack does not grant second execution'
Check ((Request "/v1/me/mobile/messages/$id/cancel" 'POST' @{deviceId='phone-origin'}).status -eq 409) 'running execution cannot claim cancellation'
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$true;result='done'} | Out-Null
Check ((Request "/v1/me/mobile/messages/$id").body.status -eq 'completed') 'claimed result persisted'
Request "/v1/me/mobile/messages/$id/ack" 'POST' @{deviceId='desktop-alpha';success=$false;resultState='unknown';result='late uncertain'} | Out-Null
Check ((Request "/v1/me/mobile/messages/$id").body.status -eq 'completed') 'late uncertainty cannot downgrade confirmed result'
$command.clientMessageId=[Guid]::NewGuid().ToString('N')
$sent=Request '/v1/me/mobile/messages' 'POST' $command
$unknownId=$sent.body.messageId
Request "/v1/me/mobile/messages/$unknownId/claim" 'POST' @{deviceId='desktop-alpha'} | Out-Null
Request "/v1/me/mobile/messages/$unknownId/ack" 'POST' @{deviceId='desktop-alpha';success=$false;resultState='unknown';result='execution_result_unknown'} | Out-Null
Check ((Request "/v1/me/mobile/messages/$unknownId").body.status -eq 'unknown') 'uncertain result distinct from failed and never pending'
Check (-not (Request "/v1/me/mobile/messages/$unknownId/claim" 'POST' @{deviceId='desktop-alpha'}).body.acquired) 'uncertain command cannot reacquire execution'
Request "/v1/me/mobile/messages/$unknownId/ack" 'POST' @{deviceId='desktop-alpha';success=$true;result='confirmed later'} | Out-Null
Check ((Request "/v1/me/mobile/messages/$unknownId").body.status -eq 'completed') 'late confirmed result resolves uncertainty'
$command.traceId='changed-trace'
Check ((Request '/v1/me/mobile/messages' 'POST' $command).status -eq 409) 'trace changes cannot reuse logical identity'
$command.traceId='trace-lifecycle'
$scopeId=[Guid]::NewGuid().ToString('N')
$firstScoped=Request '/v1/me/mobile/messages' 'POST' @{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='notify';text='scope-a';clientMessageId=$scopeId}
$secondScoped=Request '/v1/me/mobile/messages' 'POST' @{sourceDeviceId='phone-second';targetDeviceId='desktop-alpha';kind='notify';text='scope-b';clientMessageId=$scopeId}
Check ($firstScoped.status -eq 200 -and $secondScoped.status -eq 200 -and $firstScoped.body.messageId -ne $secondScoped.body.messageId) 'transaction IDs scoped to source device'
$command.clientMessageId=[Guid]::NewGuid().ToString('N');$command.expiresAt=[DateTimeOffset]::UtcNow.AddSeconds(-2).ToString('O')
Check ((Request '/v1/me/mobile/messages' 'POST' $command).status -eq 410) 'expired commands rejected before enqueue'
foreach ($text in @('page-one','page-two')) {
    Request '/v1/me/mobile/messages' 'POST' @{sourceDeviceId='phone-origin';targetDeviceId='desktop-bravo';kind='notify';text=$text;clientMessageId=[Guid]::NewGuid().ToString('N')} | Out-Null
}
$first=(Request '/v1/me/mobile/messages?deviceId=desktop-bravo&limit=1').body
Check ($first.hasMore -and @($first.items).Count -eq 1 -and $first.nextCursor -gt 0) 'bounded sequence page'
$next=(Request ('/v1/me/mobile/messages?deviceId=desktop-bravo&limit=1&after='+$first.nextCursor)).body
Check ($next.nextCursor -gt $first.nextCursor -and $next.items[0].messageId -ne $first.items[0].messageId) 'monotonic pagination independent of ack'
Check ((Request '/v1/me/mobile/messages?deviceId=desktop-bravo&after=-1').status -eq 400) 'invalid cursor rejected'
$ownerToken = $token
$grant = (Request '/v1/me/devices/phone-origin/credentials' 'POST' @{applicationId='limited-chat';scopes=@('chat.send','messages.receive');targetDeviceIds=@('desktop-alpha')}).body
try {
    $token = $grant.accessToken
    Check ((Request '/v1/me/devices/lan-links' 'POST' @{deviceId='phone-origin'}).status -eq 403) 'limited web credential cannot acquire full account LAN permissions'
    $limited = @{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='text';text='scoped chat';clientMessageId=[Guid]::NewGuid().ToString('N')}
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 200) 'device credential can send authorized chat'
    $limited.targetDeviceId='desktop-bravo'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'device grant cannot route to another device'
    $limited.targetDeviceId='desktop-alpha';$limited.kind='run-shell'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'chat grant cannot execute terminal'
    $limited.kind='text';$limited.sourceDeviceId='phone-second'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'device credential cannot forge sender'
    Check ((Request '/v1/me/devices/phone-origin/credentials' 'POST' @{scopes=@('terminal.execute')}).status -eq 403) 'device credential cannot mint broader credentials'
    Check ((Request '/v1/me/mobile/messages?deviceId=desktop-alpha').status -eq 403) 'device credential cannot read another inbox'
} finally { $token = $ownerToken }
$notes = (Request '/v1/me/devices/phone-origin/credentials' 'POST' @{applicationId='notes-web';scopes=@('capability.invoke:notes.read');targetDeviceIds=@('desktop-alpha')}).body
try {
    $token = $notes.accessToken
    $limited = @{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='capability.invoke';payload=@{name='notes.read'};clientMessageId=[Guid]::NewGuid().ToString('N')}
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 200) 'web app can invoke its approved capability'
    $limited.payload.name='notes.write'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'web app cannot widen capability scope'
} finally { $token = $ownerToken }
$reader = (Request '/v1/me/devices/phone-origin/credentials' 'POST' @{applicationId='file-reader';scopes=@('files.read');targetDeviceIds=@('desktop-alpha');fileRoots=@('C:\allowed')}).body
try {
    $token = $reader.accessToken
    $limited = @{sourceDeviceId='phone-origin';targetDeviceId='desktop-alpha';kind='fs-read';payload=@{path='C:\allowed\notes.txt'};clientMessageId=[Guid]::NewGuid().ToString('N')}
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 200) 'file reader limited to approved root'
    $limited.payload.path='C:\allowed\..\outside.txt'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'file root traversal denied'
    $limited.payload.path='C:\allowed-other\notes.txt'
    Check ((Request '/v1/me/mobile/messages' 'POST' $limited).status -eq 403) 'file prefix confusion denied'
} finally { $token = $ownerToken }
$scopedSocket = [Net.WebSockets.ClientWebSocket]::new()
$socketTimeout = [Threading.CancellationTokenSource]::new(10000)
try {
    $scopedSocket.Options.SetRequestHeader('Authorization','Bearer '+$grant.accessToken)
    $socketUrl = $base.Replace('http://','ws://') + '/v1/me/mobile/messages/ws?deviceId=' + $deviceNamespace + 'phone-origin'
    $scopedSocket.ConnectAsync([Uri]$socketUrl,$socketTimeout.Token).GetAwaiter().GetResult() | Out-Null
    $socketBuffer = [ArraySegment[byte]]::new([byte[]]::new(16384))
    $readyFrame = $scopedSocket.ReceiveAsync($socketBuffer,$socketTimeout.Token).GetAwaiter().GetResult()
    Check ($readyFrame.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Text) 'scoped credential opens realtime connection'
    Request ('/v1/me/devices/credentials/'+$grant.credentialId) 'DELETE' | Out-Null
    $closedFrame = $scopedSocket.ReceiveAsync($socketBuffer,$socketTimeout.Token).GetAwaiter().GetResult()
    Check ($closedFrame.MessageType -eq [Net.WebSockets.WebSocketMessageType]::Close) 'revocation closes existing scoped realtime connection'
} finally { $scopedSocket.Dispose(); $socketTimeout.Dispose() }
try { $token=$grant.accessToken;Check ((Request '/v1/me/devices/protocol').status -eq 401) 'revoked device credentials fail immediately' } finally { $token=$ownerToken }
Request '/v1/me/devices' 'POST' @{deviceId='phone-second';platform='android';displayName='Second phone';capabilities=@{networkLocation='Invented location'}} | Out-Null
$listed = (Request '/v1/me/devices').body.items
Check (@($listed | Where-Object { $_.deviceId -eq 'phone-second' -and $_.createdAt -and $_.lastSeenAt }).Count -eq 1) 'device list exposes first registration and latest activity'
Check (@($listed | Where-Object { $_.deviceId -eq 'phone-second' -and $_.lastLocation -eq 'Invented location' }).Count -eq 0) 'clients cannot forge network location'
$removedGrant = (Request '/v1/me/devices/phone-second/credentials' 'POST' @{applicationId='removal-test';scopes=@('device.presence','messages.receive')}).body
Check ((Request '/v1/me/devices/phone-second' 'DELETE' $null $false).status -eq 401) 'device removal requires account authentication'
$otherClaims = B64 ([Text.Encoding]::UTF8.GetBytes((@{sub=('other-'+$user);username='other';exp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+300} | ConvertTo-Json -Compress)))
$otherSigner = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('local-phone-message-verification'))
$otherToken = "$header.$otherClaims." + (B64 ($otherSigner.ComputeHash([Text.Encoding]::UTF8.GetBytes("$header.$otherClaims"))))
$otherSigner.Dispose()
try { $token=$otherToken; Check ((Request '/v1/me/devices/phone-second' 'DELETE').status -eq 404) 'one account cannot remove another accounts device' } finally { $token=$ownerToken }
Check ((Request '/v1/me/devices/phone-second' 'DELETE').status -eq 200) 'owner can remove device registration'
Check (@((Request '/v1/me/devices').body.items | Where-Object deviceId -eq 'phone-second').Count -eq 0) 'removed device disappears from target list'
Check ((Request '/v1/me/devices' 'POST' @{deviceId='phone-second';platform='android';displayName='Second phone';capabilities=@{}}).status -eq 403) 'old heartbeat cannot re-register deleted device'
Check ((Request '/v1/me/mobile/messages?deviceId=phone-second').status -eq 404) 'removed device cannot receive account messages'
Check (@((Request '/v1/me/devices/phone-origin/credentials').body.items | Where-Object credentialId -eq $removedGrant.credentialId).Count -eq 0) 'deletion is scoped to selected device'
try { $token=$removedGrant.accessToken; Check ((Request '/v1/me/devices/protocol').status -eq 401) 'removing device revokes its scoped credentials' } finally { $token=$ownerToken }
Write-Host "DEVICE_MESSAGE_PROTOCOL_ROUTING_VERSION_RECEIPTS_AND_IDEMPOTENCY=PASSED; checks=$checks"
