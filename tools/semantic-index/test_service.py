#!/usr/bin/env python3
import json
import sys
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else "http://127.0.0.1:53931"
query = sys.argv[2] if len(sys.argv) > 2 else "红色玫瑰花的三视图"
token = sys.argv[3] if len(sys.argv) > 3 else ""

req = urllib.request.Request(
    base + "/search",
    data=json.dumps({"query": query, "topK": 5}).encode("utf-8"),
    headers={"Content-Type": "application/json", "X-Yanzi-Semantic-Token": token},
    method="POST",
)
with urllib.request.urlopen(req, timeout=120) as resp:
    print(resp.read().decode("utf-8"))
