from pathlib import Path
import os
root=Path(os.environ['LOCALAPPDATA'])/'OpenQuickHost'/'Extensions'
for ext in ['raccoon-manager','ext_ef5c4c9e801543059e8bc05760392ce1']:
    p=root/ext/'managed-start.ps1'
    s=p.read_text(encoding='utf-8-sig')
    for old in ["$service = Join-Path $extension 'service'", "$service=Join-Path $extension 'service'"]:
        s=s.replace(old,"$service = & (Join-Path $extension 'stage-service.ps1')")
    assert "stage-service.ps1" in s
    p.write_text(s,encoding='utf-8')
y=root/'ext_ef5c4c9e801543059e8bc05760392ce1'/'service'/'src'/'http.js'
s=y.read_text(encoding='utf-8-sig')
s=s.replace("app.listen(3767,'127.0.0.1'","app.listen(Number(process.env.YANZI_MCP_PORT || '3767'),'127.0.0.1'")
y.write_text(s,encoding='utf-8')
# Eliminate the hard-coded development path in the optional Cloudflare setup helper.
p=root/'ext_ef5c4c9e801543059e8bc05760392ce1'/'service'/'scripts'/'connect-cloudflare.js'
s=p.read_text(encoding='utf-8-sig')
s=s.replace("const source='F:/Desktop/kaifa/raccoon-mcp/.raccoon-runtime/';",
"""import path from 'node:path';
const source=process.env.RACCOON_RUNTIME_DIR || path.join(process.env.LOCALAPPDATA || '', 'OpenQuickHost','McpRuntime','raccoon');""")
s=s.replace("source+'cloudflare.json'","path.join(source,'cloudflare.json')")
s=s.replace("source+'cloudflare-api-token.txt'","path.join(source,'cloudflare-api-token.txt')")
p.write_text(s,encoding='utf-8')
print('PORTABLE_STAGE_LINKED=OK')
