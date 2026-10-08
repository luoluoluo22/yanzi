#!/usr/bin/env python3
"""Offline tests: no network and no credentials required."""
import datetime as dt
import importlib.util
import pathlib
import os
import tempfile
import unittest

SPEC = importlib.util.spec_from_file_location("monitor", pathlib.Path(__file__).with_name("cloudflare_usage_monitor.py"))
monitor = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(monitor)

class MonitorTests(unittest.TestCase):
    def test_aggregate_account_and_target_only(self):
        data = {
            "allD1": [{"dimensions":{"date":"2026-10-08","databaseId":"a"},"sum":{"rowsRead":100,"rowsWritten":20}},
                      {"dimensions":{"date":"2026-10-08","databaseId":"b"},"sum":{"rowsRead":12,"rowsWritten":3}}],
            "yanziD1": [{"dimensions":{"date":"2026-10-08","databaseId":"a"},"sum":{"rowsRead":100,"rowsWritten":20}}],
            "workers":[{"dimensions":{"datetimeHour":"2026-10-08T01:00:00Z","scriptName":"yanzi-sync"},"sum":{"requests":300,"errors":0}},
                       {"dimensions":{"datetimeHour":"2026-10-08T03:00:00Z","scriptName":"other"},"sum":{"requests":70,"errors":1}}]
        }
        x = monitor.aggregate(data,dt.date(2026,10,8))
        self.assertEqual(x["d1_all"]["rows_read"],112)
        self.assertEqual(x["d1_yanzi"]["rows_read"],100)
        self.assertEqual(x["workers_requests"],370)
        self.assertEqual(x["yanzi_requests"],300)
        self.assertEqual(x["workers_errors"],1)

    def test_thresholds_and_regression(self):
        old={"d1_all":{"rows_read":300000,"rows_written":10000},"workers_requests":30000,"workers_errors":0}
        new={"d1_all":{"rows_read":4000000,"rows_written":31000},"workers_requests":51000,"workers_errors":0}
        metrics,alerts=monitor.assess(new,old)
        self.assertEqual(metrics["rows_read"]["remaining"],1000000)
        self.assertIn("quota_75_percent", [a["reason"] for a in alerts])
        self.assertIn("usage_regression", [a["reason"] for a in alerts])

    @unittest.skipIf(os.name == 'nt', 'Linux POSIX permissions only')
    def test_secret_file_must_be_private(self):
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/"token.env"
            p.write_text("CLOUDFLARE_API_TOKEN=testonly123\n")
            p.chmod(0o644)
            with self.assertRaisesRegex(RuntimeError,"permissions"):
                monitor.load_token(p)
            p.chmod(0o600)
            self.assertEqual(monitor.load_token(p),"testonly123")

    @unittest.skipIf(os.name == 'nt', 'Linux POSIX permissions only')
    def test_report_atomic_permissions(self):
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/"daily"/"report.json"
            monitor.write_json(p,{"safe":True})
            self.assertIn('"safe": true',p.read_text())
            self.assertEqual(p.stat().st_mode & 0o777, 0o600)

if __name__ == "__main__":
    unittest.main()
