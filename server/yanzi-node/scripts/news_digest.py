#!/usr/bin/env python3
"""燕子新闻早报：可靠的 RSS 采集、去重、AI 摘要与独立云端消息推送。"""
import argparse
import concurrent.futures
import datetime as dt
import email.utils
import fcntl
import hashlib
import html
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import urllib.request
import xml.etree.ElementTree as ET
from zoneinfo import ZoneInfo

TZ = ZoneInfo("Asia/Shanghai")
FEEDS = (
    ("国内", "中新社·国内", "https://www.chinanews.com.cn/rss/china.xml"),
    ("国际", "中新社·国际", "https://www.chinanews.com.cn/rss/world.xml"),
    ("国际", "BBC World", "https://feeds.bbci.co.uk/news/world/rss.xml"),
    ("科技", "BBC Technology", "https://feeds.bbci.co.uk/news/technology/rss.xml"),
    ("财经", "中新社·财经", "https://www.chinanews.com.cn/rss/finance.xml"),
)
STATE = Path(os.environ.get("YANZI_NEWS_STATE_DIR", "/var/lib/yanzi-news"))
GATEWAY_TOKEN = Path("/etc/chatgpt-gateway/token")
SENDER = "/opt/yanzi/server/yanzi-node/scripts/send-message.sh"

def clean(s):
    s = re.sub(r"<[^>]*>", " ", html.unescape(str(s or "")))
    return re.sub(r"\s+", " ", s).strip()

def fetch(feed, now):
    category, name, url = feed
    req = urllib.request.Request(url, headers={"User-Agent": "YanziNewsDigest/1.0 (personal RSS reader)", "Accept": "application/rss+xml,application/xml,text/xml"})
    with urllib.request.urlopen(req, timeout=14) as resp:
        if resp.status != 200:
            raise RuntimeError("http_" + str(resp.status))
        data = resp.read(450001)
    if len(data) > 450000:
        raise ValueError("feed_too_large")
    root = ET.fromstring(data)
    found = []
    for item in root.findall(".//item")[:75]:
        title, link = clean(item.findtext("title")), clean(item.findtext("link"))
        date = clean(item.findtext("pubDate")) or clean(item.findtext("{http://purl.org/dc/elements/1.1/}date"))
        if not title or not link.startswith("https://") or not date:
            continue
        try:
            when = email.utils.parsedate_to_datetime(date)
            if when is None:
                when = dt.datetime.fromisoformat(date.replace("Z", "+00:00"))
            if not when.tzinfo:
                continue
            age = (now - when.astimezone(TZ)).total_seconds()
            if age < -600 or age > 36 * 3600:
                continue
        except (TypeError, ValueError, OverflowError):
            continue
        found.append({"category": category, "source": name, "title": title[:145], "url": link[:450],
                      "summary": clean(item.findtext("description"))[:230], "published": when.isoformat()})
    return found

def select_news(now):
    gathered, failures = [], []
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as pool:
        future_map = {pool.submit(fetch, feed, now): feed for feed in FEEDS}
        for future in concurrent.futures.as_completed(future_map):
            try:
                gathered.extend(future.result())
            except Exception as error:
                failures.append({"source": future_map[future][1], "error": type(error).__name__})
    gathered.sort(key=lambda x: x["published"], reverse=True)
    seen, groups = set(), {}
    for item in gathered:
        key = re.sub(r"\W+", "", item["title"]).lower()
        if key in seen or not key:
            continue
        seen.add(key)
        groups.setdefault(item["category"], []).append(item)
    counts = {"国内": 4, "国际": 2, "科技": 2, "财经": 1}
    chosen = []
    for category, maximum in counts.items():
        chosen.extend(groups.get(category, [])[:maximum])
    chosen.sort(key=lambda item: (list(counts).index(item["category"]), -dt.datetime.fromisoformat(item["published"]).timestamp()))
    return chosen[:9], failures

