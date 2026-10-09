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


def _single_selected_and_button(hwnd: int, filename: str, expected_size: int | None):
    """Validate exact selected count and only one in-app download action."""
    found_heading,_,rows=_semantic_result_snapshot(hwnd,filename)
    if not found_heading or len(set(rows))!=1:
        raise BaiduDownloadError("Exact result changed or became ambiguous")
    root=u.ControlFromHandle(hwnd)
    queue=[(root,0)]
    selected=[]
    download_buttons=[]
    while queue:
        item,depth=queue.pop(0)
        try:
            title=item.Name or ""
            if item.ControlTypeName=="TextControl" and re.fullmatch(
                    r"已选中\s*1\s*/\s*\d+\s*个",title):
                selected.append(item)
            if item.ControlTypeName=="ButtonControl" and re.fullmatch(
                    r"下载\(([^)]+)\)",title):
                box=item.BoundingRectangle
                left,top,right,bottom=win32gui.GetWindowRect(hwnd)
                if left <= box.left < box.right <= right and top <= box.top < box.bottom <= bottom:
                    download_buttons.append((item,title,(box.left,box.top,box.right,box.bottom)))
            if depth<15:
                queue.extend((child,depth+1) for child in item.GetChildren()[:90])
        except Exception:
            continue
    if len(selected)!=1 or len(download_buttons)!=1:
        raise BaiduDownloadError(
            f"Expected one selected file and one download button, got {len(selected)}/{len(download_buttons)}"
        )
    item,label,bounds=download_buttons[0]
    if expected_size is not None:
        match=re.search(r"下载\(([^)]+)\)",label)
        amount=match.group(1).strip().upper() if match else ""
        m=re.fullmatch(r"(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB)",amount)
        if not m:
            raise BaiduDownloadError("Unknown Baidu download button size format")
        scale={"B":1,"KB":1024,"MB":1024**2,"GB":1024**3,"TB":1024**4}[m.group(2)]
        measured=float(m.group(1))*scale
        # UI rounds to one decimal, so validate within display precision.
        tolerance=max(0.6*scale,1.0)
        if abs(measured-expected_size)>tolerance:
            raise BaiduDownloadError("Selected Baidu file size differs from expected source")
    return item,bounds


def download_exact(
    filename: str,
    *,
    expected_cloud_path: str,
    expected_size: int,
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
    if not isinstance(expected_cloud_path,str) or not expected_cloud_path.startswith("/"):
        raise ValueError("A trusted absolute cloud path is required")
    if Path(expected_cloud_path.replace("/", "\\")).name!=name:
        raise ValueError("Trusted cloud path does not match requested filename")
    if not isinstance(expected_size,int) or expected_size<0 or expected_size>2*1024**3:
        raise ValueError("Expected byte size must be between 0 and 2 GiB")
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
        button,bounds=_single_selected_and_button(hwnd,name,expected_size)
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
        row=index.lookup_download_for_cloud_path(
            expected_cloud_path,after_seconds=submitted_at
        )
        if row:
            if row.error_code!=0 and row.finished_at>=row.started_at>0:
                return {
                    "status":"client_failed","downloaded":False,
                    "errorCode":row.error_code,"cloudPath":expected_cloud_path,
                    "clientHistoryVerified":True,
                }
            if row.completed:
                target=Path(row.local_path)
                if not target.is_file() or target.stat().st_size!=expected_size:
                    raise BaiduDownloadError("Completed client record but file size or path mismatches")
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
                    "size":expected_size,
                    "sha256":digest,
                    "referenceSha256Verified":bool(expected_sha256),
                    "downloadPath":str(target),
                    "outputPath":str(final),
                    "source":"baidu-official-desktop-client",
                    "liveCloudFidVerified":False,
                }
        sleep(.65)
    return {
        "status":"pending_unconfirmed","downloaded":False,
        "cloudPath":expected_cloud_path,
        "message":"Baidu download may be pending. Query transfer history; do not resubmit blindly.",
    }
