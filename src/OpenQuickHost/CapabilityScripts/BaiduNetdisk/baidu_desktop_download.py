"""Verified exact-name Baidu desktop search -> download -> client history.

Operates ONLY through the existing logged-in official desktop app.
No private API calls, credentials, token interception, or CLI network tooling.

To prevent fuzzy-search confusion, requires exactly one rendered full-filename row
and an unambiguously selected single file before clicking an accessible download
button. Final identity is the desktop client's completed server_path and optional
known source SHA-256, not simply that a file appears on disk.
"""
from __future__ import annotations

from pathlib import Path
from time import monotonic, sleep, time
import re
import shutil
import win32gui
import win32con
import uiautomation as u
from pynput.mouse import Controller, Button

from baidu_desktop_search import (
    BaiduSearchError, validate_filename, search_exact_visible,
    _find_unique_search_window, _semantic_result_snapshot,
)
from baidu_transfer_index import BaiduTransferIndex, sha256_file


class BaiduDownloadError(RuntimeError):
    pass


def _size_value(text: str) -> tuple[float, float]:
    m=re.fullmatch(r"(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB)",text.strip().upper())
    if not m:
        raise BaiduDownloadError("Unrecognized Baidu file size display")
    unit={"B":1,"KB":1024,"MB":1024**2,"GB":1024**3,"TB":1024**4}[m.group(2)]
    return float(m.group(1))*unit, max(.6*unit,1.0)


def _single_selected_and_button(hwnd: int, filename: str, expected_size: int | None):
    """Return one verified button plus UI size estimate.

    Supports either a textual download button or Baidu 8.8.3's compact
    toolbar: share button plus an icon-only download button. Icon-only
    controls MUST match the fixed verified arrow template as well as
    relative toolbar geometry; otherwise refuse without clicking.
    """
    heading,_,rows=_semantic_result_snapshot(hwnd,filename)
    unique=sorted(set(rows))
    if not heading or len(unique)!=1:
        raise BaiduDownloadError("Exact search result changed or became ambiguous")
    row=unique[0]
    root=u.ControlFromHandle(hwnd)
    q=[(root,0)]
    selected=[]
    labeled=[]
    shares=[]
    unnamed=[]
    row_sizes=[]
    bounds=win32gui.GetWindowRect(hwnd)
    while q:
        c,depth=q.pop(0)
        try:
            title=c.Name or ""
            role=c.ControlTypeName
            box=c.BoundingRectangle
            rect=(box.left,box.top,box.right,box.bottom)
            visible=(bounds[0]<=box.left<box.right<=bounds[2] and
                     bounds[1]<=box.top<box.bottom<=bounds[3])
            if role=="TextControl":
                if re.fullmatch(r"已选中\s*1\s*/\s*\d+\s*个",title):
                    selected.append(c)
                if row[1]<=box.top and box.bottom<=row[3]:
                    if re.fullmatch(r"\d+(?:\.\d+)?\s*(?:B|KB|MB|GB|TB)",title,re.I):
                        row_sizes.append(title)
            if role=="ButtonControl" and visible:
                if re.fullmatch(r"下载\([^)]+\)",title):
                    labeled.append((c,title,rect))
                elif title=="分享":
                    shares.append((c,rect))
                elif title=="":
                    unnamed.append((c,rect))
            if depth<15:
                q.extend((child,depth+1) for child in c.GetChildren()[:90])
        except Exception:
            continue
    if len(selected)!=1:
        raise BaiduDownloadError("Exactly one file must be selected before download")
    if len(row_sizes)!=1:
        raise BaiduDownloadError("Selected search row lacks one unambiguous file size")
    ui_bytes,ui_tolerance=_size_value(row_sizes[0])
    if expected_size is not None and abs(ui_bytes-expected_size)>ui_tolerance:
        raise BaiduDownloadError("Search result size differs from trusted source")

    if len(labeled)==1:
        chosen,label,rect=labeled[0]
        display=_size_value(re.search(r"下载\(([^)]+)\)",label).group(1))
        if abs(display[0]-ui_bytes)>max(display[1],ui_tolerance):
            raise BaiduDownloadError("Download button size conflicts with selected row")
        return chosen,rect,ui_bytes,ui_tolerance

    if labeled or len(shares)!=1:
        raise BaiduDownloadError("No unambiguous named or compact download control")
    share=shares[0][1]
    # In verified version the download arrow is the FIRST 32px unnamed button
    # immediately to the right of the share button on the SAME toolbar.
    possible=[(item,rect) for item,rect in unnamed
              if rect[0]==share[2] and rect[1]==share[1] and
              rect[3]==share[3] and 28<=rect[2]-rect[0]<=36]
    if len(possible)!=1:
        raise BaiduDownloadError("Compact download toolbar structure unknown")
    button,rect=possible[0]
    import cv2,numpy as np
    from PIL import ImageGrab
    resource=Path(__file__).with_name("baidu-download-icon.png")
    template=cv2.imread(str(resource),cv2.IMREAD_GRAYSCALE)
    if template is None or template.shape not in ((17,16),(16,17)):
        raise BaiduDownloadError("Verified download icon resource missing")
    # Screenshot only the candidate button; never infer a download command from
    # location alone. Visual confidence is version-specific and fail-closed.
    capture=ImageGrab.grab(bbox=rect).convert("L")
    pixels=np.asarray(capture)
    if pixels.shape[0]<template.shape[0] or pixels.shape[1]<template.shape[1]:
        raise BaiduDownloadError("Compact download button too small")
    correlation=cv2.matchTemplate(pixels,template,cv2.TM_CCOEFF_NORMED)
    _,confidence,_,_=cv2.minMaxLoc(correlation)
    if confidence<.94:
        raise BaiduDownloadError(f"Compact download icon confidence too low: {confidence:.3f}")
    return button,rect,ui_bytes,ui_tolerance


