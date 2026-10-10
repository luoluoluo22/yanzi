"""Verified on-screen file selection in Quark 7.3.5.1009 using Windows UIA.

No OCR, no private cloud RPC and no browser cookies. We read the current
Quark window's accessibility text, locate a unique text row by an exact
distinguishing prefix, CLICK ONLY INSIDE QUARK, and verify the complete
filename appears in the details pane after selection.

Only currently visible items can be selected. This does not establish live
cloud file identity on its own; use with known upload fid / SHA-256 proof.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
import time
import uiautomation as u
import win32gui

from quark_drive_adapter import QuarkDesktopAdapter, QuarkSelectionRequired


@dataclass(frozen=True)
class VisibleFileSelection:
    filename: str
    details_verified: bool
    row_top: int
    prefix: str


class QuarkVisibleFileSelector:
    def __init__(self, adapter: QuarkDesktopAdapter | None = None):
        self.adapter = adapter or QuarkDesktopAdapter()

    @staticmethod
    def _document_text_pattern(hwnd: int):
        rect=win32gui.GetWindowRect(hwnd)
        width,height=rect[2]-rect[0],rect[3]-rect[1]
        queue=[(u.ControlFromHandle(hwnd),0)]
        matches=[]
        while queue:
            control,depth=queue.pop(0)
            try:
                if control.ControlTypeName=="DocumentControl":
                    bounds=control.BoundingRectangle
                    if (bounds.right-bounds.left)>width*.75 and (bounds.bottom-bounds.top)>height*.65:
                        pattern=control.GetTextPattern()
                        if pattern and len(pattern.DocumentRange.GetText(5000))>=35:
                            matches.append(pattern)
                if depth<15:
                    queue.extend((child,depth+1) for child in control.GetChildren()[:80])
            except Exception:
                continue
        if len(matches)!=1:
            raise QuarkSelectionRequired(
                f"Expected exactly one accessible Quark file document; found {len(matches)}"
            )
        return matches[0]

    def select_visible_file(self, filename: str) -> VisibleFileSelection:
        name=Path(filename).name
        if not name or name!=filename or "/" in filename or "\\" in filename:
            raise ValueError("Expected a single cloud filename")
        if len(name)<18:
            raise QuarkSelectionRequired(
                "Filename too short for safe uniqueness check without direct fid mapping"
            )
        prefix=name[:min(18,len(name))]
        window=self.adapter.prepare_window()
        try:
            x1,y1,x2,y2=win32gui.GetWindowRect(window.hwnd)
            width,height=x2-x1,y2-y1
            pattern=self._document_text_pattern(window.hwnd)
            x=x1+round(width*.355)
            candidates={}
            for y in range(y1+180,min(y2-75,y1+875),22):
                ran=pattern.RangeFromPoint(x,y)
                if not ran:continue
                if not ran.ExpandToEnclosingUnit(u.TextUnit.Line,waitTime=0):continue
                label=ran.GetText(180)
                if not label.startswith(prefix):continue
                bounds=[r for r in ran.GetBoundingRectangles()
                        if (r.right-r.left)>12 and (r.bottom-r.top)>9]
                if not bounds:continue
                top=min(r.top for r in bounds)
                candidates[top]=bounds[0]
            if len(candidates)!=1:
                raise QuarkSelectionRequired(
                    f"Filename prefix not uniquely visible in active folder ({len(candidates)} rows)"
                )
            top,row=next(iter(candidates.items()))
            hit_x=(row.left+row.right)//2
            hit_y=(row.top+row.bottom)//2
            rel_x=round((hit_x-x1)*self.adapter.BASE_SIZE[0]/width)
            rel_y=round((hit_y-y1)*self.adapter.BASE_SIZE[1]/height)
            self.adapter._click_relative((rel_x,rel_y))
            time.sleep(.35)
            # Re-read after selection. Full filename must appear as its own
            # detail-pane line; a truncated list row is not accepted.
            verified=False
            for retry in range(6):
                current=self._document_text_pattern(window.hwnd).DocumentRange.GetText(-1)
                if sum(line.strip()==name for line in current.splitlines())==1:
                    verified=True
                    break
                time.sleep(.20)
            if not verified:
                raise QuarkSelectionRequired(
                    "Quark did not confirm complete filename in detail pane"
                )
            return VisibleFileSelection(name,True,top,prefix)
        finally:
            self.adapter.release_window()


def download_previously_uploaded_file(
    local_source: Path | str,
    target_folder: Path | str,
    *,
    timeout: float = 60.0,
):
    """Guarded automatic selection+download for exact files in our upload history.

    Requires the caller to be viewing the containing folder in Quark.
    Does not attempt to navigate directories or fetch a file found only in
    an unverified IndexedDB cache.
    """
    from quark_transfer_index import QuarkTransferIndex
    from quark_recent_file_cache import QuarkRecentFileCache
    from quark_drive_adapter import sha256_file
    src=Path(local_source).resolve(strict=True)
    folder=Path(target_folder).resolve(strict=True)
    if not folder.is_dir():
        raise ValueError("Target directory required")
    record=QuarkTransferIndex().resolve_completed_upload(src)
    if not record:
        raise QuarkSelectionRequired("No confirmed Quark upload record for source file")
    cached=QuarkRecentFileCache().lookup_candidate(src.name,parent_fid=record.dir_fid)
    if not cached or cached.fid!=record.fid:
        raise QuarkSelectionRequired("Cache and completed upload disagree on cloud fid")
    adapter=QuarkDesktopAdapter()
    selection=QuarkVisibleFileSelector(adapter).select_visible_file(src.name)
    if not selection.details_verified:
        raise QuarkSelectionRequired("Full cloud filename was not verified")
    return adapter.download_selected(
        folder,expected_filename=src.name,
        expected_sha256=sha256_file(src),
        expected_fid=record.fid,
        selection_verified=True,timeout=timeout,
    )
