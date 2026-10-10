"""Safe Quark DESKTOP cross-folder exact-name search and verified download.

Only the user's previously uploaded file is eligible. The target Quark window
must be visible, and the in-app 'search cloud drive' route must resolve an
exactly matching file name in the current result page. Cloud identity is
confirmed AFTER download with the native transfer DB fid and local SHA-256.

Quark Windows 7.3.5.1009 only. No cookie/token export or private HTTP requests.
"""
from __future__ import annotations

from pathlib import Path
from time import monotonic, sleep, time
import win32gui
import uiautomation as u

from quark_drive_adapter import (
    QuarkDesktopAdapter, QuarkSelectionRequired, QuarkIntegrityError,
    sha256_file,
)
from quark_recent_file_cache import QuarkRecentFileCache
from quark_transfer_index import QuarkTransferIndex
from quark_semantic_selection import QuarkVisibleFileSelector
from quark_unicode_input import send_unicode


class QuarkSearchError(RuntimeError):
    pass


def _row_for_exact_result(pattern, hwnd: int, filename: str):
    left,top,right,bottom=win32gui.GetWindowRect(hwnd)
    x=left+200
    rects={}
    for y in range(top+310,min(bottom-65,top+970),17):
        ran=pattern.RangeFromPoint(x,y)
        if ran is None:continue
        ran.ExpandToEnclosingUnit(u.TextUnit.Line,waitTime=0)
        if ran.GetText(200).strip()!=filename:continue
        valid=[r for r in ran.GetBoundingRectangles()
               if r.right-r.left>20 and r.bottom-r.top>10]
        if valid:
            rects[min(v.top for v in valid)]=valid[0]
    if not rects:
        raise QuarkSelectionRequired("Exact cloud filename not visible in search results")
    if max(rects)-min(rects)>24:
        raise QuarkSelectionRequired("Multiple separate results have the same filename")
    row=rects[min(rects)]
    row_y=(row.top+row.bottom)//2-top
    if row_y<310 or row_y>955:
        raise QuarkSelectionRequired("Cloud search result row outside supported viewport")
    return row_y


def _button_for_exact_row(hwnd: int, row_y: int) -> tuple[int,int]:
    import cv2
    import numpy as np
    from PIL import ImageGrab
    template_path=Path(__file__).with_name("quark-global-search-download-control.png")
    image=cv2.imread(str(template_path),cv2.IMREAD_GRAYSCALE)
    if image is None:
        raise QuarkSearchError("Bundled search download icon template is missing")
    pixels=np.asarray(ImageGrab.grab(bbox=win32gui.GetWindowRect(hwnd)).convert("RGB"))
    gray=cv2.cvtColor(pixels,cv2.COLOR_RGB2GRAY)
    h,w=gray.shape
    if abs(w-1280)>20 or abs(h-1078)>20:
        raise QuarkSearchError("Quark search layout is unvalidated at this window size")
    y1=max(0,row_y-23)
    y2=min(h,row_y+23)
    roi=gray[y1:y2,660:755]
    if roi.shape[0]<image.shape[0] or roi.shape[1]<image.shape[1]:
        raise QuarkSearchError("Download icon is outside validated row bounds")
    match=cv2.matchTemplate(roi,image,cv2.TM_CCOEFF_NORMED)
    _,score,_,point=cv2.minMaxLoc(match)
    x=660+point[0]+image.shape[1]//2
    y=y1+point[1]+image.shape[0]//2
    if score<0.93 or not (698<=x<=735) or abs(y-row_y)>12:
        raise QuarkSearchError(
            f"Search row download icon failed visual validation: {score:.3f}"
        )
    return x,y


