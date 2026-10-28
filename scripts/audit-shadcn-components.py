#!/usr/bin/env python3
"""Verify all shadcn base component docs against Yanzi, and write an auditable 64-row gap matrix."""
from __future__ import annotations
import concurrent.futures as cf
import datetime as dt
import json
import re
from pathlib import Path
import requests
from bs4 import BeautifulSoup

ROOT = Path(__file__).resolve().parent.parent
BASE = "https://ui.shadcn.com"
INDEX = BASE + "/docs/components"
OUT = ROOT / "docs" / "yanzi-ui-official-catalog"
OUT.mkdir(parents=True, exist_ok=True)

registry = (ROOT / "src/Yanzi.UI.Wpf/YanziComponentRegistry.cs").read_text(encoding="utf-8-sig")
gallery = (ROOT / "src/Yanzi.UI.Gallery/GalleryComponentAudit.cs").read_text(encoding="utf-8-sig")
entries = []
for name, status, api, group, page in re.findall(
    r'new\("([^"]+)",\s*YanziComponentStatus\.(\w+),\s*"([^"]+)",\s*"([^"]+)",\s*(\d+)\)',
    registry
):
    entries.append(dict(name=name, api=api, status=status, group=group, old_page=int(page)))

preview_block = gallery.split("IsolatedPreviewNames =", 1)[1].split("};", 1)[0]
preview_names = set(re.findall(r'"([^"]+)"', preview_block))
focus_block = gallery.split("OfficialFocus =", 1)[1].split("];", 1)[0]
focus = re.findall(r'^\s*"([^"]+)"\s*,?\s*$', focus_block, flags=re.M)
assert len(entries) == 64, f"Local count changed: {len(entries)}"
assert len(focus) == 64, f"Missing comparison focus: {len(focus)}"

index_response = requests.get(INDEX, timeout=25)
index_response.raise_for_status()
index_soup = BeautifulSoup(index_response.text, "html.parser")
official = []
for node in index_soup.select('a[href^="/docs/components/base/"]'):
    path = node.get("href", "").split("?")[0].rstrip("/")
    if re.fullmatch(r"/docs/components/base/[a-z0-9-]+", path) and path not in official:
        official.append(path)
expected = ["/docs/components/base/" + e["name"].lower().replace(" ", "-") for e in entries]
if len(official) != 64 or official != expected:
    missing = [x for x in official if x not in expected]
    invalid = [x for x in expected if x not in official]
    raise SystemExit(f"Live directory changed: official={len(official)} local={len(expected)} missing={missing} invalid={invalid}")

def fetch(item):
    url = BASE + "/docs/components/base/" + item["name"].lower().replace(" ", "-")
    try:
        response = requests.get(url, timeout=24)
        response.raise_for_status()
        page = BeautifulSoup(response.text, "html.parser")
        h1 = page.find("h1")
        headings = []
        for tag in page.select("h2,h3"):
            text = tag.get_text(" ", strip=True).rstrip("#").strip()
            if text and len(text) <= 85 and text not in headings:
                headings.append(text)
        return dict(http=response.status_code,
                    title=h1.get_text(" ", strip=True) if h1 else "",
                    headings=headings[:24], error=None)
    except Exception as exc:
        return dict(http=None, title="", headings=[], error=str(exc)[:240])

with cf.ThreadPoolExecutor(max_workers=8) as executor:
    remote = list(executor.map(fetch, entries))

risks = {
    "Chart": "本地仅有轻量柱图；其他类型图表及细节待验收",
    "Dialog": "已改为自绘 YanziContentDialog，支持可编辑内容；复杂焦点循环、RTL 与动画仍未验收",
    "Drawer": "已具备底部拖动手柄、鼠标/触摸位移、三档吸附与拖下关闭；惯性、回弹和滚动冲突尚需验证",
    "Combobox": "已新增自绘 YanziCombobox：单选过滤与空状态；多选 Chips、分组、ARIA 角色和 RTL 待验收",
    "Data Table": "排序、过滤、分页和虚拟化尚未逐项核对",
    "Context Menu": "一级相邻子菜单、互斥单选组、方向键和 Esc 已实现；多级嵌套、RTL、屏幕边界避让和延迟仍待校验",
    "Select": "原生 ComboBox 与官网自绘选择弹层差异待核对",
    "Navigation Menu": "目前复用了 Menubar；完整交互仍需复刻",
    "Dropdown Menu": "新版 YanziDropdownMenu 已有，需进一步做逐状态截图对比",
    "Button Group": "分段控件已适配，但官网各组合变体尚未逐一验收",
}
items = []
for i, (item, page) in enumerate(zip(entries, remote)):
    name = item["name"]
    items.append(dict(number=i + 1, name=name, api=item["api"],
        api_status=item["status"], old_group=item["group"],
        official_url=BASE + expected[i], http=page["http"],
        official_title=page["title"], official_headings=page["headings"],
        fetch_error=page["error"], independent_preview=name in preview_names,
        comparison_focus=focus[i],
        risk=risks.get(name, "完整视觉与交互对照尚未完成"),
        visual_result="待核对", interaction_result="待核对"))

snapshot = {
    "source": INDEX,
    "checked_utc": dt.datetime.now(dt.timezone.utc).isoformat(),
    "official_count": len(official),
    "local_count": len(items),
    "no_omissions": True,
    "independent_preview_count": sum(x["independent_preview"] for x in items),
    "visually_accepted_count": 0,
    "items": items
}
(OUT / "official-reference-snapshot.json").write_text(
    json.dumps(snapshot, ensure_ascii=False, indent=2), encoding="utf-8")

lines = [
    "# shadcn/ui x Yanzi UI: 64 item comparison register",
    "",
    "Official directory: " + INDEX,
    "Checked UTC: " + snapshot["checked_utc"],
    "",
    "Official and local catalog: 64/64 names, order and URLs match.",
    "Components with independent WPF previews: " + str(snapshot["independent_preview_count"]) + "/64.",
    "Components independently reviewed for visual and full interaction parity: 0/64.",
    "A reusable API entry is NOT proof of visual or behavioral parity.",
    "",
    "| # | Official component | HTTP | Yanzi API | Individual preview | Visual review | Component-specific checklist |",
    "|---:|---|---:|---|---|---|---|"
]
for x in items:
    preview = "yes" if x["independent_preview"] else "pending"
    lines.append("| {:02d} | [{}]({}) | {} | {} | {} | pending | {} |".format(
        x["number"], x["name"], x["official_url"], x["http"] or "error",
        x["api"], preview, x["comparison_focus"]))
lines += [
    "",
    "## Process",
    "Run python scripts/audit-shadcn-components.py to compare against the current live official component index.",
    "The gallery opens a distinct evaluation page for every item and persists six check results under the user's LocalAppData/OpenQuickHost/UiReview/ComponentAudit directory.",
    "Only actual visual and interaction validation may turn the pending state into pass.",
]
(OUT / "README.md").write_text("\n".join(lines), encoding="utf-8")
errors = [x for x in items if x["http"] != 200]
print("OFFICIAL_COUNT=" + str(len(official)))
print("LOCAL_COUNT=" + str(len(items)))
print("OFFICIAL_HTTP_200=" + str(len(items) - len(errors)))
print("INDIVIDUAL_PREVIEW=" + str(snapshot["independent_preview_count"]))
print("NEEDS_PREVIEW=" + str(len(items) - snapshot["independent_preview_count"]))
print("PARITY_UNVERIFIED=" + str(len(items)))
if errors:
    print("FETCH_ERRORS:", [(x["name"], x["fetch_error"]) for x in errors])
    raise SystemExit(1)
