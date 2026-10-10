"""Experimental Quark desktop file-transfer adapter for Windows.

This operates the INSTALLED Quark desktop application's visible UI plus native
Windows file dialogs. It is not an official API and is not a headless interface.
Do not import this module to make transfers implicitly: all mutations are
performed only by explicit upload_file/download_selected calls.

The cloud file list does NOT currently expose trustworthy file-name selectors
through UI Automation; selecting a remote file must remain an independently
verified, deliberate operation. This adapter intentionally does not implement
'download_by_name' until there is such a selector.
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Optional
import hashlib
import os
import time

EventCallback = Callable[[str, dict], None]


class QuarkTransferError(RuntimeError):
    pass


class QuarkSelectionRequired(QuarkTransferError):
    pass


class QuarkIntegrityError(QuarkTransferError):
    pass


def sha256_file(file: str | Path) -> str:
    digest = hashlib.sha256()
    with Path(file).open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def verify_file(local_file: str | Path, downloaded_file: str | Path) -> dict:
    original = Path(local_file).resolve(strict=True)
    copy = Path(downloaded_file).resolve(strict=True)
    a = sha256_file(original)
    b = sha256_file(copy)
    result = {
        "original": str(original),
        "downloaded": str(copy),
        "original_bytes": original.stat().st_size,
        "downloaded_bytes": copy.stat().st_size,
        "sha256": b,
        "identical": original.stat().st_size == copy.stat().st_size and a == b,
    }
    if not result["identical"]:
        raise QuarkIntegrityError("Downloaded file does not match original SHA-256")
    return result


@dataclass(frozen=True)
class QuarkWindow:
    hwnd: int
    pid: int
    title: str
    rect: tuple[int, int, int, int]


@dataclass
class TransferSubmission:
    kind: str
    path: str
    submitted: bool
    remote_confirmed: bool = False
    explanation: str = ""
    remote_fid: str | None = None


class QuarkDesktopAdapter:
    """Isolated Windows adapter; no credentials, private endpoints, or token access."""

    WINDOW_TITLE = "夸克网盘 - 夸克"
    BASE_SIZE = (1280, 1078)
    # Calibrated on Quark 7.3.5.1009; guarded against different window layouts.
    TOOLBAR_UPLOAD = (140, 568)
    UPLOAD_MENU_FILE = (146, 617)
    TOOLBAR_DOWNLOAD_SELECTED = (646, 216)

    def __init__(self, on_event: Optional[EventCallback] = None):
        self.on_event = on_event or (lambda event, details: None)
        self._window = None
        self._was_topmost = None
        self._was_minimized = None

    def emit(self, event: str, **details):
        self.on_event(event, details)

    @staticmethod
    def _modules():
        import win32gui
        import win32con
        import win32process
        import psutil
        import uiautomation
        from pynput.mouse import Controller, Button
        return win32gui, win32con, win32process, psutil, uiautomation, Controller, Button

    def locate(self) -> QuarkWindow:
        gui, _, winproc, psutil, _, _, _ = self._modules()
        matches = []

        def visit(hwnd, _):
            if not gui.IsWindowVisible(hwnd):
                return
            if gui.GetWindowText(hwnd) != self.WINDOW_TITLE:
                return
            try:
                _, pid = winproc.GetWindowThreadProcessId(hwnd)
                proc = psutil.Process(pid)
                exe = proc.exe().replace("/", "\\").lower()
                if not exe.endswith("\\quark.exe") or "\\programs\\quark\\" not in exe:
                    return
                rect = gui.GetWindowRect(hwnd)
                matches.append(QuarkWindow(hwnd, pid, self.WINDOW_TITLE, rect))
            except (OSError, psutil.Error):
                return

        gui.EnumWindows(visit, None)
        if len(matches) != 1:
            raise QuarkTransferError(
                f"Expected one visible Quark cloud-drive window; found {len(matches)}"
            )
        return matches[0]

    def prepare_window(self) -> QuarkWindow:
        gui, con, _, _, _, _, _ = self._modules()
        window = self.locate()
        # Minimized Chromium windows report a -32000 offscreen rectangle.
        # Restore first; then check the *actual* size and hit-test points.
        self._was_topmost = bool(
            gui.GetWindowLong(window.hwnd, con.GWL_EXSTYLE) & con.WS_EX_TOPMOST
        )
        self._was_minimized = bool(gui.IsIconic(window.hwnd))
        gui.ShowWindow(window.hwnd, con.SW_RESTORE)
        time.sleep(0.2)
        rect = gui.GetWindowRect(window.hwnd)
        w = rect[2] - rect[0]
        h = rect[3] - rect[1]
        if not (1100 <= w <= 2000 and 800 <= h <= 1350):
            if gui.IsWindow(window.hwnd) and not self._was_topmost:
                gui.SetWindowPos(window.hwnd, con.HWND_NOTOPMOST, 0, 0, 0, 0,
                                 con.SWP_NOMOVE | con.SWP_NOSIZE | con.SWP_NOACTIVATE)
            raise QuarkTransferError(
                f"Unexpected Quark layout {w}x{h}; refusing coordinate interaction"
            )
        self._window = QuarkWindow(window.hwnd, window.pid, window.title, rect)
        gui.SetWindowPos(
            window.hwnd, con.HWND_TOPMOST, 0, 0, 0, 0,
            con.SWP_NOMOVE | con.SWP_NOSIZE | con.SWP_SHOWWINDOW
        )
        time.sleep(0.45)
        self.emit("window_ready", hwnd=window.hwnd, dimensions=(w, h))
        return window

    def release_window(self, minimize: bool = False):
        if not self._window:
            return
        gui, con, _, _, _, _, _ = self._modules()
        try:
            if gui.IsWindow(self._window.hwnd):
                if not self._was_topmost:
                    gui.SetWindowPos(
                        self._window.hwnd, con.HWND_NOTOPMOST, 0, 0, 0, 0,
                        con.SWP_NOMOVE | con.SWP_NOSIZE | con.SWP_NOACTIVATE
                    )
                if minimize or self._was_minimized:
                    gui.ShowWindow(self._window.hwnd, con.SW_MINIMIZE)
        finally:
            self._window = None
            self._was_topmost = None
            self._was_minimized = None

    def _click_relative(self, point: tuple[int, int]):
        gui, con, _, _, _, Controller, Button = self._modules()
        if not self._window or not gui.IsWindow(self._window.hwnd):
            raise QuarkTransferError("Quark window is not prepared")
        r = gui.GetWindowRect(self._window.hwnd)
        w, h = r[2] - r[0], r[3] - r[1]
        if not (1100 <= w <= 2000 and 800 <= h <= 1350):
            raise QuarkTransferError("Window dimensions changed during operation")
        x = r[0] + round(point[0] * w / self.BASE_SIZE[0])
        y = r[1] + round(point[1] * h / self.BASE_SIZE[1])
        hit = gui.WindowFromPoint((x, y))
        owner = gui.GetAncestor(hit, con.GA_ROOT) if hit else 0
        if owner != self._window.hwnd:
            raise QuarkTransferError(
                "Target point is covered by another window; no click sent"
            )
        mouse = Controller()
        mouse.position = (x, y)
        if tuple(mouse.position) != (x, y):
            raise QuarkTransferError("Mouse coordinate mismatch; no click sent")
        mouse.click(Button.left)
        self.emit("safe_click", relative=point)

    def _locate_upload_control(self) -> tuple[int, int]:
        """Locate upload control visually inside the verified Quark window.

        This is template matching, not OCR. If a UI update or occlusion makes
        the control uncertain, abort rather than clicking another control.
        """
        if not self._window:
            raise QuarkTransferError("Quark window not prepared")
        import cv2
        import numpy as np
        from PIL import ImageGrab
        gui, _, _, _, _, _, _ = self._modules()
        template_file = Path(__file__).with_name("quark-upload-control.png")
        template = cv2.imread(str(template_file), cv2.IMREAD_GRAYSCALE)
        if template is None:
            raise QuarkTransferError("Upload locator template missing")
        screen = np.asarray(ImageGrab.grab().convert("RGB"))
        gray = cv2.cvtColor(screen, cv2.COLOR_RGB2GRAY)
        x1, y1, x2, y2 = gui.GetWindowRect(self._window.hwnd)
        xa, ya = max(0, x1), max(0, y1)
        xb, yb = min(screen.shape[1], x2), min(screen.shape[0], y2)
        if (xb - xa < template.shape[1] or yb - ya < template.shape[0]):
            raise QuarkTransferError("Quark window not visible enough for locator")
        roi = gray[ya:yb, xa:xb]
        matches = cv2.matchTemplate(roi, template, cv2.TM_CCOEFF_NORMED)
        _, confidence, _, top_left = cv2.minMaxLoc(matches)
        cx = xa + top_left[0] + template.shape[1] // 2
        cy = ya + top_left[1] + template.shape[0] // 2
        w, h = x2 - x1, y2 - y1
        normalized = (round((cx - x1) * self.BASE_SIZE[0] / w),
                      round((cy - y1) * self.BASE_SIZE[1] / h))
        if confidence < 0.93 or not (45 < normalized[0] < 350 and
                                     100 < normalized[1] < 780):
            raise QuarkTransferError(
                f"Upload control not identified confidently ({confidence:.3f})"
            )
        self.emit("upload_control_found", score=round(float(confidence), 5),
                  relative=normalized)
        return normalized

    def _find_native_dialog(self, edit_id: str):
        gui, _, _, _, uia, _, _ = self._modules()
        results = []
        if not self._window:
            raise QuarkTransferError("No active Quark context")
        wr = gui.GetWindowRect(self._window.hwnd)

        def visit(hwnd, _):
            if not gui.IsWindowVisible(hwnd) or gui.GetClassName(hwnd) != "#32770":
                return
            title = gui.GetWindowText(hwnd)
            if title not in ("选择文件", "打开", "选择文件夹"):
                return
            # Native file dialogs must be OWNED by this exact Quark window.
            # Overlap alone is not enough: another app might have a picker open.
            if gui.GetAncestor(hwnd, 3) != self._window.hwnd:
                return
            bounds = gui.GetWindowRect(hwnd)
            overlap_x = max(0, min(wr[2], bounds[2]) - max(wr[0], bounds[0]))
            overlap_y = max(0, min(wr[3], bounds[3]) - max(wr[1], bounds[1]))
            if overlap_x * overlap_y <= 0:
                return
            try:
                ctrl = uia.ControlFromHandle(hwnd)
                edit = ctrl.EditControl(AutomationId=edit_id)
                ok = ctrl.ButtonControl(AutomationId="1")
                if edit.Exists(0, 0) and ok.Exists(0, 0):
                    results.append((hwnd, edit, ok))
            except Exception:
                return

        gui.EnumWindows(visit, None)
        if len(results) > 1:
            raise QuarkTransferError("Ambiguous Windows file dialogs; refusing action")
        return results[0] if results else None

    def _wait_dialog(self, edit_id: str, timeout: float = 5.0):
        end = time.monotonic() + timeout
        while time.monotonic() < end:
            dialog = self._find_native_dialog(edit_id)
            if dialog:
                return dialog
            time.sleep(0.15)
        return None

    def _complete_native_dialog(self, dialog, value: str):
        gui, _, _, _, _, _, _ = self._modules()
        hwnd, edit, button = dialog
        if not gui.IsWindow(hwnd):
            raise QuarkTransferError("Native file picker closed before submission")
        edit.GetValuePattern().SetValue(value)
        button.GetInvokePattern().Invoke()
        for _ in range(30):
            if not gui.IsWindow(hwnd) or not gui.IsWindowVisible(hwnd):
                self.emit("native_dialog_closed")
                return
            time.sleep(0.12)
        raise QuarkTransferError("Native file picker remained open after submit")

    def upload_file(
        self, file_path: str | Path, *,
        wait_for_cloud: bool = False,
        timeout: float = 90.0,
    ) -> TransferSubmission:
        file = Path(file_path).resolve(strict=True)
        if not file.is_file() or not file.name:
            raise ValueError("Expected an existing regular file")
        # Exact task path, size and time must all match; a stale previous upload
        # is not valid evidence that this new submission has completed.
        started_ms = int(time.time() * 1000) - 2000
        self.prepare_window()
        try:
            # Current Quark folder view opens the file picker directly.
            # Do not use a blind menu-item coordinate when the page changes.
            dialog = self._find_native_dialog("1148")
            if not dialog:
                point = self._locate_upload_control()
                self._click_relative(point)
                dialog = self._wait_dialog("1148", 5.0)
            if not dialog:
                raise QuarkTransferError("Upload file picker did not appear after verified button click")
            self._complete_native_dialog(dialog, str(file))
            self.emit("upload_picker_submitted", name=file.name, size=file.stat().st_size)
            submitted = TransferSubmission(
                "upload", str(file), True, False,
                "File picker submission confirmed; cloud completion must be verified separately"
            )
        finally:
            self.release_window()
        if wait_for_cloud:
            from quark_transfer_index import QuarkTransferIndex
            record = QuarkTransferIndex().wait_for_upload(
                file, after_ms=started_ms, timeout=timeout
            )
            submitted.remote_confirmed = True
            submitted.remote_fid = record.fid
            submitted.explanation = (
                "Persisted upload task FINISH: all bytes transferred, fid available"
            )
            self.emit("upload_complete", name=file.name, bytes=record.size,
                      fid_suffix=record.fid[-6:])
        return submitted

    def download_selected(
        self, folder_path: str | Path, *, expected_filename: str,
        expected_sha256: str | None = None, timeout: float = 60.0,
        selection_verified: bool = False,
        expected_fid: str | None = None
    ) -> TransferSubmission:
        # File selection MUST be independently verified beforehand.
        # This is intentionally NOT a general 'download_by_name' implementation.
        if (not expected_filename or
                Path(expected_filename).name != expected_filename or
                "/" in expected_filename or "\\" in expected_filename):
            raise ValueError("Expected a basename, not a file path")
        folder = Path(folder_path).resolve(strict=True)
        if not folder.is_dir():
            raise ValueError("Download destination must be a directory")
        target = folder / expected_filename
        if target.exists():
            raise FileExistsError(f"Refusing to overwrite {target}")
        if expected_fid is not None:
            import re
            if re.fullmatch(r"[a-fA-F0-9]{32}", expected_fid) is None:
                raise ValueError("Expected a 32-character Quark cloud fid")
        if not selection_verified:
            raise QuarkSelectionRequired(
                "Cloud file selection was not independently verified; refusing download"
            )
        started_ms = int(time.time() * 1000) - 2000
        self.prepare_window()
        try:
            self._click_relative(self.TOOLBAR_DOWNLOAD_SELECTED)
            dialog = self._wait_dialog("1152", 5.0)
            if not dialog:
                raise QuarkSelectionRequired(
                    "Download folder picker missing. Select exactly the target file in Quark first."
                )
            self._complete_native_dialog(dialog, str(folder))
        finally:
            self.release_window()
        stable = 0
        previous_size = None
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if target.is_file():
                size = target.stat().st_size
                stable = stable + 1 if size == previous_size else 0
                previous_size = size
                if stable >= 3:
                    if expected_sha256 and sha256_file(target).lower() != expected_sha256.lower():
                        raise QuarkIntegrityError(
                            "Downloaded hash mismatch; inspect before using this file"
                        )
                    if expected_fid:
                        from quark_transfer_index import QuarkTransferIndex
                        task = QuarkTransferIndex().resolve_completed_download(
                            target, expected_fid=expected_fid, after_ms=started_ms
                        )
                        if task is None:
                            # A stable file alone does not prove the intended
                            # cloud file ID was actually downloaded.
                            time.sleep(0.5)
                            continue
                    self.emit("download_complete", name=target.name, bytes=size,
                              fid_suffix=expected_fid[-6:] if expected_fid else None)
                    return TransferSubmission(
                        "download", str(target), True,
                        bool(expected_sha256 or expected_fid),
                        "File complete; cloud fid and/or SHA-256 verified"
                        if (expected_sha256 or expected_fid) else
                        "File present and stable; origin/content not cryptographically verified",
                        remote_fid=expected_fid,
                    )
            time.sleep(0.5)
        raise QuarkTransferError("Download did not produce expected filename before timeout")

    def download_by_name(
        self,
        filename: str,
        folder: str | Path,
        *,
        original_local_file: str | Path | None = None,
        timeout: float = 60.0,
    ) -> TransferSubmission:
        """Automatic semantic selection for files with a verified upload source.

        The cloud file must be visible in the CURRENT Quark folder. We require
        the original local source to prove its fid and SHA-256. A mere IndexedDB
        cached filename is insufficient authorization for unattended download.
        """
        if original_local_file is None:
            raise QuarkSelectionRequired(
                "Automatic filename download requires original_local_file "
                "with a confirmed upload record and hash"
            )
        src = Path(original_local_file).resolve(strict=True)
        if src.name != filename:
            raise ValueError("Filename differs from the verified upload source")
        from quark_semantic_selection import download_previously_uploaded_file
        return download_previously_uploaded_file(src, folder, timeout=timeout)
