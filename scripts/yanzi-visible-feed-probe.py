#!/usr/bin/env python3
"""One-page public-content UI feasibility probe on an already unlocked user phone.

This script does not bypass lock screen, login, permissions or sandbox. It does not
enter chats/profiles, like, follow, comment or post. Only top-level public feeds.
All output is private on the authorized Windows computer; no cloud export.
"""
import argparse,collections,datetime,json,os,pathlib,re,subprocess,sys,time
from xml.etree import ElementTree as ET

APPS={"bilibili":"tv.danmaku.bili","xiaohongshu":"com.xingin.xhs","douyin":"com.ss.android.ugc.aweme"}
NAV={"首页","推荐","关注","热门","动态","发现","消息","搜索","直播","商城","我","我的",
     "视频","会员购","分区","知识","音乐","同城","精选","未登录","登录","立即登录",
     "扫一扫","扫一扫登录","设置","全部","搜索…","更多","返回","刷新","举报",
     "评论","点赞","分享","私信","私信聊天","关注TA","不感兴趣"}
RE_WORD=re.compile(r"[\u4e00-\u9fffA-Za-z]")
RE_RECT=re.compile(r"^\[(\d+),(\d+)\]\[(\d+),(\d+)\]$")
def invoke(adb, serial,*cmd,timeout=20):
    r=subprocess.run([adb,"-s",serial,*cmd],stdout=subprocess.PIPE,stderr=subprocess.PIPE,timeout=timeout)
    return r.returncode,r.stdout,r.stderr
def run(adb,serial,*cmd,timeout=20):
    exit,out,err=invoke(adb,serial,*cmd,timeout=timeout)
    if exit: raise RuntimeError("adb_command_failed:"+str(cmd[:2])+" "+err.decode("utf-8","replace")[:100])
    return out
def screen_state(adb,serial):
    trust=run(adb,serial,"shell","dumpsys","trust").decode("utf-8","replace")
    # TrustManager dumps current user before secondary profile users.
    current=next((line for line in trust.splitlines() if " (current)" in line and "deviceLocked=" in line),"")
    power=run(adb,serial,"shell","dumpsys","power").decode("utf-8","replace")
    unlocked=("deviceLocked=0" in current)
    awake=("mWakefulness=Awake" in power)
    return unlocked,awake
def focus(adb,serial):
    raw=run(adb,serial,"shell","dumpsys","window").decode("utf-8","replace")
    m=re.search(r"mCurrentFocus=(.*)",raw)
    return m.group(1).strip() if m else ""
def geometry(s):
    m=RE_RECT.match(s or "")
    return tuple(map(int,m.groups())) if m else None
def clean(val):
    return " ".join((val or "").split()).strip()
def norm(text):
    return re.sub(r"[\s\u200b\W_]+","",text.casefold(),flags=re.UNICODE)
def is_candidate(text):
    if not text or len(text)<7 or len(text)>180: return False
    if text in NAV or text.startswith("http"):return False
    if not RE_WORD.search(text):return False
    if len(re.findall(r"[A-Za-z\u4e00-\u9fff]",text))<5:return False
    # Do not guess anything is a title based only on being text.
    return True
def extract(xml):
    root=ET.fromstring(xml.strip())
    rows=[]
    nodes=list(root.iter("node"))
    for idx,n in enumerate(nodes):
        attrs=n.attrib
        rect=geometry(attrs.get("bounds"))
        if not rect:continue
        label=clean(attrs.get("text")) or clean(attrs.get("content-desc"))
        if not label:continue
        x1,y1,x2,y2=rect
        # Header/nav and outside-screen entries can be included only as structural.
        if is_candidate(label) and x2>x1 and y2>y1 and y1>=80 and y2<=2400:
            rows.append({"index":idx,"label":label,"region":[x1,y1,x2,y2],
                         "resource_id":attrs.get("resource-id",""),
                         "kind":"text" if attrs.get("text") else "description",
                         "class":attrs.get("class","").rsplit(".",1)[-1]})
    counts=collections.Counter()
    for n in nodes:
        a=n.attrib
        if a.get("scrollable")=="true":counts["scrollable"]+=1
        if a.get("clickable")=="true":counts["clickable"]+=1
        if clean(a.get("text")):counts["text"]+=1
        if clean(a.get("content-desc")):counts["description"]+=1
        if "WebView" in a.get("class",""):counts["webview"]+=1
    return {"node_count":len(nodes),"counts":dict(counts),"candidates":rows,
            "all_labels_count":sum(bool(clean(n.attrib.get("text")) or clean(n.attrib.get("content-desc"))) for n in nodes),
            "root_package":next((n.attrib.get("package") for n in nodes if n.attrib.get("package")),"")}
def capture(adb,serial):
    # Remote XML is immediately removed; no UI text pushed to cloud.
    path="/sdcard/yanzi_research_visible.xml"
    try:
        invoke(adb,serial,"shell","rm","-f",path)
        run(adb,serial,"shell","uiautomator","dump",path,timeout=28)
        xml=run(adb,serial,"exec-out","cat",path)
        return extract(xml)
    finally:
        invoke(adb,serial,"shell","rm","-f",path)
