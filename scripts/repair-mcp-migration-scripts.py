from pathlib import Path
repo=Path('F:/Desktop/kaifa/OpenQuickHost/scripts')
p=repo/'configure-portable-mcp.ps1'
s=p.read_text(encoding='utf-8-sig')
start=s.index("foreach($f in @('local.ps1','startup.ps1','cloudflare.ps1','tunnel.ps1')) {")
end=s.index("Replace-Exact (Join-Path $service 'scripts\\connection-status.js')", start)
s=s[:start]+"# Portable PowerShell startup scripts are reset and patched safely by fix-portable-mcp-scripts.py.\n"+s[end:]
start=s.index("Replace-Exact $startHttp ",s.index("$startHttp=Join-Path $yService"))
end=s.index("$yanStart=@",start)
s=s[:start]+s[end:]
s=s.replace('Write-Output "PORTABLE_MCP_CONFIGURED=OK runtime=$data"',"& python (Join-Path $PSScriptRoot 'fix-portable-mcp-scripts.py')\nif($LASTEXITCODE -ne 0){throw 'Portable startup script patch failed'}\nWrite-Output \"PORTABLE_MCP_CONFIGURED=OK runtime=$data\"")
p.write_text(s,encoding='utf-8')
p=repo/'migrate-mcp-services.ps1'
s=p.read_text(encoding='utf-8-sig')
needle="    New-Item -Path $item.Target -ItemType Directory -Force | Out-Null"
s=s.replace(needle,"    if (Test-Path (Join-Path $item.Target 'package-lock.json')) { Write-Output ('SKIP_ALREADY_MIGRATED '+$item.Target); continue }\n"+needle)
p.write_text(s,encoding='utf-8')
print('MIGRATION_SCRIPTS_IDEMPOTENT=OK')
