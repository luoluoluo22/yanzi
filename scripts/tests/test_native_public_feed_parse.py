"""Synthetic tests for native accessibility snapshot -> existing public card parser."""
import importlib.util
from pathlib import Path
import unittest

base=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location("native_feed",base/"yanzi-native-public-feed-parse.py")
m=importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

class NativePublicFeedTests(unittest.TestCase):
    def test_package_scope(self):
        for name in ("tv.danmaku.bili","com.xingin.xhs","com.ss.android.ugc.aweme"):
            app,rows=m.convert({"packageName":name,"nodes":[]})
            self.assertTrue(app)
            self.assertEqual(rows,[])
        with self.assertRaises(ValueError):
            m.convert({"packageName":"com.tencent.mm","nodes":[]})
    def test_adapt_text_and_description(self):
        app,rows=m.convert({"packageName":"tv.danmaku.bili",
                           "nodes":[{"bounds":[1,2,3,4],"text":"公开测试标题",
                                     "contentDescription":"视频,公开测试标题,2观看,UP主作者,",
                                     "resourceId":"some:id"}]})
        self.assertEqual(app,"bilibili")
        self.assertEqual(len(rows),2)
        self.assertEqual(rows[0]["kind"],"text")
        self.assertEqual(rows[1]["kind"],"description")
    def test_field_summary_denominator(self):
        result=m.summarize([{"title":"public","author":"u","likes":None},
                            {"title_or_caption":"public video","author":"v","likes":"2"}])
        self.assertEqual(result["card_count"],2)
        self.assertEqual(result["title_nonempty"],2)
        self.assertEqual(result["likes_nonempty"],1)

if __name__=="__main__":
    unittest.main()
