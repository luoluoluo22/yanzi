#!/usr/bin/env python3
"""Deterministic source inventory; not a claim of visual or behavioral acceptance."""
import json, re, subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[1]
out=root/'docs/ui-unification/inventory.json'
previous={}
if out.exists():
    saved=json.loads(out.read_text(encoding='utf-8-sig'))
    if saved.get('schema_version') != 1 or not isinstance(saved.get('items'),list):
        raise RuntimeError('Existing UI inventory schema is incompatible; refusing to overwrite audit evidence')
    for old in saved['items']:
        rel=old.get('path') if isinstance(old,dict) else None
        if not isinstance(rel,str) or not rel or rel in previous:
            raise RuntimeError(f'Invalid or duplicate UI inventory path: {rel!r}')
        previous[rel]=old
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
    if rel in ('src/OpenQuickHost/HotkeyCaptureWindow.xaml', 'src/OpenQuickHost/MouseTriggerCaptureWindow.xaml', 'src/OpenQuickHost/SkillExportOptionsWindow.xaml', 'src/OpenQuickHost/SimpleTextInputWindow.xaml', 'src/OpenQuickHost/WebDavCredentialWindow.xaml', 'src/OpenQuickHost/ProcessPickerWindow.xaml', 'src/OpenQuickHost/InstalledAppPickerDialog.xaml', 'src/OpenQuickHost/ColorPickerDialog.xaml', 'src/OpenQuickHost/PastedTextEditorWindow.xaml', 'src/OpenQuickHost/AiModelPickerWindow.xaml', 'src/OpenQuickHost/SearchScopesManagerWindow.xaml'):
        state='source-migrated-awaiting-windows-qa'
        shared=True
    old=previous.get(rel)
    if old is not None:
        state=old.get('status',state)
        evidence=old.get('evidence',[])
        if not isinstance(state,str) or not isinstance(evidence,list):
            raise RuntimeError(f'Invalid UI status or evidence for {rel}')
    else:
        evidence=['source: shared button styles + per-window YanziUi.ApplyTo; git diff --check'] if state.startswith('source-migrated') else []
    record=dict(old) if old is not None else {}
    record.update(path=rel,kind=group,shared_ui_reference=shared,legacy_style_signals=legacy,status=state,evidence=evidence)
    records.append(record)
seen={item['path'] for item in records}
for path, old in previous.items():
    if path in seen:
        continue
    orphan=old.copy()
    orphan['status']='missing-tracked-file-needs-review'
    orphan['evidence']=list(old.get('evidence',[]))
    marker='scanner: candidate absent from tracked UI source; review removal or relocation before closure'
    if marker not in orphan['evidence']:
        orphan['evidence'].append(marker)
    records.append(orphan)
records.sort(key=lambda entry: entry['path'])
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(dict(schema_version=1,base_commit=subprocess.check_output(['git','rev-parse','--short','HEAD'],cwd=root).decode().strip(),note='Persistent candidate-source inventory: re-scan preserves manual status and evidence and retains missing candidates; not a count of distinct runtime screens.',items=records),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
from collections import Counter
print('inventory',len(records),dict(Counter(r['kind'] for r in records)))
