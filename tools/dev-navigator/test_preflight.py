"""Preflight safety/coverage contract tests (no production side effects)."""
from __future__ import annotations
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import preflight
import navigator

class PreflightTests(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory(prefix="yanzi-preflight-test-")
        self.addCleanup(tmp.cleanup)
        self.root=Path(tmp.name)
        files=[
            "src/OpenQuickHost/MainWindow.xaml",
            "src/OpenQuickHost/MainWindow.AiChat.cs",
            "src/Yanzi.UI.Wpf/YanziDropdownMenu.cs",
            "tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
            "scripts/verify-yanzi-ui-hidden.ps1",
            "scripts/unsafe-config-update.ps1",
        ]
        for name in files:
            path=self.root/name
            path.parent.mkdir(parents=True,exist_ok=True)
            path.write_text('<Button x:Name="ModelButton" Click="Model_Click" ToolTip="模型列表"/>'
                if name.endswith(".xaml") else
                'public class Foo {\n private void Model_Click(object s) {}\n}',encoding="utf-8")
        self.index={"stats":{"files":6,"scanned":6,"reused":0},"files":{}}
        for name in files:
            self.index["files"][name]=navigator.parse_file(name,(self.root/name).read_text(encoding="utf-8"))
        self.features=[{
            "id":"ai-model-menu","title":"模型菜单","aliases":["模型列表","滚动模型"],
            "paths":["src/OpenQuickHost/MainWindow.xaml","src/OpenQuickHost/MainWindow.AiChat.cs",
                     "src/Yanzi.UI.Wpf/YanziDropdownMenu.cs"],
            "handlers":["Model_Click"],
            "tests":["tests/ModelMenuScrollVerification/*.csproj","scripts/unsafe-config-update.ps1"]
        }]
    def plan(self,**kwargs):
        return preflight.make_plan(self.root,index=self.index,features=self.features,**kwargs)
    def test_locate_ui_impact_single_call(self):
        result=self.plan(query="模型列表",ui_query="Model_Click",
            paths=["src/Yanzi.UI.Wpf/YanziDropdownMenu.cs"],
            expect=["50 models scroll"],preserve=["tabs stay fixed"])
        self.assertEqual(result["source"]["mappedFeatureIds"],["ai-model-menu"])
        self.assertEqual(result["source"]["uiControls"][0]["name"],"ModelButton")
        self.assertEqual(result["impact"]["affected"][0]["id"],"ai-model-menu")
        self.assertTrue(result["validation"]["hiddenUiSmoke"]["recommended"])
        self.assertFalse(result["validation"]["hiddenUiSmoke"]["automaticallyCoversFeature"])
    def test_safe_test_selection(self):
        result=self.plan(query="模型列表",expect=["works"])
        tests={item["path"]:item for item in result["validation"]["tests"]}
        self.assertEqual(tests["tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj"]["execution"],"safe-targeted")
        self.assertEqual(tests["scripts/unsafe-config-update.ps1"]["execution"],"review-before-running")
        with mock.patch.object(preflight,"run_checked",return_value={"status":"passed"}) as fn:
            check=preflight.run_opt_in_tests(self.root,result,targeted=True,hidden_ui=False)
            self.assertEqual(len(check["targeted"]),1)
            self.assertEqual(fn.call_count,1)
    def test_hidden_ui_is_opt_in_generic(self):
        result=self.plan(query="模型列表")
        with mock.patch.object(preflight,"run_checked",return_value={"status":"passed"}) as fn:
            check=preflight.run_opt_in_tests(self.root,result,targeted=False,hidden_ui=True)
            self.assertEqual(check["hiddenUiSmoke"]["status"],"passed")
            self.assertIn("ai-chat-model",str(fn.call_args))
        self.assertFalse(result["validation"]["hiddenUiSmoke"]["automaticallyCoversFeature"])
    def test_unknown_scope_not_ready(self):
        result=self.plan(query="不存在的业务",paths=["src/Unknown.cs"])
        self.assertEqual(result["review"]["status"],"needs-review")
        self.assertIn("src/Unknown.cs",result["impact"]["unmapped"])
        self.assertTrue(any("UNKNOWN_FEATURE" in x for x in result["review"]["concerns"]))
    def test_no_acceptance_needs_review_and_no_tests_ran(self):
        result=self.plan(query="模型列表")
        self.assertNotIn("execution",result)
        self.assertTrue(any("ACCEPTANCE_UNDEFINED" in x for x in result["review"]["concerns"]))
    def test_blank_scope_rejected(self):
        with self.assertRaises(ValueError):self.plan()
    def test_no_hidden_runner(self):
        (self.root/"scripts/verify-yanzi-ui-hidden.ps1").unlink()
        result=self.plan(query="模型列表")
        self.assertIsNone(result["validation"]["hiddenUiSmoke"]["command"])
        with mock.patch.object(preflight,"run_checked") as fn:
            checks=preflight.run_opt_in_tests(self.root,result,targeted=False,hidden_ui=True)
            self.assertEqual(checks["hiddenUiSmoke"]["status"],"not-applicable")
            fn.assert_not_called()
    def test_runner_failure_propagates(self):
        completed=mock.Mock(returncode=12,stdout="failed",stderr="err")
        with mock.patch.object(preflight.subprocess,"run",return_value=completed):
            result=preflight.run_checked(self.root,["dotnet","run"],"x",30)
        self.assertEqual(result["status"],"failed")
        self.assertEqual(result["exitCode"],12)

if __name__=="__main__":
    unittest.main(verbosity=2)
