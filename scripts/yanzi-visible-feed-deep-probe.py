#!/usr/bin/env python3
"""Read-only, bounded deep-scroll sampling of *visible* public app feeds.

No private app directory access, no background collection, no clicks, no messaging.
Aborts on lock / target lost; private evidence stays on local Windows user profile.
Screenshots saved on selected pages for human comparison, not every page.
"""
import argparse
import collections
from datetime import datetime,timezone
import importlib.util
import json
import os
from pathlib import Path
import sys
import time

HERE=Path(__file__).resolve().parent
def load(name, filename):
    spec=importlib.util.spec_from_file_location(name,HERE/filename)
    module=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
base=load("yanzi_visible_probe","yanzi-visible-feed-probe.py")
parser=load("yanzi_visible_parser","yanzi-visible-feed-parse.py")

def ensure_ready(adb,serial,package):
    if not all(base.screen_state(adb,serial)):
        raise RuntimeError("phone_is_locked_or_not_awake")
    if package not in base.focus(adb,serial):
        raise RuntimeError("foreground_app_changed")

def keep_snapshot(page, i, folder, screenshot_indexes, adb, serial):
    if i in screenshot_indexes:
        f="page-"+str(i)+".png"
        page["screenshot_file"]=f
        page["screenshot_bytes"]=base.screenshot(adb,serial,folder/f)
    return page

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--app",choices=base.APPS,required=True)
    p.add_argument("--adb",default="adb")
    p.add_argument("--serial",default="e3e68545")
    p.add_argument("--pages",type=int,default=10)
    p.add_argument("--retention-days",type=int,default=7)
    p.add_argument("--out-root",type=Path,default=Path(os.getenv("LOCALAPPDATA","."))/
                  "OpenQuickHost"/"NetworkCaptures"/"K70")
    args=p.parse_args()
    if not 2<=args.pages<=12:p.error("pages must be 2..12")
    if not 1<=args.retention_days<=30:p.error("retention-days 1..30")
    args.out_root.mkdir(parents=True,exist_ok=True)
    # Clean only our own expired deep-probe folders. This is run-triggered,
    # not a background deletion promise.
    allowed_files={"summary.json","statistics.json","structured-cards.json"}
    for stale in args.out_root.glob("visible-feed-deep-*"):
        if (not stale.is_dir() or stale.stat().st_mtime >=
                time.time()-args.retention_days*86400):
            continue
        children=list(stale.iterdir())
        if not all(x.is_file() and (x.name in allowed_files or
                   (x.name.startswith("page-") and x.suffix==".png"))
                   for x in children):
            continue
        for x in children:x.unlink()
        stale.rmdir()
    pkg=base.APPS[args.app]
    ensure_ready_before=all(base.screen_state(args.adb,args.serial))
    if not ensure_ready_before:
        print("BLOCKED_PHONE_LOCKED_OR_NOT_AWAKE=True",flush=True)
        raise SystemExit(2)
    print("START_TARGET="+args.app,flush=True)
    base.run(args.adb,args.serial,"shell","monkey","-p",pkg,
             "-c","android.intent.category.LAUNCHER","1")
    time.sleep(2.5)
    ensure_ready(args.adb,args.serial,pkg)
    folder=args.out_root/("visible-feed-deep-"+args.app+"-"+
                          datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S"))
    folder.mkdir(parents=True,exist_ok=False)
    record={"schema_version":2,"app":args.app,"package":pkg,
            "created_utc":datetime.now(timezone.utc).isoformat(timespec="seconds"),
            "scope":"visible public home recommendations; read-only scroll",
            "data_class":"private-personalized-public-feed",
            "retention_days":args.retention_days,
            "screenshots_every_page":True,
            "no_private_app_database_read":True,
            "requested_pages":args.pages,
            "pages":[]}
    screenshot_indexes=set(range(1,args.pages+1))
    disable_ui_dump=False
    last_set=set()
    no_new_streak=0
    prev_all=set()
    try:
        for i in range(1,args.pages+1):
            ensure_ready(args.adb,args.serial,pkg)
            page=None
            if not disable_ui_dump:
                ensure_ready(args.adb,args.serial,pkg)
                try:
                    page=base.capture(args.adb,args.serial)
                except (ValueError, OSError, RuntimeError, base.ET.ParseError):
                    # Continuous animations may make UIAutomator fail to reach idle.
                    # Treat missing UI as unknown, never as zero visible cards.
                    disable_ui_dump=True
                    print("UI_DUMP_UNAVAILABLE_DYNAMIC_PAGE=True",flush=True)
            ensure_ready(args.adb,args.serial,pkg)
            if page is None:
                page={"node_count":None,"counts":{},"candidates":[],
                      "root_package":pkg,"ui_dump_success":False,
                      "capture_error":"android_uiautomator_idle_or_tree_unavailable"}
            elif page.get("root_package")!=pkg:
                raise RuntimeError("UI_root_package_mismatch")
            else:
                page["ui_dump_success"]=True
            keep_snapshot(page,i,folder,screenshot_indexes,args.adb,args.serial)
            page["page_number"]=i
            cards=parser.collect({"app":args.app,"package":pkg,"pages":[page]})["pages"][0]
            keys={c["stable_key"] for c in cards}
            new=keys-prev_all
            overlap=keys&prev_all
            page["verified_candidate_cards"]=len(cards) if page["ui_dump_success"] else None
            page["new_candidate_cards"]=len(new) if page["ui_dump_success"] else None
            page["repeated_candidate_cards"]=len(overlap) if page["ui_dump_success"] else None
            page["cumulative_unique"]=len(prev_all|keys) if page["ui_dump_success"] else None
            record["pages"].append(page)
            prev_all|=keys
            if i>1 and not new and page["ui_dump_success"]:
                no_new_streak+=1
            else:
                no_new_streak=0

            print("PAGE="+str(i)+" UI_OK="+str(page["ui_dump_success"])+
                  " NODES="+str(page["node_count"])+
                  " CARDS="+str(len(cards))+
                  " NEW="+str(len(new))+
                  " SEEN="+str(len(prev_all))+
                  " SCREENSHOT="+str(bool(page.get("screenshot_file"))),
                  flush=True)
            # Persist checkpoint without dumping any user content to standard output.
            (folder/"summary.json").write_text(
                json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")
            if no_new_streak>=3:
                record["stop_reason"]="three_consecutive_pages_no_new_visible_cards"
                print("EARLY_STOP_NO_NEW_CARDS=True",flush=True)
                break
            if i==args.pages:break
            ensure_ready(args.adb,args.serial,pkg)
            base.run(args.adb,args.serial,"shell","input","swipe","510",
                     "1720","510","750","500")
            time.sleep(1.8)
        if "stop_reason" not in record:record["stop_reason"]="requested_pages_completed"
        result=parser.collect(record)
        (folder/"structured-cards.json").write_text(
            json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
        (folder/"summary.json").write_text(
            json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")
        ui_success=sum(bool(p["ui_dump_success"]) for p in record["pages"])
        stats={"app":args.app,"pages":len(record["pages"]),
               "ui_success_pages":ui_success,
               "total_card_appearances":sum(x["cards"] for x in result["two_page_card_counts"]) if ui_success else None,
               "unique_cards":result["unique_structured_cards"] if ui_success else None,
               "per_page":result["two_page_card_counts"],
               "stop_reason":record["stop_reason"],
               "private_folder":str(folder)}
        (folder/"statistics.json").write_text(
            json.dumps(stats,ensure_ascii=False,indent=2),encoding="utf-8")
        print("FINAL="+json.dumps({k:v for k,v in stats.items() if k not in ("private_folder","per_page")},ensure_ascii=False),flush=True)
        print("PRIVATE_RESULT="+str(folder),flush=True)
    except Exception as e:
        record["stop_reason"]="error"
        record["error_category"]=type(e).__name__
        (folder/"summary.json").write_text(
            json.dumps(record,ensure_ascii=False,indent=2),encoding="utf-8")
        print("ABORT_REASON="+str(e),flush=True)
        raise
    finally:
        # Do not interrupt if the user has moved to a different foreground app.
        if pkg in base.focus(args.adb,args.serial):
            base.invoke(args.adb,args.serial,"shell","input","keyevent","KEYCODE_HOME")

if __name__=="__main__":main()
