$ErrorActionPreference = 'Stop'
$token = 'yanzi-local-dev-token'
$headers = @{ 'X-Yanzi-Token' = $token }
$baseUrl = 'http://127.0.0.1:53919'

Write-Host '--- 1. GET /v1/quickpanel/groups ---'
Invoke-RestMethod -Uri "$baseUrl/v1/quickpanel/groups" -Method Get -Headers $headers | ConvertTo-Json -Depth 3

Write-Host '
--- 2. POST /v1/quickpanel/groups ---'
$groupBody = @{ name = 'Agent Test Group' } | ConvertTo-Json -Compress
$groupResult = Invoke-RestMethod -Uri "$baseUrl/v1/quickpanel/groups" -Method Post -Headers $headers -ContentType 'application/json' -Body $groupBody
$groupResult | ConvertTo-Json

Write-Host '
--- 3. POST /v1/quickpanel/add ---'
$addBody = @{ extensionId = 'agent-popup-demo-api'; groupId = $groupResult.id } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "$baseUrl/v1/quickpanel/add" -Method Post -Headers $headers -ContentType 'application/json' -Body $addBody | ConvertTo-Json

Write-Host '
--- 4. POST /v1/extensions (Create test run extension) ---'
$manifestObj = @{
    id = 'agent-run-test'
    name = 'API Run Test'
    runtime = 'csharp'
    entryMode = 'inline'
    script = @{ source = 'System.Console.WriteLine("Test execution successful!");' }
}
$manifestJson = $manifestObj | ConvertTo-Json -Compress
$runExtBody = @{ manifest = $manifestJson } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "$baseUrl/v1/extensions" -Method Post -Headers $headers -ContentType 'application/json' -Body $runExtBody | ConvertTo-Json

Write-Host '
--- 5. POST /v1/extensions/agent-run-test/run ---'
$runBody = @{ input = 'test' } | ConvertTo-Json -Compress
Invoke-RestMethod -Uri "$baseUrl/v1/extensions/agent-run-test/run" -Method Post -Headers $headers -ContentType 'application/json' -Body $runBody | ConvertTo-Json
