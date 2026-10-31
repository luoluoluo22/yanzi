"""Offline tests for source routing, incremental invalidation and conservative impact."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

MODULE_PATH = Path(__file__).with_name("navigator.py")
spec = importlib.util.spec_from_file_location("yanzi_navigator", MODULE_PATH)
assert spec and spec.loader
nav = importlib.util.module_from_spec(spec)
spec.loader.exec_module(nav)

class NavigatorTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="yanzi-nav-test-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.cache = self.root / "outside-cache" / "index.json"
        (self.root / "src" / "Host").mkdir(parents=True)
        (self.root / "tests" / "ModelVerification").mkdir(parents=True)
        (self.root / "tools" / "dev-navigator").mkdir(parents=True)
        (self.root / "src" / "Host" / "Main.xaml").write_text(
            '<Window xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">'
            '<Button x:Name="AiModels" Click="OpenModels_Click" ToolTip="选择模型" />'
            '</Window>', encoding="utf-8")
        (self.root / "src" / "Host" / "Main.cs").write_text(
            'internal class Main {\n    private void OpenModels_Click(object sender) {}\n}\n',
            encoding="utf-8")
        (self.root / "tests" / "ModelVerification" / "ModelVerification.csproj").write_text(
            '<Project/>', encoding="utf-8")
        self.features = [{"id":"models","title":"模型选择","aliases":["模型列表","切换模型"],
                          "paths":["src/Host/Main.cs","src/Host/Main.xaml"],
                          "handlers":["OpenModels_Click"],
                          "tests":["tests/ModelVerification/*.csproj"]}]
        self.write_features()
        self.run_git("init", "-q")
        self.run_git("config", "user.email", "test@example.invalid")
        self.run_git("config", "user.name", "Test")
        self.run_git("add", ".")
        self.run_git("commit", "-qm", "initial")
    def run_git(self, *args):
        subprocess.run(["git", "-C", str(self.root), *args], check=True,
                       capture_output=True)
    def write_features(self):
        (self.root/"tools"/"dev-navigator"/"features.json").write_text(
            json.dumps({"features":self.features}, ensure_ascii=False), encoding="utf-8")
    def test_index_and_incremental_invalidation(self):
        first = nav.build_index(self.root, cache_file=self.cache)
        self.assertEqual(first["stats"]["scanned"], 3)
        second = nav.build_index(self.root, cache_file=self.cache)
        self.assertEqual(second["stats"]["scanned"], 0)
        self.assertEqual(second["stats"]["reused"], 3)
        path = self.root/"src"/"Host"/"Main.xaml"
        path.write_text(path.read_text(encoding="utf-8").replace("选择模型","模型菜单可滚动"),
                        encoding="utf-8")
        third = nav.build_index(self.root, cache_file=self.cache)
        self.assertEqual(third["stats"]["scanned"], 1)
        self.assertIn("模型菜单可滚动", str(third["files"]["src/Host/Main.xaml"]["ui"]))
    def test_chinese_feature_and_handler_mapping(self):
        index = nav.build_index(self.root, cache_file=self.cache)
        found = nav.search_features("模型列表", index, self.features)
        self.assertEqual(found[0]["id"], "models")
        self.assertEqual(len(found[0]["files"]), 2)
        self.assertEqual(found[0]["tests"], ["tests/ModelVerification/ModelVerification.csproj"])
        ui = nav.ui_lookup("选择模型", index)
        self.assertEqual(ui[0]["name"], "AiModels")
        self.assertEqual(ui[0]["definitions"][0]["path"], "src/Host/Main.cs")
        self.assertEqual(ui[0]["definitions"][0]["line"], 2)
    def test_unknown_feature_falls_back_to_grounded_xaml(self):
        index = nav.build_index(self.root, cache_file=self.cache)
        result = nav.search_features("选择模型", index, [], limit=5)
        self.assertEqual(result[0]["id"], "ui-evidence")
        self.assertEqual(result[0]["evidence"]["path"], "src/Host/Main.xaml")
        self.assertEqual(result[0]["tests"], [])

    def test_impact_and_unmapped(self):
        index = nav.build_index(self.root, cache_file=self.cache)
        result = nav.impact(self.root, ["src/Host/Main.cs", "src/Host/Unknown.cs"],index,self.features)
        self.assertEqual(result["affected"][0]["id"], "models")
        self.assertEqual(result["unmapped"], ["src/Host/Unknown.cs"])
        self.assertEqual(len(result["suggestedTests"]),1)
    def test_git_diff_changed_files(self):
        (self.root/"src"/"Host"/"Main.cs").write_text("public class Updated {}\n",
                                                     encoding="utf-8")
        self.run_git("add",".")
        self.run_git("commit","-qm","update")
        self.assertEqual(nav.changed_paths(self.root,"HEAD~1"), ["src/Host/Main.cs"])
    def test_no_unknown_mapping_and_safe_paths(self):
        index = nav.build_index(self.root, cache_file=self.cache)
        self.assertFalse(nav.search_features("完全陌生功能",index,self.features))
        self.assertEqual(nav.ui_lookup("不存在按钮",index),[])
        self.assertEqual(nav.normalize_paths(self.root, ["../outside.txt","src\\Host\\Main.cs"]),
                         ["src/Host/Main.cs"])
    def test_cache_corruption_recovery(self):
        self.cache.parent.mkdir(parents=True)
        self.cache.write_text("{broken json",encoding="utf-8")
        index = nav.build_index(self.root,cache_file=self.cache)
        self.assertEqual(index["stats"]["scanned"],3)

if __name__ == "__main__":
    unittest.main(verbosity=2)
