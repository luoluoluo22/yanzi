"""Contracts for the single-call source/navigation/QA batch runner."""
from __future__ import annotations

from pathlib import Path
from unittest import TestCase, mock
import task_batch


class BatchTests(TestCase):
    def setUp(self):
        self.root = Path(__file__).resolve().parents[2]
        self.index = {"stats": {"files": 3, "reused": 1}}
        self.features = [{"id": "ai-model-menu", "title": "模型菜单"}]
        self.diagnosis = {
            "status": "triage-candidate-not-confirmed",
            "signals": ["MODEL_LIST"],
            "features": [{"id": "ai-model-menu"}],
            "recentChanges": [{"commit": "abc123"}],
            "nextChecks": ["Verify scroll behavior"],
        }
        self.plan = {
            "source": {"mappedFeatureIds": ["ai-model-menu"],
                       "candidateFiles": ["src/OpenQuickHost/MainWindow.AiChat.cs"]},
            "review": {"status": "needs-review", "concerns": ["UI_COVERAGE_GAP"]},
            "validation": {"tests": [{"path": "tests/ModelMenuScrollVerification/ModelMenuScrollVerification.csproj",
                                       "execution": "safe-targeted"}],
                           "hiddenUiSmoke": {"recommended": True, "scenario": "ai-chat-model"}},
        }
        self.mockers = [
            mock.patch.object(task_batch.knowledge, "revision",
                              return_value={"commit": "a" * 40, "dirty": False}),
            mock.patch.object(task_batch.navigator, "build_index", return_value=self.index),
            mock.patch.object(task_batch.navigator, "features_from", return_value=self.features),
            mock.patch.object(task_batch.diagnose, "analyze", return_value=self.diagnosis),
            mock.patch.object(task_batch.preflight, "make_plan", return_value=self.plan),
            mock.patch.object(task_batch.knowledge, "qa_status", return_value={
                "ai-model-menu": {"isolatedChecksCurrent": False}}),
        ]
        self.patched = [p.start() for p in self.mockers]
        for p in self.mockers:
            self.addCleanup(p.stop)

    def test_parallel_two_stages_share_one_source_index(self):
        result = task_batch.orchestrate(self.root, query="模型列表不能滚动",
                                         accept=["50 models scroll"])
        self.assertEqual(result["status"], "candidate-plan-ready")
        self.patched[1].assert_called_once_with(self.root)
        self.patched[2].assert_called_once_with(self.root)
        self.assertEqual(result["preflight"]["featureIds"], ["ai-model-menu"])
        self.assertEqual(result["diagnosis"]["featureIds"], ["ai-model-menu"])
        self.assertTrue(result["metrics"]["sourceIndexBuiltOnce"])
        self.assertEqual(result["metrics"]["localStagesFailed"], 0)

    def test_default_does_not_execute_tests_or_record_ledger(self):
        with mock.patch.object(task_batch.preflight, "run_opt_in_tests") as run:
            with mock.patch.object(task_batch.knowledge, "record_executed") as ledger:
                result = task_batch.orchestrate(self.root, query="model menu")
                run.assert_not_called()
                ledger.assert_not_called()
        self.assertEqual(result["validation"]["status"], "not-requested")

    def test_explicit_safe_tests_and_ledger_record(self):
        execution = {"targeted": [{"name": "model", "status": "passed",
                                   "elapsedSeconds": 2.5, "exitCode": 0}],
                     "hiddenUiSmoke": {"name": "hidden UI", "status": "passed",
                                       "elapsedSeconds": 0.4, "exitCode": 0}}
        with mock.patch.object(task_batch.preflight, "run_opt_in_tests",
                               return_value=execution) as runner:
            with mock.patch.object(task_batch.knowledge, "record_executed",
                                   return_value=[{"record": 1}, {"record": 2}]) as ledger:
                result = task_batch.orchestrate(
                    self.root, query="model menu", run_targeted=True, run_hidden_ui=True)
        self.assertEqual(result["validation"]["status"], "passed")
        self.assertEqual(result["validation"]["recorded"], 2)
        self.assertEqual(len(result["validation"]["results"]), 2)
        self.assertTrue(runner.called)
        self.assertTrue(ledger.called)

    def test_test_failure_returns_failure_with_code_not_log_text(self):
        secret = "sk-" + "ABCSECRET" * 6
        execution = {"targeted": [{"name": "model", "status": "failed", "exitCode": 7,
                                  "outputTail": "PRIVATE=" + secret}],
                     "hiddenUiSmoke": None}
        with mock.patch.object(task_batch.preflight, "run_opt_in_tests",
                               return_value=execution):
            with mock.patch.object(task_batch.knowledge, "record_executed",
                                   return_value=[{"status": "failed"}]):
                result = task_batch.orchestrate(self.root, query="model",
                                                 run_targeted=True)
        self.assertEqual(result["status"], "failed-tests")
        self.assertEqual(result["validation"]["results"][0]["exitCode"], 7)
        self.assertNotIn(secret, str(result))

    def test_exception_is_fail_closed_without_secrets(self):
        password = "super-secret-and-sent-by-test"
        self.patched[4].side_effect = RuntimeError(password)
        result = task_batch.orchestrate(self.root, query="model")
        self.assertEqual(result["status"], "stage-failed")
        self.assertEqual(result["metrics"]["localStagesFailed"], 1)
        self.assertNotIn(password, str(result))
        self.assertEqual(result["failureReason"], "stage-runtime-error")

    def test_parallel_and_sequential_have_same_mapping(self):
        parallel = task_batch.orchestrate(self.root, query="model", parallel=True)
        sequential = task_batch.orchestrate(self.root, query="model", parallel=False)
        self.assertEqual(parallel["preflight"], sequential["preflight"])
        self.assertEqual(parallel["diagnosis"], sequential["diagnosis"])
        self.assertEqual(parallel["metrics"]["parallelReadOnlyStages"], True)
        self.assertEqual(sequential["metrics"]["parallelReadOnlyStages"], False)

    def test_raw_input_not_in_safe_report(self):
        secret = "password-KEEP-OUT-very-specific"
        result = task_batch.orchestrate(
            self.root, query="模型列表不能滚动 " + secret,
            accept=["read private key " + secret])
        value = task_batch.safe_output(result)
        self.assertNotIn(secret, str(value))
        self.assertFalse(value["boundaries"]["remoteMcpInvocationCountObserved"])
        self.assertIsNone(value["metrics"]["actualRemoteMcpCallCount"])

    def test_missing_query_rejected_and_limits(self):
        for value in ["", " "]:
            with self.subTest(value=value):
                with self.assertRaises(ValueError):
                    task_batch.orchestrate(self.root, query=value)
        with self.assertRaises(ValueError):
            task_batch.orchestrate(self.root, query="a" * 801)

    def test_incomplete_stage_prevents_success(self):
        self.patched[1].side_effect = OSError("secret in path")
        result = task_batch.orchestrate(self.root, query="model")
        self.assertEqual(result["status"], "stage-failed")
        self.assertEqual(result["failureReason"], "resource-unavailable")
        self.assertEqual(len(result["stages"]), 2)
        self.assertNotIn("secret in path", str(result))
