"""Unit checks for fail-closed split CI routing."""
from __future__ import annotations
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location(
    "ci_classify", Path(__file__).with_name("classify-ci-change.py"))
assert spec and spec.loader
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)

class TierTests(unittest.TestCase):
    def test_only_developer_tools_qualify(self):
        self.assertEqual(ci.classify([
            "tools/dev-navigator/diagnose.py", "scripts/test-dev-diagnose.ps1",
            "docs/dev-diagnostics.md"])[0], "dev-tools")
    def test_protected_files_force_full(self):
        protected = [
            "AGENTS.md", ".github/workflows/engineering-checks.yml",
            "scripts/classify-ci-change.py", "scripts/test-ci-change-classifier.py",
            "scripts/finish-agent-task.ps1",
            "src/OpenQuickHost/MainWindow.AiChat.cs",
            "cloudflare/src/index.mjs", "mobile/android/app/build.gradle",
            "sdk/javascript/index.js", "browser-extension/background.js",
            "tools/idle-chatgpt-tasks/README.md",
            ".env", "docs/dev-diagnostics.md/../unknown",
        ]
        for path in protected:
            with self.subTest(path=path):
                self.assertEqual(ci.classify(["tools/dev-navigator/readme.md",path])[0], "full")
    def test_path_evasion_rejected(self):
        for p in ["../tools/dev-navigator/x.py", "tools/dev-navigator/../x.py",
                  "tools\\dev-navigator\\safe.py", "/tools/dev-navigator/x.py",
                  "tools/dev-navigator//x.py", "tools/dev-navigator/x.ps1"]:
            with self.subTest(p=p):
                self.assertFalse(ci.allowed(p))
    def test_empty_and_large_diff_full(self):
        self.assertEqual(ci.classify([])[0],"full")
        self.assertEqual(ci.classify(["tools/dev-navigator/test.py"] * 501)[0],"full")
    def test_rename_outside_developer_scope_forces_full(self):
        self.assertEqual(ci.classify(["tools/dev-navigator/foo.py","src/OpenQuickHost/foo.cs"])[0],"full")
    def test_diff_detection_in_real_temporary_git_repository(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            def run(*args):
                return subprocess.run(["git","-C",str(root),*args],capture_output=True,check=True).stdout.decode().strip()
            run("init","-q")
            run("config","user.name","QA")
            run("config","user.email","qa@example.invalid")
            (root/"tools"/"dev-navigator").mkdir(parents=True)
            (root/"tools"/"dev-navigator"/"index.py").write_text("before",encoding="utf-8")
            run("add",".");run("commit","-qm","init")
            base=run("rev-parse","HEAD")
            (root/"tools"/"dev-navigator"/"index.py").write_text("after",encoding="utf-8")
            run("add",".");run("commit","-qm","dev only")
            head=run("rev-parse","HEAD")
            self.assertEqual(ci.classify(ci.changed_paths(root,base,head))[0],"dev-tools")
            (root/"src").mkdir()
            (root/"src"/"main.cs").write_text("test",encoding="utf-8")
            run("add",".");run("commit","-qm","runtime")
            self.assertEqual(ci.classify(ci.changed_paths(root,head,run("rev-parse","HEAD")))[0],"full")
    def test_invalid_revision_pair_raises(self):
        with self.assertRaises(ValueError):
            ci.changed_paths(Path("."),"../../foo","deadbeef")
    def test_deletion_still_classifies(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            def run(*args):
                return subprocess.run(["git","-C",str(root),*args],capture_output=True,check=True).stdout.decode().strip()
            run("init","-q");run("config","user.email","qa@example.invalid");run("config","user.name","QA")
            (root/"src").mkdir()
            (root/"src"/"file.cs").write_text("runtime",encoding="utf-8")
            run("add",".");run("commit","-qm","first")
            old=run("rev-parse","HEAD")
            (root/"src"/"file.cs").unlink()
            run("add","-A");run("commit","-qm","delete runtime")
            self.assertEqual(ci.classify(ci.changed_paths(root,old,run("rev-parse","HEAD")))[0],"full")

    def test_workflow_retains_required_checks_and_guards_all_steps(self):
        workflow = (Path(__file__).resolve().parents[1] /
                    ".github" / "workflows" / "engineering-checks.yml").read_text(
                        encoding="utf-8-sig").replace("\r\n", "\n")
        self.assertIn("  classify:\n", workflow)
        self.assertIn("  portable-contracts:\n", workflow)
        self.assertIn("  desktop-and-worker:\n", workflow)
        self.assertIn('"tools/dev-navigator/**"', workflow)
        self.assertIn("runs-on: $" + "{{ needs.classify.outputs.tier == 'dev-tools' && "
                      "'ubuntu-latest' || 'windows-latest' }}", workflow)
        for job, end in [("portable-contracts", "desktop-and-worker"),
                         ("desktop-and-worker", None)]:
            start = workflow.index("  " + job + ":\n")
            stop = workflow.index("  " + end + ":\n", start) if end else len(workflow)
            chunk = workflow[start:stop]
            self.assertIn("needs: classify", chunk)
            parts = chunk.split("\n      - ")
            self.assertGreaterEqual(len(parts), 5)
            for part in parts[1:]:
                first = part.splitlines()[0]
                if first == "uses: actions/checkout@v4":
                    continue
                self.assertRegex(part, r"(?m)^        if: needs\.classify\.outputs\.tier == '(?:full|dev-tools)'$",
                                 msg=job + ": unguarded " + first)
        self.assertIn("python3 scripts/test-ci-change-classifier.py", workflow)
        self.assertIn("python3 -m unittest discover -s tools/dev-navigator", workflow)

if __name__ == "__main__":
    unittest.main(verbosity=2)
