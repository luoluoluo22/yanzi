"""Check icon and shared UI resource references without launching the apps."""

from collections import Counter
from pathlib import Path
import json
import re
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / "src" / "OpenQuickHost"
AVALONIA = ROOT / "src" / "Yanzi.Avalonia"
ANDROID = ROOT / "mobile" / "android" / "app" / "src" / "main"


def keys_in(source: str) -> set[str]:
    return set(re.findall(r'\["([\w-]+)"\]\s*=', source))


library = (DESKTOP / "ExtensionIconLibrary.cs").read_text(encoding="utf-8")
mdi = set(json.loads((DESKTOP / "Assets" / "mdi-icons.json").read_text(encoding="utf-8")))
local = keys_in(library.split("SvgAssetIcons =", 1)[0])
svg_section = library.split("SvgAssetIcons =", 1)[1].split("IconAliases =", 1)[0]
svg = dict(re.findall(r'\["([\w-]+)"\]\s*=\s*"([^"]+)"', svg_section))
aliases_section = library.split("IconAliases =", 1)[1].split("AppIcons =", 1)[0]
aliases = dict(re.findall(r'\["([\w-]+)"\]\s*=\s*"([\w-]+)"', aliases_section))
available = mdi | local | svg.keys()
errors: list[str] = []

for name, target in aliases.items():
    if target not in available:
        errors.append(f"Desktop alias {name} -> missing {target}")

references: Counter[str] = Counter()
for path in DESKTOP.glob("*.xaml"):
    source = path.read_text(encoding="utf-8-sig")
    for name in re.findall(r"\{local:IconGeometry\s+([\w:-]+)\}", source):
        references[name.removeprefix("mdi:")] += 1
for name in references:
    if aliases.get(name, name) not in available:
        errors.append(f"Desktop icon reference missing: {name}")

for name, file_name in svg.items():
    path = DESKTOP / "Assets" / "Icons" / file_name
    if not path.is_file():
        errors.append(f"Desktop SVG missing: {name} -> {file_name}")
        continue
    paths = [node for node in ET.parse(path).getroot().iter() if node.tag.endswith("path")]
    if not paths or any(not node.get("d") for node in paths):
        errors.append(f"Desktop SVG has an empty path: {file_name}")

for path in DESKTOP.glob("*.xaml"):
    try:
        ET.parse(path)
    except ET.ParseError as exc:
        errors.append(f"Desktop XAML invalid: {path.name}: {exc}")

avalonia_tokens = set(re.findall(
    r'x:Key="([\w-]+)"', (AVALONIA / "App.xaml").read_text(encoding="utf-8")
))
for path in AVALONIA.glob("*.xaml"):
    if path.name == "App.xaml":
        continue
    for name in re.findall(r"\{DynamicResource\s+(Overlay\w+)\}", path.read_text(encoding="utf-8-sig")):
        if name not in avalonia_tokens:
            errors.append(f"Avalonia resource missing: {path.name}: {name}")

mobile_mdi = set(json.loads((ANDROID / "assets" / "mdi-icons.json").read_text(encoding="utf-8")))
if mobile_mdi != mdi:
    errors.append("Desktop and Android MDI catalogs differ")
mobile_library = (ANDROID / "java" / "cc" / "luoluoluo" / "yanzi" / "mobile" / "MobileIconLibrary.java").read_text(encoding="utf-8")
mobile_local = set(re.findall(r'ICONS\.put\("([\w-]+)"', mobile_library))
for path in (ANDROID / "java").rglob("*.java"):
    for name in re.findall(r"mdi:([\w-]+)", path.read_text(encoding="utf-8-sig")):
        if name not in mobile_mdi | mobile_local:
            errors.append(f"Android icon reference missing: {path.name}: {name}")

print(f"Desktop: {len(references)} icon names, {sum(references.values())} XAML uses, {len(svg)} SVG mappings")
print(f"Avalonia: {len(avalonia_tokens)} application resources")
print(f"Android: {len(mobile_mdi)} MDI icons")
for error in errors:
    print("ERROR:", error)
sys.exit(1 if errors else 0)
