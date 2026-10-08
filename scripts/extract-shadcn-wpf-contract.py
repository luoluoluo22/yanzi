#!/usr/bin/env python3
"""Extract a reproducible WPF comparison contract from the vendored shadcn source.

Never executes upstream TSX; only reads local pinned JSON, CSS and text examples.
Assumes browser root font-size=16px at 100% zoom. Values are layout CSS px,
which correspond to WPF DIP only under matching 96-DPI scaling.
"""
import argparse
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT=Path(__file__).resolve().parent.parent
BASE=ROOT/"docs/yanzi-ui-official-catalog/reference-package"
OUT=ROOT/"docs/yanzi-ui-official-catalog/wpf-source-contract.json"
TARGETS=("accordion","alert","card","calendar","combobox",
         "button","input","select","dialog","dropdown-menu","native-select")
THEME_KEYS=("background","foreground","card","card-foreground","popover",
            "popover-foreground","primary","primary-foreground","muted",
            "muted-foreground","accent","border","input","ring")
RADII_FACTORS={"sm":0.6,"md":0.8,"lg":1.0,"xl":1.4}
SPACE=4 # Tailwind spacing unit in CSS pixels
REQUIRED={
 "accordion":("accordion","accordion-item","accordion-trigger","accordion-content"),
 "alert":("alert","alert-title","alert-description"),
 "card":("card","card-header","card-content","card-footer"),
 "calendar":("calendar",),
 "combobox":("combobox-trigger","combobox-content","combobox-item","combobox-empty"),
 "native-select":("native-select-wrapper","native-select","native-select-icon","native-select-option"),
 "button":("button",),
 "input":("input",),
 "select":("select-trigger","select-content","select-item"),
 "dialog":("dialog","dialog-content","dialog-header","dialog-footer"),
 "dropdown-menu":("dropdown-menu-trigger","dropdown-menu-content","dropdown-menu-item",
                  "dropdown-menu-checkbox-item","dropdown-menu-radio-item"),
}
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify",action="store_true",help="Fail if contract or WPF roundings drift")
    args=parser.parse_args()
    manifest=json.loads((BASE/"manifest.json").read_text(encoding="utf-8"))
    tokens=json.loads((BASE/"theme/design-tokens.json").read_text(encoding="utf-8"))
    radius=tokens["light"]["--radius"]
    radius_rem=re.fullmatch(r"(\d+(?:\.\d+)?)rem",radius)
    if not radius_rem:raise RuntimeError("Unsupported upstream radius expression: "+radius)
    base=round(float(radius_rem[1])*16,2)
    computed={}
    for name,factor in RADII_FACTORS.items():
        expr=tokens["theme_inline"][f"--radius-{name}"]
        if name=="lg":
            assert expr=="var(--radius)"
        else:
            assert re.fullmatch(r"calc\(var\(--radius\) \* [0-9.]+\)",expr)
            assert abs(float(expr.split("*")[1].strip(" )"))-factor)<.001
        computed[name]=round(base*factor,2)
    selected={}
    for slug in TARGETS:
        path=BASE/"registry"/f"{slug}.json"
        data=json.loads(path.read_text(encoding="utf-8"))
        assert data["name"]==slug and data.get("files")
        content="\n".join(file["content"] for file in data["files"])
        slots=list(dict.fromkeys(re.findall(r'data-slot="([^"]+)"',content)))
        if not set(REQUIRED[slug]).issubset(slots):
            raise RuntimeError(f"{slug}: required parts disappeared: {REQUIRED[slug]}")
        example=BASE/"examples"/f"{slug}-example.tsx"
        assert example.is_file()
        class_strings=re.findall(r'(?:className=\{cn\(\s*|className=)"([^"]+)"',content)
        selected[slug]={
            "registry_file":str(path.relative_to(ROOT)).replace("\\","/"),
            "example_file":str(example.relative_to(ROOT)).replace("\\","/"),
            "parts":slots,
            "source_classes":class_strings,
        }
    expected={
        "project":"Yanzi UI WPF source-driven design comparison",
        "upstream_sha":manifest["upstream_sha"],
        "registry_style":manifest["registry_style"],
        "assumptions":{"root_font_css_px":16,"zoom_percent":100,"tailwind_spacing_css_px":SPACE},
        "radius_css_px":{"base":base,**computed},
        "spacing_css_px":{"1":SPACE,"2":SPACE*2,"2.5":SPACE*2.5,"3":SPACE*3,"4":SPACE*4,"7":SPACE*7},
        "css_tokens":{
            scheme:{key:tokens[scheme].get("--"+key) for key in THEME_KEYS}
            for scheme in ("light","dark")
        },
        "components":selected,
        "warning":"This contract describes official CSS source, not proof of WPF rendering parity. "
                  "Registry snapshot can differ slightly from the pinned GitHub example commit."
    }
    serialized=json.dumps(expected,ensure_ascii=False,indent=2)+"\n"
    if args.verify:
        if not OUT.exists() or OUT.read_text(encoding="utf-8")!=serialized:
            raise RuntimeError("Pinned reference contract is out of date.")
    else:
        OUT.write_text(serialized,encoding="utf-8")
    # Validate both theme dictionaries against the canonical upstream radius scale.
    ns="{http://schemas.microsoft.com/winfx/2006/xaml/presentation}"
    key="{http://schemas.microsoft.com/winfx/2006/xaml}Key"
    mapping={"Sm":"sm","Md":"md","Lg":"lg","Xl":"xl","Control":"md","Card":"xl"}
    for theme in ("Dark","Light"):
        path=ROOT/"src/Yanzi.UI.Wpf/Themes"/f"Tokens.{theme}.xaml"
        tree=ET.parse(path)
        definitions={el.get(key):el.text for el in tree.getroot()
                     if el.tag==ns+"CornerRadius"}
        for local,upstream in mapping.items():
            token="Yanzi.Radius."+local
            if token not in definitions:
                raise RuntimeError(f"Missing WPF radius {token} in {theme}")
            if float(definitions[token])!=expected["radius_css_px"][upstream]:
                raise RuntimeError(f"{theme} {token} mismatches shadcn source: "
                                   f"{definitions[token]} != {expected['radius_css_px'][upstream]}")
    assert expected["radius_css_px"]["sm"]==6 and expected["radius_css_px"]["xl"]==14
    print("PASS: source contract extracted for",len(selected),"components;",
          "radii",expected["radius_css_px"],"upstream",manifest["upstream_sha"][:12])
if __name__=="__main__":
    main()
