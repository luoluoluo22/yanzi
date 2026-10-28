#!/usr/bin/env python3
"""Read-only surface audit of selected Android apps. No private content persisted.

Only app inventory and visible UI structure counts. Not a backup, API client,
packet sniffer or accessibility bypass. Requires already-authorized ADB.
"""
import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

SAFE_PROBE = {
    "calculator": "com.miui.calculator",
    "bilibili": "tv.danmaku.bili",
    "xiaohongshu": "com.xingin.xhs",
    "douyin": "com.ss.android.ugc.aweme",
    "netease-music": "com.netease.cloudmusic",
    "pcapdroid": "com.emanuelef.remote_capture",
    "yanzi": "cc.luoluoluo.yanzi.mobile",
}
GENERIC_UI = ("首页","推荐","我的","搜索","消息","动态","视频","关注","热门",
              "视频号","发现","设置","权限","同意","允许","取消","登录","跳过",
              "立即体验","购物","商城","直播","歌单","排行榜","音乐","播放",
              "列表","全部","分类","历史","文件","电台","计算器","清除",
              "个人中心","播放列表","网络","开始","停止","就绪","状态","连接",
              "Home","Search","Settings","Login","Cancel","Allow","Play")
def call(adb, serial, *args, timeout=15):
    result = subprocess.run([adb,"-s",serial,*args],
                            capture_output=True,encoding="utf-8",
                            errors="replace", timeout=timeout)
    return result.returncode, result.stdout, result.stderr

def focused(adb, serial):
    _, text, _ = call(adb,serial,"shell","dumpsys","window")
    for line in text.splitlines():
        if "mCurrentFocus=" in line:
            return line.strip().split("mCurrentFocus=",1)[-1].strip()
    return ""

def package_info(adb,serial,pkg):
    _, text, _ = call(adb,serial,"shell","dumpsys","package",pkg)
    version = re.search(r"\bversionName=([^\s]+)",text)
    code = re.search(r"\bversionCode=(\d+)",text)
    return {"package":pkg, "version_name":version.group(1) if version else None,
            "version_code":int(code.group(1)) if code else None}

def ui_snapshot(adb,serial):
    # Local shell accessibility snapshot of the *currently visible* window only.
    call(adb,serial,"shell","rm","-f","/sdcard/yanzi_app_data_probe.xml")
    ec, txt, _=call(adb,serial,"shell","uiautomator","dump",
                    "/sdcard/yanzi_app_data_probe.xml",timeout=23)
    if ec or "dumped to" not in txt.lower():
        return {"ui_dump_success":False,"error":"uiautomator_dump_unavailable"}
    ec, xml, _=call(adb,serial,"exec-out","cat","/sdcard/yanzi_app_data_probe.xml",timeout=12)
    # Accessibility XML may contain personal text. Never leave it on shared phone storage.
    call(adb,serial,"shell","rm","-f","/sdcard/yanzi_app_data_probe.xml")
    if ec:
        return {"ui_dump_success":False,"error":"uiautomator_read_unavailable"}
    try: root=ET.fromstring(xml.strip())
    except ET.ParseError:
        return {"ui_dump_success":False,"error":"invalid_accessibility_xml"}
    nodes=[n.attrib for n in root.iter("node")]
    counts=Counter()
    classes=Counter()
    keywords=set()
    for a in nodes:
        if a.get("clickable")=="true":counts["clickable"]+=1
        if a.get("scrollable")=="true":counts["scrollable"]+=1
        if a.get("password")=="true":counts["password"]+=1
        if a.get("resource-id"):counts["resource_id"]+=1
        if a.get("text"):counts["text"]+=1
        if a.get("content-desc"):counts["content_description"]+=1
        if "EditText" in a.get("class",""):counts["edit_text"]+=1
        if "WebView" in a.get("class",""):counts["webview"]+=1
        classes[a.get("class","").rsplit(".",1)[-1]]+=1
        for field in ("text","content-desc"):
            val=a.get(field,"")
            # Only report fixed UI words, not user/private strings.
            if val in GENERIC_UI:keywords.add(val)
    return {
        "ui_dump_success":True,
        "root_package":next((a.get("package") for a in nodes if a.get("package")),None),
        "node_count":len(nodes),"visible_text_nodes":counts["text"],
        "description_nodes":counts["content_description"],
        "resource_id_nodes":counts["resource_id"],
        "clickable_nodes":counts["clickable"],"scrollable_nodes":counts["scrollable"],
        "editable_nodes":counts["edit_text"],"password_nodes":counts["password"],
        "webview_nodes":counts["webview"],
        "generic_labels_only":sorted(keywords),
        "top_node_classes":dict(classes.most_common(6)),
        "no_raw_text_or_screenshots_saved":True,
    }

def probe(adb,serial,name,pkg):
    row=package_info(adb,serial,pkg)
    row["app"]=name
    # Only query whether sandbox is accessible; never read inside.
    ec,_,_=call(adb,serial,"shell","run-as",pkg,"pwd",timeout=9)
    row["app_sandbox_accessible_to_adb"]=ec==0
    ec,output,_=call(adb,serial,"shell","monkey","-p",pkg,
                     "-c","android.intent.category.LAUNCHER","1",timeout=15)
    row["launch_intent_dispatched"]=ec==0
    time.sleep(2.0)
    focus=focused(adb,serial)
    # Avoid recording activity titles or any content from non-target apps.
    row["target_foreground"]=pkg in focus
    if not row["target_foreground"]:
        row["ui"]={"ui_dump_success":False,
                   "error":"app_not_foreground_or_redirected"}
    else:
        row["ui"]=ui_snapshot(adb,serial)
    row["observed_utc"]=datetime.now(timezone.utc).isoformat(timespec="seconds")
    print(f"PROBED {name} foreground={row['target_foreground']} ui_nodes={row['ui'].get('node_count',0)}"
          f" text_nodes={row['ui'].get('visible_text_nodes',0)}"
          f" root={row['ui'].get('root_package','?')}",flush=True)
    return row

def main():
    p=argparse.ArgumentParser()
    p.add_argument("--adb",default="adb")
    p.add_argument("--serial",required=True)
    p.add_argument("--apps",nargs="+",choices=SAFE_PROBE,default=["calculator","bilibili","xiaohongshu","douyin","netease-music","pcapdroid","yanzi"])
    p.add_argument("--output-root",type=Path,default=Path(os.environ.get("LOCALAPPDATA","."))/
                   "OpenQuickHost"/"NetworkCaptures"/"K70")
    args=p.parse_args()
    args.output_root.mkdir(parents=True,exist_ok=True)
    ts=datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    folder=args.output_root/("app-surface-audit-"+ts)
    folder.mkdir(parents=True,exist_ok=False)
    result={"schema_version":1,"purpose":"Read-only visible UI/metadata feasibility audit",
            "apps_requested":args.apps,"observations":[],"no_raw_ui_text_saved":True,
            "note":"Visible user-owned apps only; no chat/mail/banking apps opened."}
    for name in args.apps:
        pkg=SAFE_PROBE[name]
        result["observations"].append(probe(args.adb,args.serial,name,pkg))
    # Restore the home screen rather than leave an inspected app open.
    call(args.adb,args.serial,"shell","input","keyevent","KEYCODE_HOME")
    dest=folder/"summary.json"
    dest.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding="utf-8")
    print("LOCAL_SUMMARY="+str(dest))
if __name__=="__main__":
    main()
