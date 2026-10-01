param([Parameter(Mandatory=$true)][string]$FixturePath)
$ErrorActionPreference='Stop'
$f=Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
$base=$f.baseUrl
$headers=@{Authorization="Bearer $($f.token)"}
function Post([string]$path,$body) { Invoke-RestMethod "$base$path" -Method POST -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 10 -Compress) }
$a='android-a-'+[Guid]::NewGuid().ToString('N')
$b='android-b-'+[Guid]::NewGuid().ToString('N')
foreach($id in @($a,$b)){Post '/v1/me/devices' @{deviceId=$id;platform='android';displayName='chat-test';capabilities=@{}} | Out-Null}
$body=@{sourceDeviceId=$f.desktopDeviceId;targetDeviceId=$a;kind='text';title='YanziChat';text='fanout';payload=@{source='desktop-chat'};clientMessageId=[Guid]::NewGuid().ToString('N')}
$message=Post '/v1/me/mobile/messages' $body
$repeat=Post '/v1/me/mobile/messages' $body
if ($repeat.messageId -ne $message.messageId -or -not $repeat.deduplicated) {throw 'Shared chat retry broke idempotency'}
foreach($id in @($a,$b)) {
    $queue=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$id" -Headers $headers
    if (-not @($queue.items | Where-Object {$_.messageId -eq $message.messageId}).Count) {throw 'Peer did not receive shared chat'}
    Post "/v1/me/mobile/messages/$($message.messageId)/ack" @{deviceId=$id} | Out-Null
}
$queue=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$a" -Headers $headers
if (@($queue.items | Where-Object {$_.messageId -eq $message.messageId}).Count) {throw 'ACKed chat returned again'}
$queue=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$($f.desktopDeviceId)" -Headers $headers
if (@($queue.items | Where-Object {$_.messageId -eq $message.messageId}).Count) {throw 'Sender received own chat'}
$c='android-c-'+[Guid]::NewGuid().ToString('N')
Post '/v1/me/devices' @{deviceId=$c;platform='android';displayName='offline-peer';capabilities=@{}} | Out-Null
$queue=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$c" -Headers $headers
if (-not @($queue.items | Where-Object {$_.messageId -eq $message.messageId}).Count) {throw 'New/offline peer could not catch up after other ACKs'}
$body.kind='run-shell';$body.title='command';$body.clientMessageId=[Guid]::NewGuid().ToString('N')
$command=Post '/v1/me/mobile/messages' $body
$queue=Invoke-RestMethod "$base/v1/me/mobile/messages?deviceId=$b" -Headers $headers
if (@($queue.items | Where-Object {$_.messageId -eq $command.messageId}).Count) {throw 'Device command leaked to another device'}
Write-Host 'ACCOUNT_CHAT_INDEPENDENT_ACK_OFFLINE_CATCHUP_AND_COMMAND_ISOLATION=PASSED'
