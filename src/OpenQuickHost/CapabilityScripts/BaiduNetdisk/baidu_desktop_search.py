"""Live filename search in the signed-in official Baidu Netdisk Windows UI.

This is a desktop-control adapter, NOT an unofficial Baidu cloud HTTP client.
No cookies, tokens, clipboard content, passwords or private APIs are accessed.
Only files visible on the currently rendered search result page are inspected.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import time
import re

import psutil
import uiautomation as u
import win32gui
import win32con
import win32process
from pynput.keyboard import Controller, Key

from baidu_unicode_input import send_unicode


class BaiduSearchError(RuntimeError):
    pass


@dataclass(frozen=True)
class ExactSearchHit:
    file_name: str
    visible_match_count: int
    advertised_result_count: int | None
    source: str = "baidu-desktop-live-search"
    exact_name_on_visible_page: bool = True
    unique_across_entire_cloud: bool = False
    fid_verified: bool = False


def validate_filename(file_name: str) -> str:
    if not isinstance(file_name, str) or not 3 <= len(file_name) <= 180:
        raise ValueError("An exact 3..180 character filename is required")
    if (Path(file_name).name != file_name or
        any(x in file_name for x in ('/', '\\', '\r', '\n', '\0'))):
        raise ValueError("Filename cannot be a path or contain control characters")
    if any(ord(c)>0xFFFF for c in file_name):
        raise ValueError("This Windows Unicode-input adapter supports BMP characters only")
    return file_name


def _find_unique_search_window() -> int:
    # Chromium's accessibility tree can be almost empty while the desktop
    # window is in the background. Discover by trusted EXE and HWND first,
    # then restore/focus candidates temporarily to inspect the search field.
    windows=[]

    def callback(hwnd: int, _: object):
        if not win32gui.IsWindow(hwnd) or win32gui.GetWindowText(hwnd) != "百度网盘":
            return
        pid=win32process.GetWindowThreadProcessId(hwnd)[1]
        try:
            if psutil.Process(pid).name().lower()=="baidunetdiskunite.exe":
                windows.append(hwnd)
        except psutil.Error:
            pass

    win32gui.EnumWindows(callback,None)
    if not windows:
        raise BaiduSearchError("No official Baidu desktop windows found")
    matches=[]
    previous=win32gui.GetForegroundWindow()
    for hwnd in windows:
        win32gui.ShowWindow(hwnd,win32con.SW_RESTORE)
        win32gui.SetWindowPos(
            hwnd,win32con.HWND_TOPMOST,0,0,0,0,
            win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_SHOWWINDOW)
        try: win32gui.SetForegroundWindow(hwnd)
        except Exception: pass
        time.sleep(.35)
        try:
            parent=u.ControlFromHandle(hwnd)
            field=parent.EditControl(AutomationId="tags-input-ipt",searchDepth=12)
            if field.Exists(0.3):
                matches.append(hwnd)
        except Exception:
            pass
        finally:
            win32gui.SetWindowPos(
                hwnd,win32con.HWND_NOTOPMOST,0,0,0,0,
                win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_NOACTIVATE)
    if win32gui.IsWindow(previous) and previous not in matches:
        try: win32gui.SetForegroundWindow(previous)
        except Exception: pass
    if len(matches)!=1:
        raise BaiduSearchError(
            f"Expected one Baidu cloud manager with search input; found {len(matches)}"
        )
    return matches[0]


def _semantic_result_snapshot(hwnd: int, filename: str) -> tuple[bool, int | None, list[tuple[int,int,int,int]]]:
    """Find a fresh search heading plus complete-name row groups.

    The same filename may occur in the search input/header/text elsewhere. Only
    matching GroupControl file rows are treated as candidates.
    """
    q=[(u.ControlFromHandle(hwnd),0)]
    searched=False
    count=None
    rows=[]
    header_re=re.compile(r'搜索结果，共(\d+)项')
    bound=win32gui.GetWindowRect(hwnd)
    n=0
    while q and n<2400:
        c,depth=q.pop(0)
        n+=1
        try:
            name=c.Name or ""
            role=c.ControlTypeName
            if role=="TextControl" and filename in name and "搜索结果" in name:
                # Exact phrase in the search heading prevents stale-page results.
                if f'“{filename}”' in name:
                    searched=True
                    match=header_re.search(name)
                    if match:count=int(match.group(1))
            if role=="GroupControl" and name==filename:
                r=c.BoundingRectangle
                if (r.right>r.left and r.bottom>r.top and
                    bound[0]<=r.left<r.right<=bound[2] and
                    bound[1]+100<=r.top<r.bottom<=bound[3]):
                    rows.append((r.left,r.top,r.right,r.bottom))
            if depth<17:
                q.extend((child,depth+1) for child in c.GetChildren()[:100])
        except Exception:
            continue
    return searched,count,rows


def search_exact_visible(file_name: str, *, wait_seconds: float = 12) -> dict:
    """Search the whole Baidu cloud UI, returning only exact visible matches.

    This does not prove a globally unique cloud fid. The current app may show
    fuzzy results, pagination, paid-content ads, and results on other pages.
    It never selects, downloads, deletes, moves or uploads any cloud file.
    """
    name=validate_filename(file_name)
    if not isinstance(wait_seconds, (int,float)) or not 2 <= wait_seconds <= 30:
        raise ValueError("wait_seconds must be 2..30")
    hwnd=_find_unique_search_window()
    previous=win32gui.GetForegroundWindow()
    win32gui.ShowWindow(hwnd,win32con.SW_RESTORE)
    win32gui.SetWindowPos(
        hwnd,win32con.HWND_TOPMOST,0,0,0,0,
        win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_SHOWWINDOW)
    try:
        doc=u.ControlFromHandle(hwnd)
        field=doc.EditControl(AutomationId="tags-input-ipt",searchDepth=12)
        if not field.Exists(0):
            raise BaiduSearchError("Baidu search input disappeared")
        r=field.BoundingRectangle
        center=((r.left+r.right)//2,(r.top+r.bottom)//2)
        if win32gui.GetAncestor(win32gui.WindowFromPoint(center),win32con.GA_ROOT)!=hwnd:
            raise BaiduSearchError("Search input is covered by another window")
        field.Click()
        if win32gui.GetForegroundWindow()!=hwnd:
            raise BaiduSearchError("Baidu did not obtain input focus")
        keyboard=Controller()
        with keyboard.pressed(Key.ctrl):
            keyboard.press("a"); keyboard.release("a")
        sent=send_unicode(name)
        if sent!=len(name)*2:
            raise BaiduSearchError("Incomplete Unicode search input")
        # IMPORTANT: typing into an existing search page changes the title while
        # retaining results from the previous query. Press Enter to submit.
        keyboard.press(Key.enter)
        keyboard.release(Key.enter)
        time.sleep(.25)
        # ValuePattern stays blank in this Chromium renderer, so only trust
        # a committed result page after the old results have been invalidated.
        end=time.monotonic()+wait_seconds
        heading=False
        rows=[]
        advertised=None
        while time.monotonic()<end:
            heading,advertised,rows=_semantic_result_snapshot(hwnd,name)
            if heading:
                # Query accepted; give in-app async file list a short moment.
                if rows or advertised==0:
                    break
            time.sleep(.4)
        if not heading:
            raise BaiduSearchError("No live Baidu search heading for exact query")
        unique=sorted(set(rows))
        if len(unique)>1:
            raise BaiduSearchError("Ambiguous: multiple visible result rows share exact filename")
        return {
            "searched":True,
            "fileName":name,
            "found":len(unique)==1,
            "visibleExactMatches":len(unique),
            "advertisedFuzzyResultCount":advertised,
            "searchScope":"all-cloud-files",
            "currentPageOnly":True,
            "notFoundGloballyProven":False,
            "source":"baidu-desktop-live-search",
            "livePageVerified":True,
            "uniqueAcrossEntireCloud":False,
            "cloudFidVerified":False,
            "downloaded":False,
        }
    finally:
        win32gui.SetWindowPos(
            hwnd,win32con.HWND_NOTOPMOST,0,0,0,0,
            win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_NOACTIVATE)
        # Avoid moving focus to an unrelated window if it was closed meanwhile.
        if previous!=hwnd and win32gui.IsWindow(previous):
            try: win32gui.SetForegroundWindow(previous)
            except Exception: pass
