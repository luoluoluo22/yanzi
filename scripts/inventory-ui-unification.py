#!/usr/bin/env python3
"""Deterministic source inventory; not a claim of visual or behavioral acceptance."""
import json, re, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
tracked=subprocess.check_output(['git','ls-files','-z'],cwd=root).decode().split('\0')
records=[]
for rel in tracked:
    if not rel: continue
    p=root/rel
    if not p.is_file(): continue
    suffix=p.suffix.lower()
    group=None
    if suffix in ('.xaml','.axaml') and rel.startswith('src/'):
        group='desktop-xaml'
    elif suffix=='.xml' and '/res/layout/' in rel and rel.startswith('mobile/'):
        group='android-layout'
    elif suffix=='.java' and rel.startswith('mobile/') and re.search(r'(Activity|Fragment|Dialog|View|Popup|Overlay|Widget|Screen|Adapter)\.java$',rel):
        group='android-dynamic-ui-candidate'
    elif suffix in ('.html','.tsx','.jsx','.vue','.svelte') and (rel.startswith(('browser-extension/','cloudflare/','server/','mobile/')) or rel.startswith('docs/')):
        group='web-ui-candidate' if rel.startswith('docs/') else 'web-ui'
    elif suffix in ('.js','.mjs','.ts') and rel.startswith(('browser-extension/','cloudflare/','server/')) and re.search(r'(popup|page|view|dialog|panel|ui|overlay|toast|menu)',p.stem,re.I):
        group='web-dynamic-ui-candidate'
    elif suffix=='.cs' and rel.startswith('src/') and re.search(r'(Window|Dialog|Overlay|Toast|Menu|Popup|View|Panel)\.cs$',rel):
        group='desktop-dynamic-ui-candidate'
    if not group: continue
    data=p.read_text(encoding='utf-8-sig',errors='replace')
    shared=bool(re.search(r'Yanzi\.UI\.Wpf|YanziUi\.|Yanzi\.(?:Button|Input|Switch|Card)|YanziTheme',data))
    legacy=len(re.findall(r'(?:StaticResource|DynamicResource)\s+(?:PrimaryBtn|SecondaryBtn|BaseButton)|#[0-9a-fA-F]{6,8}\b',data))
    state='pending-manual-review'
    if rel in ('src/OpenQuickHost/HotkeyCaptureWindow.xaml', 'src/OpenQuickHost/MouseTriggerCaptureWindow.xaml', 'src/OpenQuickHost/SkillExportOptionsWindow.xaml', 'src/OpenQuickHost/SimpleTextInputWindow.xaml', 'src/OpenQuickHost/WebDavCredentialWindow.xaml'):
        state='source-migrated-awaiting-windows-qa'
        shared=True
    records.append(dict(path=rel,kind=group,shared_ui_reference=shared,legacy_style_signals=legacy,status=state,evidence=['source: shared button styles + per-window YanziUi.ApplyTo; git diff --check'] if state.startswith('source-migrated') else []))
out=root/'docs/ui-unification/inventory.json'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(dict(schema_version=1,base_commit=subprocess.check_output(['git','rev-parse','--short','HEAD'],cwd=root).decode().strip(),note='Candidate source inventory; every candidate requires manual verification; includes docs previews and code-behind candidates; not a count of distinct runtime screens.',items=records),ensure_ascii=False,indent=2)+'\n')
from collections import Counter
print('inventory',len(records),dict(Counter(r['kind'] for r in records)))
