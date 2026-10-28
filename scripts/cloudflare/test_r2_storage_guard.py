import datetime as dt
import importlib.util
from pathlib import Path
import unittest

module = importlib.util.spec_from_file_location("r2_guard",Path(__file__).with_name("r2_storage_guard.py"))
guard = importlib.util.module_from_spec(module)
module.loader.exec_module(guard)


def row(bucket, stamp, bytes_, count=1):
    return {"dimensions":{"bucketName":bucket,"datetime":stamp},
            "max":{"payloadSize":bytes_,"metadataSize":0,"objectCount":count}}


class GuardTest(unittest.TestCase):
    now=dt.datetime(2026,10,13,3,30,tzinfo=dt.timezone.utc)

    def test_totals_day_growth(self):
        rows=[
            row("openquickhost-sync-assets","2026-10-12T23:20:00Z",7_000_000_000),
            row("img","2026-10-12T23:20:00Z",3_000_000),
            row("openquickhost-sync-assets","2026-10-13T03:20:00Z",7_210_000_000),
            row("img","2026-10-13T03:20:00Z",3_000_000),
        ]
        snap=guard.snapshot(rows,self.now)
        self.assertEqual(snap["used_bytes"],7_213_000_000)
        self.assertEqual(snap["daily_change_bytes"],210_000_000)
        self.assertEqual(guard.breach_codes(snap),["r2_daily_growth_200mb"])

    def test_capacity_alert(self):
        snap={"used_bytes":9_005_000_000,"daily_change_bytes":None}
        self.assertEqual(guard.breach_codes(snap),["r2_storage_9gb"])

    def test_missing_baseline_is_null_not_zero(self):
        rows=[row("openquickhost-sync-assets","2026-10-13T03:20:00Z",10),row("img","2026-10-13T03:21:00Z",20)]
        snap=guard.snapshot(rows,self.now)
        self.assertIsNone(snap["daily_change_bytes"])
        self.assertFalse(snap["daily_change_baseline_available"])

    def test_missing_current_bucket_rejected(self):
        rows=[row("openquickhost-sync-assets","2026-10-13T03:20:00Z",10)]
        with self.assertRaisesRegex(RuntimeError,"incomplete"):
            guard.snapshot(rows,self.now)

    def test_stale_sample_rejected(self):
        rows=[row("openquickhost-sync-assets","2026-10-13T00:20:00Z",10),
              row("img","2026-10-13T00:20:00Z",20)]
        with self.assertRaisesRegex(RuntimeError,"stale"):
            guard.snapshot(rows,self.now)


if __name__=="__main__":
    unittest.main()
