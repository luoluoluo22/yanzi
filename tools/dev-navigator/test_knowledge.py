"""Unit verification: candidate discovery and truthful local QA evidence lifecycle."""
from __future__ import annotations
from pathlib import Path
import tempfile
import unittest
import json
import os
from unittest import mock
import knowledge

class KnowledgeTests(unittest.TestCase):
    def setUp(self):
        td=tempfile.TemporaryDirectory()
        self.addCleanup(td.cleanup)
        self.dir=Path(td.name)
        self.path=self.dir/"qa-ledger.json"
        self.sha="a"*40
        self.source={"commit":self.sha,"dirty":False}
        self.plan={
            "source":{"mappedFeatureIds":["ai-model-menu"]},
            "validation":{
                "tests":[{"path":"tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
                           "execution":"safe-targeted","command":["dotnet","run"]},
                          {"path":"scripts/unsafe.ps1","execution":"review-before-running",
                           "command":["powershell.exe","-File","scripts/unsafe.ps1"]}],
                "hiddenUiSmoke":{"command":["powershell.exe","-File","scripts/verify-yanzi-ui-hidden.ps1"],
                                 "scenario":"ai-chat-model"},
            },
        }
    def test_candidate_discovery_not_promoted(self):
        index={"files":{"src/Test.xaml":{"ui":[
            {"name":"Known","line":10,"handlers":{"Click":"Known_Click"},"labels":{"ToolTip":"known"}},
            {"name":"Unknown","line":20,"handlers":{"Click":"Unknown_Click"},"labels":{"ToolTip":"new"}},
            {"name":"","line":21,"handlers":{},"labels":{"Text":"decorative"}}]}}}
        fs=[{"id":"known","handlers":["Known_Click"],"paths":["src/Test.xaml"]}]
        result=knowledge.discover_ui(index,fs,limit=30)
        self.assertEqual(result["knownHandlersMapped"],1)
        self.assertEqual(result["unmappedCandidates"],1)
        self.assertEqual(result["candidates"][0]["handlers"],["Unknown_Click"])
        self.assertNotIn("id",result["candidates"][0])
    def test_filtered_candidate_discovery(self):
        idx={"files":{"src/Foo.xaml":{"ui":[
            {"name":"SettingsButton","line":1,"handlers":{"Click":"Settings_Click"},"labels":{}},
            {"name":"ClipboardButton","line":2,"handlers":{"Click":"Clipboard_Click"},"labels":{}}]}}}
        data=knowledge.discover_ui(idx,[],limit=10,query="clipboard")
        self.assertEqual(data["unmappedCandidates"],2)
        self.assertEqual(data["matchedCandidates"],1)
        self.assertEqual(data["candidates"][0]["name"],"ClipboardButton")

    def test_worktrees_share_qa_ledger(self):
        with mock.patch.object(knowledge.navigator,"git",return_value=str(self.dir/"common"/".git")):
            with mock.patch.dict(os.environ,{"LOCALAPPDATA":str(self.dir)}):
                x=knowledge.ledger_path(self.dir/"worktree-a")
                y=knowledge.ledger_path(self.dir/"worktree-b")
                self.assertEqual(x,y)

    def test_record_real_runner_results_and_no_manual_pass(self):
        executed={"targeted":[{"name":"tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
                               "status":"passed","exitCode":0},
                              {"name":"scripts/unsafe.ps1","status":"passed","exitCode":0}],
                  "hiddenUiSmoke":{"status":"failed","exitCode":1}}
        written=knowledge.record_executed(self.dir,self.plan,executed,ledger=self.path,source=self.source)
        self.assertEqual(len(written),2)
        self.assertEqual({x["scope"] for x in written},
                        {"isolated-dotnet-test","isolated-hidden-desktop-wpf"})
        self.assertNotIn("outputTail",str(knowledge.load_ledger(self.path)))
        summary=knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=self.source)
        self.assertFalse(summary["ai-model-menu"]["isolatedChecksCurrent"])
        self.assertEqual(summary["ai-model-menu"]["installedAppUi"],"not-verified")
        self.assertEqual(len(summary["ai-model-menu"]["records"]),2)
    def test_old_commit_and_dirty_source_cannot_be_current(self):
        executed={"targeted":[{"name":"tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
                               "status":"passed","exitCode":0}]}
        knowledge.record_executed(self.dir,self.plan,executed,ledger=self.path,source=self.source)
        self.assertTrue(knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=self.source)
                        ["ai-model-menu"]["isolatedChecksCurrent"])
        old={"commit":"b"*40,"dirty":False}
        self.assertFalse(knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=old)
                         ["ai-model-menu"]["isolatedChecksCurrent"])
        dirty={"commit":self.sha,"dirty":True}
        self.assertFalse(knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=dirty)
                         ["ai-model-menu"]["isolatedChecksCurrent"])
        dirty_run={"commit":self.sha,"dirty":True}
        knowledge.record_executed(self.dir,self.plan,executed,ledger=self.path,source=dirty_run)
        self.assertFalse(knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=self.source)
                         ["ai-model-menu"]["isolatedChecksCurrent"])
    def test_invalid_ledger_protected(self):
        self.path.write_text("{corrupted",encoding="utf-8")
        with self.assertRaises(ValueError):
            knowledge.record_executed(self.dir,self.plan,{"targeted":[
                {"name":"tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
                 "status":"passed"}]},ledger=self.path,source=self.source)
        self.assertEqual(self.path.read_text(encoding="utf-8"),"{corrupted")
    def test_one_pass_cannot_hide_other_failed_test_same_scope(self):
        base={"atUtc":"2026-10-31T08:00:00+00:00","features":["ai-model-menu"],
              "scope":"isolated-dotnet-test","commit":self.sha,"dirty":False}
        knowledge.save_ledger(self.path,[
            {**base,"test":"a.csproj","status":"failed"},
            {**base,"test":"b.csproj","status":"passed"}])
        result=knowledge.qa_status(self.dir,["ai-model-menu"],ledger=self.path,source=self.source)
        self.assertEqual(len(result["ai-model-menu"]["records"]),2)
        self.assertFalse(result["ai-model-menu"]["isolatedChecksCurrent"])

    def test_no_feature_no_record(self):
        self.plan["source"]["mappedFeatureIds"]=[]
        out=knowledge.record_executed(self.dir,self.plan,{"hiddenUiSmoke":{"status":"passed"}},
                                      ledger=self.path,source=self.source)
        self.assertEqual(out,[])
        self.assertFalse(self.path.exists())
    def test_unknown_qa_scope_not_written(self):
        out=knowledge.record_executed(self.dir,self.plan,{
            "targeted":[{"name":"scripts/unsafe.ps1","status":"passed"}],
            "hiddenUiSmoke":{"status":"not-applicable"}},ledger=self.path,source=self.source)
        self.assertEqual(out,[])
    def test_ledger_bound(self):
        records=[{"i":i} for i in range(knowledge.LEDGER_LIMIT+11)]
        knowledge.save_ledger(self.path,records)
        items=knowledge.load_ledger(self.path)
        self.assertEqual(len(items),knowledge.LEDGER_LIMIT)
        self.assertEqual(items[0]["i"],11)

if __name__=="__main__":
    unittest.main(verbosity=2)
