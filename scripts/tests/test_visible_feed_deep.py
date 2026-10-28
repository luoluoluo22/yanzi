"""Synthetic tests for deep feed stats: missing UI must not be treated as no content."""
from pathlib import Path
import importlib.util
import json
import tempfile
import unittest

p=Path(__file__).resolve().parents[1]/"yanzi-visible-feed-deep-analyze.py"
spec=importlib.util.spec_from_file_location("deep_analyzer",p)
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

class DeepStudyTests(unittest.TestCase):
    def test_ui_missing_is_unknown_not_zero(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder=Path(tmp)/"visible-feed-deep-douyin-test"
            folder.mkdir()
            (folder/"page-1.png").write_bytes(b"\x89PNG synthetic")
            (folder/"summary.json").write_text(json.dumps({
                "app":"douyin","package":"com.ss.android.ugc.aweme",
                "pages":[{"page_number":1,"screenshot_file":"page-1.png",
                          "ui_dump_success":False,"node_count":None,
                          "candidates":[]}]},ensure_ascii=False),encoding="utf-8")
            got=module.analyze_folder(folder)
            self.assertEqual(got["ui_unavailable_pages"],1)
            self.assertIsNone(got["card_appearances"])
            self.assertIsNone(got["different_visible_cards_by_title_and_author"])
    def test_known_field_completeness(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder=Path(tmp)/"visible-feed-deep-xiaohongshu-test"
            folder.mkdir()
            (folder/"page-1.png").write_bytes(b"\x89PNG synthetic")
            (folder/"summary.json").write_text(json.dumps({
                "app":"xiaohongshu","package":"com.xingin.xhs",
                "pages":[{"page_number":1,"screenshot_file":"page-1.png",
                          "ui_dump_success":True,"node_count":10,
                          "new_candidate_cards":2,"candidates":[]}]},
                ensure_ascii=False),encoding="utf-8")
            (folder/"structured-cards.json").write_text(json.dumps({
                "pages":[[{"stable_key":"a|1","title":"a","author":"1","likes":"2"},
                          {"stable_key":"b|2","title":"b","author":"2","likes":None},
                          {"stable_key":"a|1","title":"a","author":"1","likes":"2"}]]
            }),encoding="utf-8")
            got=module.analyze_folder(folder)
            self.assertEqual(got["card_appearances"],3)
            self.assertEqual(got["different_visible_cards_by_title_and_author"],2)
            self.assertEqual(got["duplicate_card_appearances"],1)
            self.assertEqual(got["field_presence_among_extracted_card_appearances"]["likes"]["observed"],2)

if __name__=="__main__":
    unittest.main()
