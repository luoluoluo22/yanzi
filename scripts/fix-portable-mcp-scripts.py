from pathlib import Path
import os, shutil
base=Path(os.environ['LOCALAPPDATA'])/'OpenQuickHost'/'Extensions'
r=base/'raccoon-manager'/'service'
y=base/'ext_ef5c4c9e801543059e8bc05760392ce1'/'service'
original_r=Path('F:/Desktop/kaifa/raccoon-mcp')
original_y=Path('F:/Desktop/kaifa/OpenQuickHost/tools/yanzi-mcp')
def change(file,old,new):
    data=file.read_text(encoding='utf-8-sig')
    if old not in data: raise RuntimeError(f"Missing patch anchor in {file}: {old}")
    file.write_text(data.replace(old,new),encoding='utf-8')
for name in ['local.ps1','startup.ps1','cloudflare.ps1','tunnel.ps1']:
    dst=r/'scripts'/name
    shutil.copy2(original_r/'scripts'/name,dst)
    change(dst,"$runtimeDir = Join-Path $projectDir '.raccoon-runtime'","$runtimeDir = if ($env:RACCOON_RUNTIME_DIR) { $env:RACCOON_RUNTIME_DIR } else { Join-Path $projectDir '.raccoon-runtime' }")
for name in ['local.ps1','tunnel.ps1']:
    change(r/'scripts'/name,"$envFile = Join-Path $projectDir '.env'","$envFile = Join-Path $runtimeDir '.env'")
change(r/'scripts'/'startup.ps1',"(Join-Path $projectDir '.env')","(Join-Path $runtimeDir '.env')")
shutil.copy2(original_y/'scripts'/'start-http.ps1',y/'scripts'/'start-http.ps1')
change(y/'scripts'/'start-http.ps1',"$runtimeDirectory = Join-Path $toolDirectory '.runtime'","$runtimeDirectory = if ($env:YANZI_MCP_RUNTIME_DIR) { $env:YANZI_MCP_RUNTIME_DIR } else { Join-Path $toolDirectory '.runtime' }")
print('PORTABLE_SCRIPTS_FIXED=OK')