def summarize(news):
    """AI can summarize only the feed title and snippet; links are appended unmodified below."""
    if not GATEWAY_TOKEN.is_file():
        return None, "gateway_token_missing"
    feed_input = [{"index": i+1, "category": x["category"], "title": x["title"],
                   "source": x["source"], "description": x["summary"]} for i, x in enumerate(news)]
    body = {"model": "gpt-6-sol",
            "instructions": "你是严谨的中文新闻编辑。只能使用用户提供的标题和摘要，不得补充未提供的事实，不得声称核实过原文。按编号写简短导读，每条最多45字，说明已知事实；因果关系没有证据就不要推断。输出纯文本，禁止凭空加入人物、日期、数字或链接。",
            "input": "请整理这份过去36小时的RSS新闻标题和摘要，每条一行，保留编号：\n" + json.dumps(feed_input, ensure_ascii=False),
            "reasoning_effort": "low", "verbosity": "low"}
    req = urllib.request.Request("http://127.0.0.1:8791/v1/chat",
                                 data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
                                 headers={"Authorization": "Bearer " + GATEWAY_TOKEN.read_text().strip(), "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=65) as response:
            result = json.load(response)
        text = clean(result.get("text", ""))
        # Restrict generation length and fall back to quoted RSS headlines if synthesis is malformed.
        if not result.get("ok") or len(text) < 50 or len(text) > 1700:
            return None, "gateway_output_invalid"
        return text[:1050], None
    except Exception as error:
        return None, type(error).__name__

def credential_notice(now):
    """A device grant lasts at most 30 days; alert before it expires."""
    path = Path("/etc/yanzi-server-node.env")
    try:
        line = next(x for x in path.read_text().splitlines()
                    if x.startswith("YANZI_DEVICE_CREDENTIAL_EXPIRES_AT="))
        expiry = int(line.partition("=")[2])
        remaining = expiry - now.timestamp()
        if 0 < remaining <= 7 * 86400:
            expires = dt.datetime.fromtimestamp(expiry, TZ).strftime("%m月%d日 %H:%M")
            return f"⚠️ 服务器消息授权将在{expires}到期，请在电脑上重新执行授权安装脚本。"
    except (OSError, ValueError, StopIteration):
        pass
    return ""

def compose(now, items, ai):
    lines = ["📰 燕子每日新闻 · " + now.strftime("%Y-%m-%d") + "（北京时间）",
             "来源：RSS公开标题与简介；点击原文核实。"]
    if ai:
        lines += ["", "AI 导读（仅依据来源摘要）", ai]
    else:
        lines += ["", "今日重点"]
        for i, entry in enumerate(items, 1):
            lines.append(f"{i}. [{entry['category']}] {entry['title']}")
    lines += ["", "原文链接"]
    for i, entry in enumerate(items, 1):
        lines.append(f"{i}. {entry['source']}｜{entry['title']}\n{entry['url']}")
    text = "\n".join(lines)
    if len(text) > 3750:
        # Never cut the end of a URL. Drop sources from the tail and rebuild.
        return compose(now, items[:-1], ai if len(items) > 5 else None)
    return text

def save_atomic(path, state):
    fd, tmp = tempfile.mkstemp(prefix="news-", dir=path.parent)
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as out:
            json.dump(state, out, ensure_ascii=False, indent=2)
            out.write("\n")
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--send", action="store_true")
    parser.add_argument("--test", action="store_true", help="Use a test idempotency key (not today's scheduled key)")
    args = parser.parse_args()
    now = dt.datetime.now(TZ)
    STATE.mkdir(parents=True, exist_ok=True, mode=0o700)
    os.chmod(STATE, 0o700)
    with open(STATE / "run.lock", "a+") as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        key = now.strftime("%Y%m%d") + ("-test" if args.test else "")
        state_file = STATE / (key + ".json")
        state = json.loads(state_file.read_text()) if state_file.exists() else {}
        if state.get("messageId") and args.send:
            print(json.dumps({"ok": True, "alreadySent": True, "messageId": state["messageId"], "date": key}))
            return
        if "message" not in state or not args.send:
            news, failures = select_news(now)
            if len(news) < 3:
                raise RuntimeError("fresh_news_insufficient_" + str(len(news)))
            ai, ai_error = summarize(news)
            message = compose(now, news, ai)
            warning = credential_notice(now)
            if warning:
                message += "\n\n" + warning
            if args.test:
                message = "【新闻链路测试】\n" + message
            if args.send:
                state = {"date": key, "message": message, "items": len(news),
                         "sources": sorted(set(x["source"] for x in news)), "aiStatus": "ok" if ai else ai_error,
                         "preparedAt": now.isoformat(), "clientMessageId": "yanzi-news-" + key}
                save_atomic(state_file, state)
            else:
                print(json.dumps({"ok": True, "dryRun": True, "items": len(news),
                                  "sources": sorted(set(x["source"] for x in news)),
                                  "aiStatus": "ok" if ai else ai_error, "feedErrors": failures,
                                  "messagePreview": message[:2500]}, ensure_ascii=False))
                return
        env = os.environ.copy()
        env["YANZI_MESSAGE_CLIENT_ID"] = state["clientMessageId"]
        send = subprocess.run(["bash", SENDER], input=state["message"], text=True,
                              capture_output=True, env=env, timeout=35)
        if send.returncode:
            raise RuntimeError("message_send_failed: " + send.stderr[-180:])
        result = json.loads(send.stdout)
        if not result.get("messageId"):
            raise RuntimeError("message_response_missing_id")
        state.update({"messageId": result["messageId"], "cloudStatus": result.get("cloudStatus"),
                      "sentAt": dt.datetime.now(TZ).isoformat()})
        save_atomic(state_file, state)
        print(json.dumps({"ok": True, "date": key, "messageId": state["messageId"],
                          "cloudStatus": state["cloudStatus"], "items": state["items"], "aiStatus": state["aiStatus"]}))

if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(json.dumps({"ok": False, "error": str(error)[:240]}, ensure_ascii=False))
        raise SystemExit(1)
