from pathlib import Path
import os
runtime=Path(os.environ['LOCALAPPDATA'])/'OpenQuickHost'/'McpRuntime'/'raccoon'
env=runtime/'.env'
s=env.read_text(encoding='utf-8-sig')
lines=s.splitlines()
new=[]
for line in lines:
    if line.startswith('RACCOON_AUDIT_LOG='):
        line='RACCOON_AUDIT_LOG='+str(runtime/'audit.jsonl')
    new.append(line)
env.write_text('\n'.join(new)+'\n',encoding='utf-8')
startup=Path(os.environ['LOCALAPPDATA'])/'OpenQuickHost'/'Extensions'/'raccoon-manager'/'service'/'scripts'/'startup.ps1'
s=startup.read_text(encoding='utf-8-sig')
s=s.replace("if ($useCloudflare) {","if ($useCloudflare -and -not $env:RACCOON_TUNNEL_ALREADY_ACTIVE) {")
startup.write_text(s,encoding='utf-8')
managed=startup.parents[2]/'managed-start.ps1'
s=managed.read_text(encoding='utf-8-sig')
s=s.replace("& (Join-Path $service 'scripts\\startup.ps1') -Recover:$Recover", """$legacyTunnel = @(Get-CimInstance Win32_Process | Where-Object {
  $_.Name -eq 'cloudflared.exe' -and $_.CommandLine -match 'raccoon-mcp' -and $_.CommandLine -match 'token-file'
})
if($legacyTunnel.Count -gt 0){$env:RACCOON_TUNNEL_ALREADY_ACTIVE='1'}
& (Join-Path $service 'scripts\\startup.ps1') -Recover:$Recover""")
managed.write_text(s,encoding='utf-8')
print('CUTOVER_PREPARED=OK')
