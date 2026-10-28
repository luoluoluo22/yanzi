#!/usr/bin/env python3
"""Offline normalization of already-authorized native accessibility snapshots.

Output is stored only in the owner's private local capture directory.
The exported Git repo contains code but never personalized recommendation texts.
"""
import argparse
from collections import Counter
import datetime
import importlib.util
import json
import os
from pathlib import Path

PKG_TO_APP={"tv.danmaku.bili":"bilibili",
            "com.xingin.xhs":"xiaohongshu",
            "com.ss.android.ugc.aweme":"douyin"}
def load_parser():
    spec=importlib.util.spec_from_file_location(
        "visible_feed_parser",Path(__file__).resolve().parent/"yanzi-visible-feed-parse.py")
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module

def convert(snapshot):
    pkg=snapshot["packageName"]
    if pkg not in PKG_TO_APP: raise ValueError("non_public_feed_package")
    candidates=[]
    for idx, n in enumerate(snapshot.get("nodes",[])):
        bounds=n.get("bounds")
        if not isinstance(bounds,list) or len(bounds)!=4:continue
        for source,kind in (("text","text"),("contentDescription","description")):
            label=n.get(source)
            if not isinstance(label,str) or not label.strip():continue
            candidates.append({
                "index":idx,"label":label.strip(),"kind":kind,
                "region":bounds,"resource_id":n.get("resourceId",""),
                "class":"native_accessibility"})
    return PKG_TO_APP[pkg], candidates

def summarize(cards):
    return {
        "card_count":len(cards),
        "title_nonempty":sum(bool(c.get("title") or c.get("title_or_caption")) for c in cards),
        "author_nonempty":sum(bool(c.get("author")) for c in cards),
        "views_nonempty":sum(bool(c.get("views")) for c in cards),
        "duration_nonempty":sum(bool(c.get("duration")) for c in cards),
        "likes_nonempty":sum(bool(c.get("likes")) for c in cards),
        "favorites_nonempty":sum(bool(c.get("favorites")) for c in cards),
        "shares_nonempty":sum(bool(c.get("shares")) for c in cards)
    }

def main():
    a=argparse.ArgumentParser()
    a.add_argument("snapshots",type=Path,nargs="+")
    args=a.parse_args()
    parser=load_parser()
    batches={}
    for f in args.snapshots:
        data=json.loads(f.read_text(encoding="utf-8"))
        app, candidates=convert(data)
        packet={"app":app,"package":data["packageName"],
                "pages":[{"candidates":candidates}]}
        parsed=parser.collect(packet)
        cards=parsed["pages"][0]
        record={"schema_version":1,"timestamp_utc":datetime.datetime.now(datetime.timezone.utc).isoformat(),
                "source":"authorized MobilePublicFeedSnapshot; visible active app only",
                "package":data["packageName"],
                "native_nodes_returned":data["returnedNodes"],
                "visited_nodes":data["visitedNodes"],
                "native_elapsed_ms":data["elapsedMs"],
                "truncated":data["truncated"],
                "cards":cards,"field_summary":summarize(cards),
                "no_private_database":True}
        target=f.with_suffix(".parsed.json")
        target.write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")
        print("APP="+app+" NODES="+str(data["returnedNodes"])+" CARDS="+str(len(cards))+
              " SUMMARY="+json.dumps(record["field_summary"],ensure_ascii=False,separators=(",",":"))+
              " PRIVATE_PARSED_FILE="+str(target))
        batches.setdefault(app,[]).append(record)
    for app,records in batches.items():
        if len(records)<2:continue
        seen=set()
        for i,r in enumerate(records,1):
            keys={c["stable_key"] for c in r["cards"] if c.get("stable_key")}
            print("APP="+app+" PAGE="+str(i)+" NEW="+str(len(keys-seen))+
                  " OVERLAP="+str(len(keys&seen))+" CUMULATIVE="+str(len(seen|keys)))
            seen|=keys
if __name__=="__main__":main()
