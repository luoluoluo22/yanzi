#!/usr/bin/env python3
"""Summarize bounded multi-page public feed sampling without exposing feed text.

Use private local captures only. UI-unavailable pages are NEVER counted as
zero cards; completeness is measured only on successfully extracted cards.
"""
from collections import Counter
from datetime import datetime,timezone
import argparse
import json
import os
from pathlib import Path

APPS={"bilibili","xiaohongshu","douyin"}

def analyze_folder(folder):
    data=json.loads((folder/"summary.json").read_text(encoding="utf-8"))
    pages=data.get("pages",[])
    if not pages:return None
    app=data.get("app")
    if app not in APPS:return None
    good=[p for p in pages if p.get("ui_dump_success",p.get("node_count") is not None)]
    screenshot_files=[folder/p["screenshot_file"] for p in pages if p.get("screenshot_file")]
    if not all(f.is_file() for f in screenshot_files):
        raise FileNotFoundError("Missing screenshot from "+str(folder))
    all_cards=[]
    if good and (folder/"structured-cards.json").is_file():
        structured=json.loads((folder/"structured-cards.json").read_text(encoding="utf-8"))
        all_cards=[card for page in structured["pages"] for card in page]
        keys={c.get("stable_key") for c in all_cards if c.get("stable_key")}
    else:keys=set()
    denominator=len(all_cards)
    fields={}
    for attr in ("title","title_or_caption","author","likes","views","danmaku","duration","favorites","shares"):
        present=sum(bool(c.get(attr)) for c in all_cards)
        if present:fields[attr]={"observed":present,"of":denominator,
                                "rate":round(present/denominator,4) if denominator else None}
    return {
        "app":app,
        "package":data.get("package"),
        "capture_directory":str(folder),
        "screens_sampled":len(pages),
        "screenshots_saved":len(screenshot_files),
        "screen_bytes_total":sum(f.stat().st_size for f in screenshot_files),
        "ui_success_pages":len(good),
        "ui_unavailable_pages":len(pages)-len(good),
        "ui_completion_rate":round(len(good)/len(pages),3) if pages else None,
        "card_appearances":denominator if good else None,
        "different_visible_cards_by_title_and_author":len(keys) if good else None,
        "duplicate_card_appearances":denominator-len(keys) if good else None,
        "new_cards_by_page":[p.get("new_candidate_cards") for p in pages],
        "field_presence_among_extracted_card_appearances":fields if good else None,
        "warning":("No valid UI trees: do not interpret as zero visible content"
                   if not good else "UI field presence is not screenshot recall; true total unknown")
    }

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--root",type=Path,default=Path(os.getenv("LOCALAPPDATA","."))/
                   "OpenQuickHost"/"NetworkCaptures"/"K70")
    args=p.parse_args()
    observations={}
    for folder in sorted(args.root.glob("visible-feed-deep-*")):
        if not folder.is_dir() or not (folder/"summary.json").exists():continue
        result=analyze_folder(folder)
        if not result:continue
        # Latest complete 10-page run per app; skip early failed probes.
        if result["screens_sampled"]<10:continue
        if result["app"] in observations:
            prior=observations[result["app"]]
            if prior["capture_directory"]>result["capture_directory"]:continue
        observations[result["app"]]=result
    output={
        "schema_version":1,
        "analysis_utc":datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "purpose":"Bounded visible public feed: UI availability, card dedup and field presence",
        "reports":observations,
        "private_data_only":True,
        "note":"No personal screenshots, titles or author handles exported to Git."
    }
    path=args.root/"app-visible-feed-deep-index.json"
    path.write_text(json.dumps(output,ensure_ascii=False,indent=2),encoding="utf-8")
    print("PRIVATE_INDEX="+str(path))
    for name,d in observations.items():
        print(name,"screens",d["screens_sampled"],"UI",d["ui_success_pages"],
              "cards",d["card_appearances"],"unique",d["different_visible_cards_by_title_and_author"],
              "duplicates",d["duplicate_card_appearances"],
              "screenshots",d["screenshots_saved"])
if __name__=="__main__":main()
