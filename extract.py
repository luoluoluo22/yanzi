import json
with open(r'C:\Users\Administrator\AppData\Local\OpenQuickHost\Extensions\mini-browser\manifest.json', 'r', encoding='utf-8-sig') as f:
    data = json.load(f)
with open(r'F:\Desktop\kaifa\OpenQuickHost\mini-browser.cs', 'w', encoding='utf-8') as f:
    f.write(data['script']['source'])
