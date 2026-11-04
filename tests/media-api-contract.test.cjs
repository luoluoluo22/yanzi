const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");

const code = fs.readFileSync(path.join(__dirname, "..", "browser-extension", "media-api.js"), "utf8");
const ID = "hbbaogocjdkfbffpcoiaacdjioamjfef";

function setup() {
  let listener, calls = [], tabCalls = [];
  const ctx = {
    console, URL, setTimeout, clearTimeout,
    chrome: {
      runtime: { id: ID, onMessage: { addListener: fn => { listener = fn; } } },
      scripting: { executeScript: async ({ func, args, world }) => {
        assert.equal(world, "ISOLATED");
        const page = {
          location: { hostname: "www.douyin.com", pathname: "/video/7427381362337778980",
            href: "https://www.douyin.com/video/7427381362337778980" },
          document: {
            title: "", // Ego inactive tabs often have no document.title
            querySelector: selector => {
              const values = {
                '[data-e2e="video-player-digg"]': "25.7万",
                '[data-e2e="feed-comment-icon"]': "3680",
                '[data-e2e="video-player-collect"]': "22.6万",
                '[data-e2e="video-player-share"]': "5.1万",
              };
              if (selector === ".comment-mainContent") return { scrollIntoView() {} };
              if (selector === '[data-e2e="detail-video-info"]') return { innerText: "每周精选内容\nA real video about neural networks\n25.7万\n3680" };
              if (selector === 'meta[name="description"]') return { content: "Test video" };
              return Object.hasOwn(values, selector) ? { innerText: values[selector] } : null;
            },
            querySelectorAll: selector => selector === ".comment-mainContent .comment-item-info-wrap"
              ? [{ innerText: "Test commenter\nA genuinely useful remark" }] : []
          },
          setTimeout, clearTimeout, Date
        };
        const result = await vm.runInNewContext("(" + func.toString() + ")(...args)", {
          ...page, args
        });
        return [{ result }];
      }}
    },
    withEphemeralTab: async (pattern, url, callback, timeout, preferExisting) => {
      tabCalls.push({pattern,url,timeout,preferExisting});
      return callback(17, true);
    },
    yanziBrowserHost: { isConnected: () => true },
    yanziMediaReadHost: async (...args) => {
      calls.push(args);
      return { ok: true, data: {schemaVersion:"1",site:args[0],operation:args[1],
        returned:1,items:[]}};
    }
  };
  vm.createContext(ctx);
  vm.runInContext(code, ctx, { filename: "media-api.js" });
  return { ctx, calls, tabCalls, listener: () => listener };
}

test("only three platforms and three read-only operations are accepted", async () => {
  const { ctx } = setup();
  for (const query of [
    { site: "unknown", operation: "search", value: "AI" },
    { site: "bilibili", operation: "publish", value: "AI" },
    { site: "douyin", operation: "detail", value: "file:///etc/passwd" },
    { site: "douyin", operation: "search", value: "--help" },
  ]) {
    const result = await ctx.yanziMediaRequest({ ...query, limit:5 });
    assert.equal(result.ok, false);
  }
});

test("three-site searches are forwarded to the allowlisted local host", async () => {
  const { ctx, calls } = setup();
  for (const site of ["xiaohongshu","douyin","bilibili"]) {
    const r = await ctx.yanziMediaRequest({site,operation:"search",value:"人工智能",limit:5});
    assert.equal(r.ok,true);
  }
  assert.equal(calls.length,3);
  assert.equal(calls[1][0],"douyin");
  assert.equal(calls[1][1],"search");
  assert.equal(calls[1][2],"人工智能");
});

test("Douyin details are grounded in actual counters and never invent plays", async () => {
  const { ctx, tabCalls } = setup();
  const r = await ctx.yanziMediaRequest({
    site:"douyin",operation:"detail",
    value:"https://www.douyin.com/video/7427381362337778980",limit:5
  });
  assert.equal(r.ok, true);
  const m = r.data.item.metrics;
  assert.equal(m.likes.value,257000);
  assert.equal(m.likes.precision,"rounded_display");
  assert.equal(m.comments.value,3680);
  assert.equal(m.collects.value,226000);
  assert.equal(m.shares.value,51000);
  assert.equal(m.plays.status,"not_provided");
  assert.equal(m.plays.value,null);
  assert.equal(r.data.item.source,"douyin_public_page");
  assert.equal(r.data.item.title,"A real video about neural networks");
  assert.equal(tabCalls.length,1);
});

test("Douyin comment text comes from real page rows without fabricated authors", async () => {
  const { ctx } = setup();
  const r = await ctx.yanziMediaRequest({
    site:"douyin",operation:"comments",
    value:"https://www.douyin.com/video/7427381362337778980",limit:5
  });
  assert.equal(r.ok,true);
  assert.match(r.data.items[0].text,/genuinely useful remark/);
  assert.equal(r.data.items[0].author,null);
  assert.equal(r.data.items[0].likes,null);
});

test("cross-extension messages are rejected", () => {
  const { listener } = setup();
  let res;
  listener()({action:"yanzi_media_api",site:"bilibili",operation:"search",value:"AI"},{
    id:"untrusted-other-extension"}, result => { res=result; });
  assert.equal(res.ok,false);
});
