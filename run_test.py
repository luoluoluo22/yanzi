import os, json
data = json.load(open(r'C:\Users\Administrator\AppData\Local\OpenQuickHost\Extensions\mini-browser\manifest.json', encoding='utf-8'))
with open(r'F:\Desktop\kaifa\OpenQuickHost\test_comp2\Action.cs', 'w', encoding='utf-8') as f:
    f.write(data['script']['source'])
