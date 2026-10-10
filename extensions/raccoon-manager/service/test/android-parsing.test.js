import test from "node:test";
import assert from "node:assert/strict";
import { parseUiAutomatorXml, parseAndroidPackageSnapshot } from "../src/adb-tools.js";

test("uiautomator XML becomes structured nodes", () => {
  const xml = `<?xml version="1.0"?><hierarchy><node text="燕子" resource-id="com.example:id/title" class="android.widget.TextView" package="com.example.dev" content-desc="status" clickable="true" enabled="true" bounds="[10,20][110,70]" /></hierarchy>`;
  const nodes = parseUiAutomatorXml(xml);
  assert.equal(nodes.length, 1);
  assert.deepEqual(nodes[0].bounds, { left: 10, top: 20, right: 110, bottom: 70 });
  assert.equal(nodes[0].text, "燕子");
  assert.equal(nodes[0].resourceId, "com.example:id/title");
  assert.equal(nodes[0].clickable, true);
});

test("Android package snapshot extracts version PID activity and relevant crash", () => {
  const snapshot = parseAndroidPackageSnapshot(
    "com.example.dev",
    "Package [com.example.dev]\n versionCode=42 minSdk=28\n versionName=1.2.3\n",
    "mResumedActivity: ActivityRecord{abc u0 com.example.dev/.MainActivity t1}",
    "12345\n",
    "FATAL EXCEPTION: main\nProcess: com.example.dev, PID: 12345\n"
  );
  assert.equal(snapshot.installed, true);
  assert.equal(snapshot.versionCode, 42);
  assert.equal(snapshot.versionName, "1.2.3");
  assert.equal(snapshot.pid, 12345);
  assert.equal(snapshot.resumedActivity, "com.example.dev/.MainActivity");
  assert.equal(snapshot.hasCrash, true);
});