def screenshot(adb,serial,path):
    png=run(adb,serial,"exec-out","screencap","-p",timeout=20)
    if not png.startswith(b"\x89PNG"):raise RuntimeError("invalid_png")
    if len(png)>10_000_000:raise RuntimeError("image_exceeds_limit")
    path.write_bytes(png)
    return len(png)
def compare(a,b):
    s1={norm(r["label"]) for r in a["candidates"]}
    s2={norm(r["label"]) for r in b["candidates"]}
    common=s1&s2
    return {"page1_candidates":len(a["candidates"]),
            "page2_candidates":len(b["candidates"]),
            "normalized_unique_page1":len(s1),
            "normalized_unique_page2":len(s2),
            "overlap_count":len(common),"new_page2_count":len(s2-s1),
            "union_unique_count":len(s1|s2),
            "new_candidate_ratio":round(len(s2-s1)/len(s2),3) if s2 else 0}
def prune_older(root, days):
    cutoff=time.time()-days*86400
    allowed={"page1.png","page2.png","page1-review.jpg","page2-review.jpg","summary.json","structured-cards.json"}
    removed=0
    for d in root.glob("visible-feed-*"):
        if not d.is_dir() or d.stat().st_mtime>=cutoff:continue
        members=list(d.iterdir())
        if not all(f.is_file() and f.name in allowed for f in members):continue
        for f in members:f.unlink()
        d.rmdir()
        removed+=1
    return removed

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--app",choices=APPS,required=True)
    p.add_argument("--adb",default="adb")
    p.add_argument("--serial",default="e3e68545")
    p.add_argument("--scroll",action="store_true")
    p.add_argument("--retention-days",type=int,default=7)
    p.add_argument("--out-root",type=pathlib.Path,default=pathlib.Path(os.getenv("LOCALAPPDATA","."))/
                  "OpenQuickHost"/"NetworkCaptures"/"K70")
    args=p.parse_args()
    if not 1<=args.retention_days<=30:p.error("retention-days 1..30")
    args.out_root.mkdir(parents=True,exist_ok=True)
    pruned=prune_older(args.out_root,args.retention_days)
    if pruned:print("PRIVATE_OLD_SESSIONS_PRUNED="+str(pruned))
    if not all(screen_state(args.adb,args.serial)):
        print("BLOCKED_PHONE_LOCKED_OR_NOT_AWAKE=True")
        raise SystemExit(2)
    package=APPS[args.app]
    run(args.adb,args.serial,"shell","monkey","-p",package,
        "-c","android.intent.category.LAUNCHER","1")
    time.sleep(3)
    if package not in focus(args.adb,args.serial):
        print("BLOCKED_FOREGROUND_NOT_TARGET=True")
        raise SystemExit(3)
    now=datetime.datetime.now(datetime.timezone.utc)
    folder=args.out_root/("visible-feed-"+args.app+"-"+now.strftime("%Y%m%d-%H%M%S"))
    folder.mkdir(parents=True,exist_ok=False)
    record={"schema_version":1,"app":args.app,"package":package,"timestamp_utc":now.isoformat(),
            "scope":"public visible home feed; read-only",
            "classifier":"heuristic candidate labels only, not verified title/author extraction",
            "pages":[],"retention_days":args.retention_days}
    try:
        one=capture(args.adb,args.serial)
        one["screenshot_file"]="page1.png"
        one["screenshot_bytes"]=screenshot(args.adb,args.serial,folder/"page1.png")
        record["pages"].append(one)
        print(f"PAGE1 nodes={one['node_count']} candidates={len(one['candidates'])}",flush=True)
        if args.scroll:
            if not all(screen_state(args.adb,args.serial)) or package not in focus(args.adb,args.serial):
                print("SCROLL_ABORT_PHONE_OR_APP_CHANGED=True")
                raise SystemExit(4)
            run(args.adb,args.serial,"shell","input","swipe","510","1730","510","830","540")
            time.sleep(2)
            if not all(screen_state(args.adb,args.serial)) or package not in focus(args.adb,args.serial):
                print("SECOND_SNAPSHOT_ABORT_PHONE_OR_APP_CHANGED=True")
                raise SystemExit(4)
            two=capture(args.adb,args.serial)
            two["screenshot_file"]="page2.png"
            two["screenshot_bytes"]=screenshot(args.adb,args.serial,folder/"page2.png")
            record["pages"].append(two)
            record["comparison"]=compare(one,two)
            print("COMPARE="+json.dumps(record["comparison"],ensure_ascii=False),flush=True)
        (folder/"summary.json").write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")
        print("PRIVATE_LOCAL_DATA="+str(folder))
    finally:
        # Restore device home only if still inside this experiment app; don't
        # interrupt the owner if they have switched to another app.
        if package in focus(args.adb,args.serial):
            invoke(args.adb,args.serial,"shell","input","keyevent","KEYCODE_HOME")
if __name__=="__main__":main()