def download_exact(
    filename: str,
    *,
    expected_cloud_path: str | None = None,
    expected_size: int | None = None,
    expected_sha256: str | None = None,
    copy_to_folder: str | Path | None = None,
    timeout_seconds: int = 90,
) -> dict:
    """Real download; caller must obtain user confirmation beforehand.

    The official Baidu client chooses its default download directory. Optionally
    copies the verified result to an existing caller-specified directory without
    overwriting any target. The original Baidu download remains untouched.
    """
    name=validate_filename(filename)
    if (expected_cloud_path is None) != (expected_size is None):
        raise ValueError("Trusted path and size must be supplied together, or both omitted")
    if expected_cloud_path is not None:
        if not isinstance(expected_cloud_path,str) or not expected_cloud_path.startswith("/"):
            raise ValueError("Expected absolute trusted cloud path")
        if Path(expected_cloud_path.replace("/", "\\")).name!=name:
            raise ValueError("Trusted cloud path filename mismatch")
    if expected_size is not None and (
        not isinstance(expected_size,int) or expected_size<0 or expected_size>2*1024**3
    ):
        raise ValueError("Expected size must be 0..2GiB")
    if not isinstance(timeout_seconds,int) or not 15<=timeout_seconds<=300:
        raise ValueError("Timeout must be 15..300 seconds")
    if expected_sha256 is not None and not re.fullmatch("[a-fA-F0-9]{64}",expected_sha256):
        raise ValueError("Expected SHA-256 must be a 64-digit hex digest")
    target_dir=None
    if copy_to_folder is not None:
        target_dir=Path(copy_to_folder).resolve(strict=True)
        if not target_dir.is_dir():
            raise ValueError("Copy destination must be a directory")
        if (target_dir/name).exists():
            raise FileExistsError("Destination already has requested filename; no overwrite")
    index=BaiduTransferIndex()
    found=search_exact_visible(name,wait_seconds=min(15,timeout_seconds//2))
    if not (found.get("found") and found.get("visibleExactMatches")==1):
        raise BaiduSearchError("No unique exact filename on current cloud search results")
    hwnd=_find_unique_search_window()
    win32gui.ShowWindow(hwnd,win32con.SW_RESTORE)
    win32gui.SetWindowPos(
        hwnd,win32con.HWND_TOPMOST,0,0,0,0,
        win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_SHOWWINDOW)
    submitted_at=None
    try:
        heading,count,rows=_semantic_result_snapshot(hwnd,name)
        rows=sorted(set(rows))
        if not heading or len(rows)!=1:
            raise BaiduDownloadError("Search result changed since verification")
        a,b,c,d=rows[0]
        position=(a+min(25,(c-a)//2),(b+d)//2)
        if win32gui.GetAncestor(win32gui.WindowFromPoint(position),win32con.GA_ROOT)!=hwnd:
            raise BaiduDownloadError("Exact file row is covered by another application")
        mouse=Controller()
        mouse.position=position
        mouse.click(Button.left)
        sleep(.22)
        button,bounds,ui_bytes,ui_tolerance=_single_selected_and_button(hwnd,name,expected_size)
        cx=(bounds[0]+bounds[2])//2
        cy=(bounds[1]+bounds[3])//2
        if win32gui.GetAncestor(win32gui.WindowFromPoint((cx,cy)),win32con.GA_ROOT)!=hwnd:
            raise BaiduDownloadError("Download button is occluded")
        # Submit exactly one native client download. Never blindly retry.
        submitted_at=int(time())-2
        button.Click()
    finally:
        win32gui.SetWindowPos(
            hwnd,win32con.HWND_NOTOPMOST,0,0,0,0,
            win32con.SWP_NOMOVE|win32con.SWP_NOSIZE|win32con.SWP_NOACTIVATE)
    if submitted_at is None:
        raise BaiduDownloadError("Download was not submitted")

    deadline=monotonic()+timeout_seconds
    while monotonic()<deadline:
        if expected_cloud_path is not None:
            row=index.lookup_download_for_cloud_path(
                expected_cloud_path,after_seconds=submitted_at
            )
        else:
            row=index.lookup_recent_download_by_filename(
                name,after_seconds=submitted_at
            )
        if row:
            if row.error_code!=0 and row.finished_at>=row.started_at>0:
                return {
                    "status":"client_failed","downloaded":False,
                    "errorCode":row.error_code,"cloudPath":row.server_path,
                    "clientHistoryVerified":True,
                }
            if row.completed:
                target=Path(row.local_path)
                if not target.is_file() or target.stat().st_size!=row.file_size:
                    raise BaiduDownloadError("Completed client record but file size or path mismatches")
                if abs(row.file_size-ui_bytes)>ui_tolerance:
                    raise BaiduDownloadError("Client download differs from selected UI size")
                if expected_size is not None and row.file_size!=expected_size:
                    raise BaiduDownloadError("Client download differs from trusted size")
                if target.name!=name:
                    raise BaiduDownloadError("Baidu changed downloaded file name; ambiguous target")
                digest=sha256_file(target)
                if expected_sha256 and digest.lower()!=expected_sha256.lower():
                    raise BaiduDownloadError("SHA-256 differs from independently trusted digest")
                final=target
                if target_dir is not None:
                    final=target_dir/name
                    if final.exists():
                        raise FileExistsError("Destination appeared during download; refusing overwrite")
                    # Copy via exclusive-create to avoid accidental overwrites
                    with target.open("rb") as src, final.open("xb") as dst:
                        shutil.copyfileobj(src,dst,1024*1024)
                    if sha256_file(final)!=digest:
                        final.unlink(missing_ok=True)
                        raise BaiduDownloadError("Copy after download hash differs")
                return {
                    "status":"client_completed","downloaded":True,
                    "clientHistoryVerified":True,
                    "cloudPath":row.server_path,
                    "filename":name,
                    "size":row.file_size,
                    "sha256":digest,
                    "nameOnlySelection":expected_cloud_path is None,
                    "trustedPathValidated":expected_cloud_path is not None,
                    "referenceSha256Verified":bool(expected_sha256),
                    "downloadPath":str(target),
                    "outputPath":str(final),
                    "source":"baidu-official-desktop-client",
                    "liveCloudFidVerified":False,
                    "globallyUniqueFilenameVerified":False,
                }
        sleep(.65)
    return {
        "status":"pending_unconfirmed","downloaded":False,
        "cloudPath":expected_cloud_path,
        "message":"Baidu download may be pending. Query transfer history; do not resubmit blindly.",
    }
