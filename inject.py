import json
with open(r'F:\Desktop\kaifa\OpenQuickHost\mini-browser.cs', 'r', encoding='utf-8-sig') as f:
    source = f.read()

with open(r'C:\Users\Administrator\AppData\Local\OpenQuickHost\Extensions\mini-browser\manifest.json', 'r', encoding='utf-8-sig') as f:
    data = json.load(f)

data['script']['source'] = source

with open(r'C:\Users\Administrator\AppData\Local\OpenQuickHost\Extensions\mini-browser\manifest.json', 'w', encoding='utf-8') as f:
    json.dump(data, f, ensure_ascii=False, indent=2)
