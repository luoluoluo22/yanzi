param([Parameter(Mandatory=$true)][string]$FixturePath)
$ErrorActionPreference = 'Stop'
$fixture = Get-Content -LiteralPath $FixturePath -Raw | ConvertFrom-Json
$base = $fixture.baseUrl
$headers = @{Authorization="Bearer $($fixture.token)"}
$bytes = [Text.Encoding]::UTF8.GetBytes(('range-check-content-' * 2048))
$sha = [Security.Cryptography.SHA256]::Create()
$hash = ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant()
$sha.Dispose()
$uploadHeaders = @{Authorization=$headers.Authorization;'X-Content-Sha256'=$hash}
$uploaded = Invoke-RestMethod "$base/v1/me/mobile/attachments?name=verification.bin" -Method POST -Headers $uploadHeaders -ContentType 'application/octet-stream' -Body $bytes
if ($uploaded.sha256 -ne $hash -or $uploaded.size -ne $bytes.Length) { throw 'Upload metadata mismatch' }
$fileMessageBody=@{sourceDeviceId=$fixture.desktopDeviceId;targetPlatform='android';kind='file';title='YanziChat';text='expiry-test';payload=@{attachmentId=$uploaded.attachmentId};expiresAt=[DateTimeOffset]::UtcNow.AddDays(30).ToString('o');clientMessageId=[Guid]::NewGuid().ToString('N')} | ConvertTo-Json -Compress
$fileMessage=Invoke-RestMethod "$base/v1/me/mobile/messages" -Method POST -Headers $headers -ContentType 'application/json' -Body $fileMessageBody
$fileDetail=Invoke-RestMethod "$base/v1/me/mobile/messages/$($fileMessage.messageId)" -Headers $headers
if ($fileDetail.expiresAt -ne $uploaded.expiresAt) {throw 'Queued attachment outlives its stored content'}
function RequestRange([string]$Range) {
    $request = [Net.HttpWebRequest]::Create("$base/v1/me/mobile/attachments/$($uploaded.attachmentId)/content")
    $request.Headers['Authorization'] = $headers.Authorization
    if ($Range -eq 'partial') { $request.AddRange(10,29) } else { $request.AddRange(99999999) }
    $response = $request.GetResponse()
    try {
        $buffer = New-Object IO.MemoryStream
        try { $response.GetResponseStream().CopyTo($buffer); $body = $buffer.ToArray() } finally { $buffer.Dispose() }
        return @{StatusCode=[int]$response.StatusCode;Headers=$response.Headers;Content=$body}
    } finally { $response.Dispose() }
}
$content = RequestRange 'partial'
if ($content.StatusCode -ne 206 -or $content.Headers['Content-Range'] -ne "bytes 10-29/$($bytes.Length)") { throw 'Range metadata mismatch' }
$actual = if ($content.Content -is [byte[]]) { $content.Content } else { [Text.Encoding]::UTF8.GetBytes($content.Content) }
if ([Convert]::ToBase64String($actual) -ne [Convert]::ToBase64String($bytes[10..29])) { throw 'Range bytes mismatch' }
$payload = @{sourceDeviceId=$fixture.desktopDeviceId;targetPlatform='android';kind='text';title='idempotency-test';text='repeat';payload=@{};clientMessageId=[Guid]::NewGuid().ToString('N')} | ConvertTo-Json -Compress
$first = Invoke-RestMethod "$base/v1/me/mobile/messages" -Method POST -Headers $headers -ContentType 'application/json' -Body $payload
$second = Invoke-RestMethod "$base/v1/me/mobile/messages" -Method POST -Headers $headers -ContentType 'application/json' -Body $payload
if ($first.messageId -ne $second.messageId -or -not $second.deduplicated) { throw 'Idempotency mismatch' }
function ExpectFailure([int]$Status, [scriptblock]$Action) {
    try { & $Action | Out-Null; throw 'Expected request failure' }
    catch {
        $failure = $_.Exception
        while ($failure.InnerException -and -not $failure.Response) { $failure = $failure.InnerException }
        if (-not $failure.Response -or [int]$failure.Response.StatusCode -ne $Status) { throw }
    }
}
if ($base -match '^http://127\.0\.0\.1:') {
    function EncodeJwt([byte[]]$data) { [Convert]::ToBase64String($data).TrimEnd('=').Replace('+','-').Replace('/','_') }
    $jwtHeader = EncodeJwt ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
    $jwtPayload = EncodeJwt ([Text.Encoding]::UTF8.GetBytes((@{sub=('foreign-'+[Guid]::NewGuid().ToString('N'));username='foreign-test';exp=([DateTimeOffset]::UtcNow.ToUnixTimeSeconds()+120)} | ConvertTo-Json -Compress)))
    $signer = [Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes('local-phone-message-verification'))
    $foreignToken = "$jwtHeader.$jwtPayload." + (EncodeJwt ($signer.ComputeHash([Text.Encoding]::UTF8.GetBytes("$jwtHeader.$jwtPayload"))))
    $signer.Dispose()
    ExpectFailure 404 { Invoke-RestMethod "$base/v1/me/mobile/attachments/$($uploaded.attachmentId)/content" -Headers @{Authorization="Bearer $foreignToken"} }
}
ExpectFailure 409 { Invoke-RestMethod "$base/v1/me/mobile/messages" -Method POST -Headers $headers -ContentType 'application/json' -Body $payload.Replace('repeat','different') }
ExpectFailure 416 { RequestRange 'invalid' }
ExpectFailure 400 { Invoke-RestMethod "$base/v1/me/mobile/attachments?name=bad.bin" -Method POST -Headers @{Authorization=$headers.Authorization;'X-Content-Sha256'=('0'*64)} -ContentType 'application/octet-stream' -Body $bytes }
$publicList = Invoke-RestMethod "$base/v1/debug/list-packages"
if (@($publicList.keys | Where-Object { $_ -like 'private-mobile/*' }).Count -gt 0) { throw 'Private attachment key exposed' }
Invoke-RestMethod "$base/v1/me/mobile/attachments/$($uploaded.attachmentId)" -Method DELETE -Headers $headers | Out-Null
ExpectFailure 404 { Invoke-RestMethod "$base/v1/me/mobile/attachments/$($uploaded.attachmentId)/content" -Headers $headers }
Write-Host 'ATTACHMENT_RANGE_CHECKSUM_DELETE_AND_MESSAGE_IDEMPOTENCY=PASSED'