def download_verified_by_cloud_search(
    original_local_file: str | Path,
    target_folder: str | Path,
    *,
    timeout: float = 90.0,
):
    from pynput.keyboard import Controller,Key
    source=Path(original_local_file).resolve(strict=True)
    folder=Path(target_folder).resolve(strict=True)
    if not source.is_file() or not folder.is_dir():
        raise ValueError("Source must be a file and destination an existing directory")
    if (folder/source.name).exists():
        raise FileExistsError("Refusing to overwrite existing downloaded file")
    if len(source.name)>180 or len(source.name)<12:
        raise ValueError("Experimental cloud search supports filenames of length 12..180")
    if timeout<10 or timeout>240:
        raise ValueError("Timeout must be between 10 and 240 seconds")

    index=QuarkTransferIndex()
    uploaded=index.resolve_completed_upload(source)
    if not uploaded or not uploaded.completed:
        raise QuarkSelectionRequired(
            "A verified local upload record for this exact file is required"
        )
    # Root folder uploads use Quark's special dirFid='0'. IndexedDB only
    # caches some recent files, so a missing cache entry cannot invalidate a
    # trusted FINISH upload; a conflicting cached ID still fails closed.
    from re import fullmatch
    scope=(uploaded.dir_fid if fullmatch(r"[a-fA-F0-9]{32}",uploaded.dir_fid) else None)
    candidate=QuarkRecentFileCache().lookup_candidate(source.name,parent_fid=scope)
    if candidate and candidate.fid!=uploaded.fid:
        raise QuarkSelectionRequired("Conflicting cached fid for trusted uploaded file")
    digest=sha256_file(source)
    adapter=QuarkDesktopAdapter()
    window=adapter.prepare_window()
    started_ms=0
    try:
        if win32gui.GetForegroundWindow()!=window.hwnd:
            try:win32gui.SetForegroundWindow(window.hwnd)
            except Exception:pass
        sleep(.10)
        if win32gui.GetForegroundWindow()!=window.hwnd:
            raise QuarkSearchError("Quark is not in the foreground; refusing input")
        keys=Controller()
        with keys.pressed(Key.ctrl):
            keys.press("f");keys.release("f")
        sleep(.14)
        adapter._click_relative((345,62))
        with keys.pressed(Key.ctrl):
            keys.press("a");keys.release("a")
        send_unicode(source.name)
        sleep(.16)
        adapter._click_relative((365,116))  # Quark cloud, NOT web
        sleep(.32)
        adapter._click_relative((365,166))  # in-app search
        sleep(.5)

        deadline=monotonic()+min(timeout*.45,16.0)
        pattern=None
        row_y=None
        while monotonic()<deadline:
            try:
                pattern=QuarkVisibleFileSelector._document_text_pattern(window.hwnd)
                text=pattern.DocumentRange.GetText(-1)
                if "相关搜索结果" in text and source.name in text:
                    row_y=_row_for_exact_result(pattern,window.hwnd,source.name)
                    break
            except QuarkSelectionRequired:
                pass
            sleep(.45)
        if row_y is None:
            raise QuarkSearchError("Exact filename not found in loaded cloud search results")

        # Show the inline actions for this verified result row.
        adapter._click_relative((240,row_y))
        sleep(.20)
        action=_button_for_exact_row(window.hwnd,row_y)
        started_ms=int(time()*1000)-2500
        adapter._click_relative(action)
        picker=adapter._wait_dialog("1152",5.0)
        if picker is None:
            raise QuarkSearchError("Quark-owned download folder picker did not appear")
        adapter._complete_native_dialog(picker,str(folder))
    finally:
        adapter.release_window()

    target=folder/source.name
    deadline=monotonic()+timeout
    while monotonic()<deadline:
        if target.is_file() and sha256_file(target)==digest:
            downloaded=index.resolve_completed_download(
                target,expected_fid=uploaded.fid,after_ms=started_ms
            )
            if downloaded and downloaded.completed:
                return {
                    "submitted": True,
                    "remoteConfirmed": True,
                    "filename": source.name,
                    "fid": downloaded.fid,
                    "targetPath": str(target),
                    "size": target.stat().st_size,
                    "sha256": digest,
                    "searchScope": "all-cloud-files",
                    "identityProof": "upload-fid + download-fid + local-sha256",
                }
        sleep(.55)
    raise QuarkIntegrityError(
        "Quark search download did not achieve FINISH/fid/hash within timeout"
    )
