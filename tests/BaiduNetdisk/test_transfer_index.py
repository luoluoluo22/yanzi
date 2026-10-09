"""Baidu desktop read-only transfer verification tests.

All non-acceptance tests use scratch SQLite fixtures under temp directories;
none writes to Baidu's actual application databases.
"""
from pathlib import Path
import sqlite3
import sys
import tempfile
import time
import unittest

ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT / "src" / "OpenQuickHost" / "CapabilityScripts" / "BaiduNetdisk"
sys.path.insert(0,str(SRC))

from baidu_transfer_index import (
    BaiduTransferIndex, BaiduTransferIndexError,
)


class BaiduReadOnlyTransferTests(unittest.TestCase):
    original=Path(r"F:\Desktop\cloud-drive-eval-20261009\AI-baidu-desktop-roundtrip-20261009.txt")
    downloaded=Path(r"F:\Backup\Downloads\AI-baidu-desktop-roundtrip-20261009.txt")

    def test_real_desktop_upload_record_completed(self):
        row=BaiduTransferIndex().resolve_completed("upload",self.original)
        self.assertIsNotNone(row)
        self.assertTrue(row.completed)
        self.assertEqual(row.file_size,107)
        self.assertEqual(row.error_code,0)
        self.assertEqual(row.server_path,"/"+self.original.name)

    def test_real_desktop_download_record_completed(self):
        row=BaiduTransferIndex().resolve_completed("download",self.downloaded)
        self.assertIsNotNone(row)
        self.assertTrue(row.completed)
        self.assertEqual(row.file_size,107)
        self.assertEqual(row.server_path,"/"+self.original.name)

    def test_real_roundtrip_equal_sha256(self):
        proof=BaiduTransferIndex().verify_roundtrip(self.original,self.downloaded)
        self.assertTrue(proof["confirmed"])
        self.assertEqual(proof["bytes"],107)
        self.assertFalse(proof["liveCloudExistenceVerified"])
        self.assertEqual(len(proof["sha256"]),64)

    def test_stale_transfer_time_rejected(self):
        future=int(time.time())+3600
        self.assertIsNone(BaiduTransferIndex().resolve_completed(
            "upload",self.original,after_seconds=future
        ))

    def test_mismatched_cloud_path_rejected(self):
        self.assertIsNone(BaiduTransferIndex().resolve_completed(
            "download",self.downloaded,
            expected_server_path="/not-the-test-file.txt"
        ))

    def test_same_path_cannot_claim_roundtrip(self):
        with self.assertRaises(ValueError):
            BaiduTransferIndex().verify_roundtrip(self.original,self.original)

    def test_multiple_account_stores_fail_closed(self):
        with tempfile.TemporaryDirectory() as dir:
            root=Path(dir)
            for name in ("one","two"):
                store=root/name
                store.mkdir()
                (store/"upload.db").touch()
                (store/"transmission.db").touch()
            with self.assertRaises(BaiduTransferIndexError):
                BaiduTransferIndex(root)

    def test_fixture_error_code_and_size_rejected(self):
        with tempfile.TemporaryDirectory() as dir:
            root=Path(dir)
            source=root/"my-probe-file.txt"
            source.write_bytes(b"TEST")
            store=root/"baidu_account"
            store.mkdir()
            for dbfile in ("upload.db","transmission.db"):
                con=sqlite3.connect(store/dbfile)
                con.execute("CREATE TABLE upload_history_file "
                    "(local_path TEXT, server_path TEXT, isdir INTEGER, "
                    "file_size INTEGER, op_starttime INTEGER, op_endtime INTEGER, "
                    "error_code INTEGER)")
                con.execute("INSERT INTO upload_history_file VALUES "
                    "(?,?,?,?,?,?,?)",
                    (str(source),"/my-probe-file.txt",0,4,1,2,404))
                con.commit()
                con.close()
            idx=BaiduTransferIndex(store)
            self.assertIsNone(idx.resolve_completed("upload",source))
            conn=sqlite3.connect(store/"upload.db")
            conn.execute("UPDATE upload_history_file SET error_code=NULL")
            conn.commit()
            self.assertIsNone(idx.resolve_completed("upload",source))
            conn.execute("UPDATE upload_history_file SET error_code=0,file_size=9")
            conn.commit()
            self.assertIsNone(idx.resolve_completed("upload",source))
            conn.close()


if __name__=="__main__":
    unittest.main(verbosity=2)
