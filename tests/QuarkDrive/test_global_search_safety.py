"""Offline safety tests for YanZi's Quark cross-folder adapter.

No cloud account, GUI, or real transfers are required to run these tests.
"""
from pathlib import Path
from unittest import TestCase
from unittest.mock import patch
import sys
import tempfile

HOST = Path(__file__).resolve().parents[2] / "src" / "OpenQuickHost"
BRIDGE = HOST / "CapabilityScripts" / "QuarkDrive"
sys.path.insert(0, str(BRIDGE))

from quark_global_search import download_verified_by_cloud_search
from quark_drive_adapter import QuarkSelectionRequired


class QuarkGlobalSearchFailClosedTests(TestCase):
    def test_missing_source_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            with self.assertRaises(FileNotFoundError):
                download_verified_by_cloud_search(Path(root) / "missing-test-file.txt", root)

    def test_missing_destination_rejected(self):
        with tempfile.TemporaryDirectory() as root:
            src = Path(root) / "not-in-cloud-test-file.txt"
            src.write_bytes(b"test")
            with self.assertRaises(FileNotFoundError):
                download_verified_by_cloud_search(src, Path(root) / "missing")

    def test_existing_filename_never_overwritten(self):
        with tempfile.TemporaryDirectory() as root:
            base=Path(root)
            src=base/"source"/"sample-quark-file.txt"
            src.parent.mkdir()
            src.write_bytes(b"source")
            dst=base/"dest"
            dst.mkdir()
            other=dst/src.name
            other.write_bytes(b"do-not-touch")
            with self.assertRaises(FileExistsError):
                download_verified_by_cloud_search(src,dst)
            self.assertEqual(other.read_bytes(),b"do-not-touch")

    def test_bad_timeout_refused_before_db_or_ui(self):
        with tempfile.TemporaryDirectory() as root:
            src=Path(root)/"sample-quark-file.txt"
            src.write_bytes(b"source")
            dst=Path(root)/"dest"
            dst.mkdir()
            with self.assertRaises(ValueError):
                download_verified_by_cloud_search(src,dst,timeout=0)

    def test_file_without_finished_upload_cannot_search_download(self):
        with tempfile.TemporaryDirectory() as root:
            src=Path(root)/"sample-quark-file.txt"
            src.write_bytes(b"unverified")
            dst=Path(root)/"dest"
            dst.mkdir()
            with patch("quark_global_search.QuarkTransferIndex") as factory:
                factory.return_value.resolve_completed_upload.return_value=None
                with self.assertRaises(QuarkSelectionRequired):
                    download_verified_by_cloud_search(src,dst)
                factory.return_value.resolve_completed_upload.assert_called_once()
