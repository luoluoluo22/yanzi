#!/usr/bin/env python3
"""Offline-only proof of structured public card extraction from visible UI evidence.

Runs on locally saved user-owned screen snapshots. Not an API emulator and never
requests hidden app databases or authenticated endpoint bodies.
"""
import collections
import json
from pathlib import Path
import re
import sys
import unicodedata

BASE=Path.home() # used only if cli path absent
def nkey(s):
    s=unicodedata.normalize("NFKC",s or "").casefold()
    return "".join(x for x in s if x.isalnum())
def bilibili(candidates):
    cards=[]
    for row in candidates:
        s=row["label"]
        if row["kind"]!="description" or not s.startswith(("视频,","直播,")):continue
        bits=s.split(",")
        if len(bits)<3 or "UP主" not in s:continue
        t=bits[0]
        title=bits[1].strip()
        if not title or len(title)<3:continue
        author=next((x[3:].strip() for x in bits[2:] if x.startswith("UP主")),"")
        def find(pattern):
            m=re.search(pattern,s)
            return m.group(1) if m else None
        card={"type":t,"title":title,"author":author,
              "views":find(r"([0-9.]+[万亿]?)观看"),
              "danmaku":find(r"([0-9.]+[万亿]?)弹幕"),
              "duration":find(r"时长([^,]+)"),
              "source":"accessibility description"}
        card["stable_key"]=nkey(title)+"|"+nkey(author)
        cards.append(card)
    return cards
def xiaohongshu(candidates):
    cards=[]
    for row in candidates:
        s=row["label"]
        if row["kind"]!="description" or not s.startswith(("视频 ","笔记 ")):continue
        m=re.match(r"^(视频|笔记) (.+) 来自(.+?) ([0-9.]*[万亿]?赞|赞)$",s)
        if not m:continue
        typ,title,author,likes=m.groups()
        if not title or len(title)<2:continue
        card={"type":typ,"title":title.strip(),"author":author.strip(),
              "likes":likes if likes!="赞" else None,
              "source":"accessibility description"}
        card["stable_key"]=nkey(title)+"|"+nkey(author)
        cards.append(card)
    return cards
def douyin(candidates):
    by_y=sorted(candidates,key=lambda r:r["region"][1])
    authors=[row["label"].strip() for row in by_y if row["kind"]=="text" and row["label"].startswith("@")]
    text=[row["label"].strip() for row in by_y if row["kind"]=="text"
          and row["region"][1]>1600 and not row["label"].startswith("@")]
    metrics={}
    for row in by_y:
        if row["kind"]!="description":continue
        s=row["label"]
        if "喜欢" in s and "按钮" in s:
            m=re.search(r"喜欢([0-9.]+[万亿]?)",s)
            if m:metrics["likes"]=m.group(1)
        if "收藏" in s and "按钮" in s:
            m=re.search(r"收藏([0-9.]+[万亿]?)",s)
            if m:metrics["favorites"]=m.group(1)
        if "分享" in s and "按钮" in s:
            m=re.search(r"分享([0-9.]+[万亿]?)",s)
            if m:metrics["shares"]=m.group(1)
    if not authors or not text:
        return []
    title=sorted(text,key=lambda x:len(x),reverse=True)[0]
    author=authors[-1]
    card={"type":"视频","title_or_caption":title,"author":author,
          "likes":metrics.get("likes"),"favorites":metrics.get("favorites"),
          "shares":metrics.get("shares"),
          "source":"separate accessibility text/description nodes"}
    card["stable_key"]=nkey(title)+"|"+nkey(author)
    return [card]
def collect(data):
    app=data["app"]
    mapper={"bilibili":bilibili,"xiaohongshu":xiaohongshu,"douyin":douyin}
    pages=[]
    for page in data["pages"]:
        raw=mapper[app](page["candidates"])
        seen=set()
        cards=[]
        for card in raw:
            key=card["stable_key"]
            if key and key not in seen:
                seen.add(key)
                cards.append(card)
        pages.append(cards)
    seen=set()
    summary=[]
    for i, cards in enumerate(pages,1):
        keys={c["stable_key"] for c in cards}
        summary.append({"page":i,"cards":len(cards),
                        "new_vs_previous":len(keys-seen),
                        "repeated_vs_previous":len(keys&seen)})
        seen.update(keys)
    return {"schema_version":1,"app":app,"package":data["package"],
            "source":"read-only uiautomator XML from unlocked visible public home feed",
            "two_page_card_counts":summary,"unique_structured_cards":len(seen),
            "pages":pages,"not_available":["true video ID","full unseen feed","API JSON response"],
            "limitations":["some card details may not be exposed in accessibility",
                           "douyin samples reflect one visible video per page",
                           "titles/likes copied verbatim from UI and may be truncated",
                           "no proof of inaccessible/background user data"]}
def main():
    for raw in sys.argv[1:]:
        path=Path(raw)
        data=json.loads(path.read_text(encoding="utf-8"))
        result=collect(data)
        dest=path.parent/"structured-cards.json"
        dest.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
        print(json.dumps({"app":result["app"],"unique":result["unique_structured_cards"],
                          "pages":result["two_page_card_counts"],
                          "example_titles":[(c.get("title") or c.get("title_or_caption"))[:100]
                                            for page in result["pages"] for c in page][:2],
                          "file":str(dest)},ensure_ascii=False))
if __name__=="__main__":main()
