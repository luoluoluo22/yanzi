"""Developer triage: route evidence without leaking raw logs or inventing root causes."""
from __future__ import annotations
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import diagnose

class TriageTests(unittest.TestCase):
    def setUp(self):
        td=tempfile.TemporaryDirectory(prefix="yanzi-triage-test-")
        self.addCleanup(td.cleanup)
        self.root=Path(td.name)
        self.logs=self.root/"OpenQuickHost"/"logs"
        self.logs.mkdir(parents=True)
        self.index={"files":{
            "src/OpenQuickHost/SettingsAiController.cs":{"ui":[],"symbols":[],"references":[]},
            "src/OpenQuickHost/AiModelMenuOrdering.cs":{"ui":[],"symbols":[],"references":[]},
            "tests/GeminiProviderVerification/GeminiProviderVerification.csproj":{
                "ui":[],"symbols":[],"references":[]}}}
        self.features=[
            {"id":"ai-provider-settings","title":"供应商设置","aliases":["Gemini接入"],
             "paths":["src/OpenQuickHost/SettingsAiController.cs"],
             "handlers":[],"tests":["tests/GeminiProviderVerification/*.csproj"]},
            {"id":"ai-model-menu","title":"模型选择","aliases":["模型列表不能滚动"],
             "paths":["src/OpenQuickHost/AiModelMenuOrdering.cs"],
             "handlers":[],"tests":[]}
        ]
    def plan(self,**kwargs):
        with mock.patch.object(diagnose.knowledge,"qa_status",return_value={}):
            return diagnose.analyze(self.root,index=self.index,features=self.features,
                                    include_recent=False,**kwargs)
    def test_gemini_credential_fingerprint_routes_provider(self):
        r=self.plan(description="Gemini 返回401 UNAUTHENTICATED CREDENTIALS_MISSING")
        self.assertEqual(r["features"][0]["id"],"ai-provider-settings")
        self.assertIn("CREDENTIALS_MISSING",r["signals"])
        self.assertIn("HTTP_401",r["signals"])
        self.assertEqual(r["status"],"triage-candidate-not-confirmed")
        self.assertTrue(r["unresolved"])
    def test_model_menu_with_screenshot_note_but_no_image_claim(self):
        r=self.plan(description="模型列表不能滚动",screenshot_note="模型列表有50个选项")
        self.assertEqual(r["features"][0]["id"],"ai-model-menu")
        self.assertFalse(r["input"]["screenshotImageAnalyzed"])
    def test_secret_text_never_emitted(self):
        key="sk-" + "TOPSECRETNEVERLOG" * 3
        r=self.plan(description="401 authorization: Bearer "+key)
        payload=json.dumps(r,ensure_ascii=False)
        self.assertNotIn(key,payload)
        self.assertNotIn("Bearer",payload)
    def test_log_fingerprints_only(self):
        secret="AIza" + "FAKETESTSECRET"*4
        f=self.logs/"ai-chat.log"
        f.write_text("2026-10-31 stage=failed status=401 CREDENTIALS_MISSING key="+secret+"\n",encoding="utf-8")
        evidence=diagnose.sample_log(f,local_data=self.root)
        self.assertIn("CREDENTIALS_MISSING",evidence["fingerprints"])
        self.assertTrue(evidence["rawLogIncluded"] is False)
        self.assertNotIn(secret,json.dumps(evidence))
        r=self.plan(description="",log_evidence=evidence)
        self.assertEqual(r["features"][0]["id"],"ai-provider-settings")
    def test_reject_outside_allowed_log_tree(self):
        outside=self.root/"secrets.env"
        outside.write_text("PRIVATE_API_KEY",encoding="utf-8")
        with self.assertRaises(ValueError):
            diagnose.sample_log(outside,local_data=self.root)
        other=self.logs/"other.log"
        other.write_text("timeout",encoding="utf-8")
        with self.assertRaises(ValueError):
            diagnose.sample_log(other,local_data=self.root)
    def test_reject_large_log(self):
        f=self.logs/"runtime.log"
        with f.open("wb") as stream:
            stream.seek(diagnose.MAX_LOG_BYTES)
            stream.write(b"\x00")
        with self.assertRaises(ValueError):
            diagnose.sample_log(f,local_data=self.root)
    def test_no_symptom_is_not_guess(self):
        with self.assertRaises(ValueError):
            self.plan(description="")
    def test_no_matching_evidence_never_labels_cause(self):
        r=self.plan(description="something completely unfamiliar")
        self.assertEqual(r["features"],[])
        self.assertEqual(r["status"],"triage-candidate-not-confirmed")
        self.assertIn("先确认精确控件",r["nextChecks"][0])
    def test_recency_is_not_causality(self):
        with mock.patch.object(diagnose,"recent_changes",return_value=[
            {"commit":"1234567","date":"2026-10-31","subject":"update",
             "files":["src/OpenQuickHost/SettingsAiController.cs"]}]):
            with mock.patch.object(diagnose.knowledge,"qa_status",return_value={}):
                r=diagnose.analyze(self.root,index=self.index,features=self.features,
                                   description="CREDENTIALS_MISSING",include_recent=True)
        self.assertEqual(len(r["recentChanges"]),1)
        self.assertIn("候选不等于根因",r["unresolved"][0])
    def test_long_untrusted_description_rejected(self):
        with self.assertRaises(ValueError):
            self.plan(description="A"*(diagnose.MAX_QUERY_CHARS+1))

if __name__=="__main__":
    unittest.main(verbosity=2)
