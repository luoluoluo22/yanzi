#!/usr/bin/env python3
"""Isolated Chrome extension smoke test. No access to personal browser profiles.

Requirements: Python 3.11+, websocket-client (pip install websocket-client).
Usage: python scripts/browser-helper-smoke-chromium.py [--browser chrome|edge]
"""
import argparse
import json
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path
from websocket import create_connection

ROOT=Path(__file__).resolve().parent.parent
SOURCE=ROOT/"browser-extension"
EXES={
    "chromium":Path(__import__("os").environ["LOCALAPPDATA"])/"ms-playwright"/"chromium-1169"/"chrome-win"/"chrome.exe",
    "chrome":Path(r"C:\Program Files\Google\Chrome\Application\chrome.exe"),
    "edge":Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
}

def send(ws,method,params=None,session=None,index=1):
    data={"id":index,"method":method,"params":params or {}}
    if session: data["sessionId"]=session
    ws.send(json.dumps(data))
    deadline=time.monotonic()+7
    while time.monotonic()<deadline:
        reply=json.loads(ws.recv())
        if reply.get("id")==index: return reply
    raise RuntimeError("CDP_TIMEOUT "+method)

def main():
    parser=argparse.ArgumentParser()
    parser.add_argument("--browser",choices=EXES,default="chromium")
    args=parser.parse_args()
    if args.browser in ("chrome", "edge"):
        raise SystemExit(
            "BRANDED_CHROME_REQUIRES_USER_LOAD_UNPACKED: Chrome 137+ blocks "
            "--load-extension for official builds. Use the Chromium test binary "
            "or test through user-consented chrome://extensions."
        )
    exe=EXES[args.browser]
    if not exe.is_file():
        raise SystemExit(f"NOT_INSTALLED: {exe}")
    expected=json.loads((SOURCE/"manifest.json").read_text(encoding="utf-8"))["version"]
    with tempfile.TemporaryDirectory(prefix="yanzi-browser-isolated-") as profile:
        process=subprocess.Popen([
            str(exe),"--headless=new","--no-first-run","--no-default-browser-check",
            "--disable-background-networking","--remote-allow-origins=*",
            "--remote-debugging-port=0",f"--user-data-dir={profile}",
            f"--disable-extensions-except={SOURCE}",f"--load-extension={SOURCE}",
            "about:blank",
        ],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
        try:
            active=Path(profile)/"DevToolsActivePort"
            for i in range(80):
                if active.exists() or process.poll() is not None: break
                time.sleep(.2)
            if not active.exists(): raise RuntimeError("BROWSER_HEADLESS_FAILED_TO_START")
            port=int(active.read_text().splitlines()[0])
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/json/version",timeout=5) as res:
                info=json.load(res)
            ws=create_connection(info["webSocketDebuggerUrl"],timeout=7,suppress_origin=True)
            try:
                extension=None
                for attempt in range(25):
                    targets=send(ws,"Target.getTargets",index=attempt+1).get("result",{}).get("targetInfos",[])
                    extension=next((t for t in targets if t.get("url","").startswith("chrome-extension://")),None)
                    if extension: break
                    time.sleep(.2)
                if not extension: raise RuntimeError("EXTENSION_NOT_LOADED")
                extension_id=extension["url"].split("/")[2]
                url=f"chrome-extension://{extension_id}/popup/popup.html"
                created=send(ws,"Target.createTarget",{"url":url},index=60)
                target_id=created.get("result",{}).get("targetId")
                if not target_id: raise RuntimeError("POPUP_NOT_CREATED")
                attached=send(ws,"Target.attachToTarget",
                              {"targetId":target_id,"flatten":True},index=61)
                session=attached.get("result",{}).get("sessionId")
                if not session: raise RuntimeError("POPUP_ATTACH_FAILED")
                send(ws,"Runtime.enable",session=session,index=62)
                version=None
                for attempt in range(12):
                    result=send(ws,"Runtime.evaluate",
                        {"expression":"chrome.runtime.getManifest().version","returnByValue":True},
                        session=session,index=63+attempt)
                    version=result.get("result",{}).get("result",{}).get("value")
                    if version: break
                    time.sleep(.2)
                if version != expected:
                    diagnostic=send(ws,"Runtime.evaluate",
                        {"expression":"JSON.stringify({url:location.href,title:document.title,chrome:typeof chrome,hasRuntime:typeof chrome==='object' && !!chrome.runtime})",
                         "returnByValue":True},session=session,index=90)
                    print("CDP_DIAGNOSTIC",json.dumps({"versionResult":result,"context":diagnostic})[:1300])
                    raise RuntimeError(f"MANIFEST_VERSION_MISMATCH expected={expected} actual={version}")
                print(f"PASS {args.browser.upper()} EXTENSION_ID={extension_id} POPUP_VERSION={version}")
                send(ws,"Target.closeTarget",{"targetId":target_id},index=99)
            finally:ws.close()
        finally:
            process.terminate()
            try:process.wait(timeout=7)
            except subprocess.TimeoutExpired:process.kill()

if __name__=="__main__":
    main()
