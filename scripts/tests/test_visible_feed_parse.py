"""No personal feed content: deterministic tests of public visible-card parser."""
import importlib.util
from pathlib import Path
import unittest

source = Path(__file__).resolve().parents[1] / "yanzi-visible-feed-parse.py"
spec = importlib.util.spec_from_file_location("visible_feed_parse", source)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

def candidate(label, kind="description", y=900):
    return {"label":label,"kind":kind,"region":[0,y,800,y+45]}

class PublicVisibleCardParserTests(unittest.TestCase):
    def test_bilibili_card_and_non_card(self):
        items=[
            candidate("直播,7之1,标签"),
            candidate("视频,一条公开的测试视频,1.2万观看,7弹幕,时长3分钟10秒,UP主测试作者,")
        ]
        out=module.bilibili(items)
        self.assertEqual(len(out),1)
        self.assertEqual(out[0]["author"],"测试作者")
        self.assertEqual(out[0]["views"],"1.2万")
        self.assertEqual(out[0]["duration"],"3分钟10秒")
    def test_xiaohongshu_card(self):
        items=[candidate("笔记 一篇公开的测试笔记 来自测试作者 19赞"),
               candidate("消息，6条未读")]
        out=module.xiaohongshu(items)
        self.assertEqual(len(out),1)
        self.assertEqual(out[0]["title"],"一篇公开的测试笔记")
        self.assertEqual(out[0]["likes"],"19赞")
    def test_douyin_metrics(self):
        items=[
            candidate("未点赞，喜欢3.0万，按钮",y=1350),
            candidate("未选中，收藏820，按钮",y=1700),
            candidate("分享1.0万，按钮",y=1890),
            candidate("@测试作者",kind="text",y=2000),
            candidate("这是公开内容的测试文案 #测试",kind="text",y=2100)
        ]
        out=module.douyin(items)
        self.assertEqual(len(out),1)
        self.assertEqual(out[0]["likes"],"3.0万")
        self.assertEqual(out[0]["favorites"],"820")
        self.assertEqual(out[0]["shares"],"1.0万")
    def test_two_page_deduplication(self):
        shared=candidate("笔记 这是一条公开的示例记录 来自创作者 5赞")
        added=candidate("笔记 第二条公开的示例记录 来自创作者 8赞")
        data={"app":"xiaohongshu","package":"com.xingin.xhs",
              "pages":[{"candidates":[shared]},{"candidates":[shared,added]}]}
        out=module.collect(data)
        self.assertEqual(out["unique_structured_cards"],2)
        self.assertEqual(out["two_page_card_counts"][1]["new_vs_previous"],1)
        self.assertEqual(out["two_page_card_counts"][1]["repeated_vs_previous"],1)

if __name__=="__main__":unittest.main()
