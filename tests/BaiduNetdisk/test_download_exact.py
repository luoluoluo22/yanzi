"""Fail-closed tests for exact-name Baidu desktop download.

Tests do not click the desktop or issue a download. Live transfer verification
is performed separately against only the dedicated synthetic test fixture.
"""
from pathlib import Path
import sys
import tempfile
import unittest

SCRIPT=Path(__file__).resolve().parents[2]/"src"/"OpenQuickHost"/"CapabilityScripts"/"BaiduNetdisk"
sys.path.insert(0,str(SCRIPT))

from baidu_desktop_download import download_exact
from baidu_transfer_index import BaiduTransferIndex


class BaiduDownloadSafetyTests(unittest.TestCase):
    base=dict(filename="test-unique-user-file.txt",
              expected_cloud_path="/test-unique-user-file.txt",
              expected_size=55)

    def test_reject_wrong_cloud_path_filename(self):
        with self.assertRaises(ValueError):
            download_exact(**{**self.base,"expected_cloud_path":"/not-the-same-name.txt"})

    def test_reject_relative_cloud_path(self):
        with self.assertRaises(ValueError):
            download_exact(**{**self.base,"expected_cloud_path":"test-unique-user-file.txt"})

    def test_reject_negative_and_too_large_size(self):
        for size in (-1, 2*1024**3+1):
            with self.subTest(size=size),self.assertRaises(ValueError):
                download_exact(**{**self.base,"expected_size":size})

    def test_reject_unsafe_filename(self):
        for filename in ("../escape.txt","folder\\file.txt","a.txt\n"):
            with self.subTest(filename=repr(filename)),self.assertRaises(ValueError):
                download_exact(**{**self.base,"filename":filename})

    def test_reject_invalid_sha256(self):
        with self.assertRaises(ValueError):
            download_exact(**{**self.base,"expected_sha256":"aaa"})

    def test_reject_invalid_timeout(self):
        for timeout in (0,14,301):
            with self.subTest(timeout=timeout),self.assertRaises(ValueError):
                download_exact(**{**self.base,"timeout_seconds":timeout})

    def test_refuse_existing_destination_without_touch(self):
        with tempfile.TemporaryDirectory() as folder:
            target=Path(folder)/self.base["filename"]
            target.write_bytes(b"Preserve old file")
            original=target.read_bytes()
            with self.assertRaises(FileExistsError):
                download_exact(**self.base,copy_to_folder=folder)
            self.assertEqual(target.read_bytes(),original)

    def test_local_copy_requires_existing_folder(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(FileNotFoundError):
                download_exact(**self.base,
                               copy_to_folder=Path(folder)/"missing")

    def test_recent_completed_download_requires_correct_cloud_path_and_time(self):
        source=Path(r"F:\Desktop\cloud-drive-eval-20261009\AI-baidu-verified-upload-20261009-174820.txt")
        from baidu_transfer_index import sha256_file
        expected="/"+source.name
        idx=BaiduTransferIndex()
        history=idx.lookup_download_for_cloud_path(expected)
        self.assertIsNotNone(history)
        self.assertTrue(history.completed)
        self.assertEqual(history.file_size,55)
        self.assertEqual(sha256_file(history.local_path),sha256_file(source))
        self.assertIsNone(idx.lookup_download_for_cloud_path(
            expected,after_seconds=history.started_at+10))
