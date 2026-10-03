"""Destructive upgrade fixture on an explicitly selected emulator; never a phone.

Usage: python scripts/test-android-release-upgrade.py --serial emulator-5554
       --baseline OLD.apk --candidate NEW.apk --output ABSOLUTE_TEMP_DIRECTORY
Uses only synthetic offline credentials. Clears the emulator fixture in finally.
"""
import argparse
import base64
import hashlib
import json
import pathlib
import re
import sqlite3
import subprocess
import time
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True)
    parser.add_argument("--baseline", required=True, type=pathlib.Path)
    parser.add_argument("--candidate", required=True, type=pathlib.Path)
    parser.add_argument("--output", required=True, type=pathlib.Path)
    parser.add_argument("--adb", default="F:/SDK/platform-tools/adb.exe")
    args = parser.parse_args()
    if not re.fullmatch(r"emulator-\d+", args.serial):
        parser.error("This destructive test accepts emulator-* only.")
    if not args.output.is_absolute():
        parser.error("An absolute output directory is required.")
    for apk in (args.baseline, args.candidate):
        if not apk.is_file():
            parser.error(f"APK not found: {apk}")
    args.output.mkdir(parents=True, exist_ok=True)
    package = "cc.luoluoluo.yanzi.mobile"
    private = f"/data/data/{package}"
    checks = []

    def adb(*parts, allow_failure=False):
        p = subprocess.run([args.adb, "-s", args.serial, *map(str, parts)],
                           capture_output=True, timeout=120)
        text = p.stdout.decode("utf-8", "replace") + p.stderr.decode("utf-8", "replace")
        if p.returncode and not allow_failure:
            raise RuntimeError(text)
        return text

    def check(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)
        print("PASS", name, flush=True)

    def install(apk, *flags):
        result = adb("install", *flags, apk, allow_failure=True)
        check("Success" in result, f"Install {apk.name}")

    def launch_chat():
        adb("logcat", "-c")
        adb("shell", "am", "start", "-n", package + "/.MainActivity")
        time.sleep(2)
        for attempt in range(5):
            adb("shell", "uiautomator", "dump", "/sdcard/release-upgrade-ui.xml", allow_failure=True)
            xml = adb("exec-out", "cat", "/sdcard/release-upgrade-ui.xml", allow_failure=True)
            if "<hierarchy" in xml:
                nodes = ET.fromstring(xml[xml.index("<?xml"):] if "<?xml" in xml else xml)
                for node in nodes.iter("node"):
                    if node.get("text") == "聊天":
                        x1, y1, x2, y2 = map(int, re.findall(r"\d+", node.get("bounds")))
                        adb("shell", "input", "tap", (x1 + x2) // 2, (y1 + y2) // 2)
                        time.sleep(2)
                        log = adb("logcat", "-d", "-t", "1000")
                        check("FATAL EXCEPTION" not in log, "Release launch and chat view have no fatal exception")
                        return
            time.sleep(1)
        raise AssertionError("Chat navigation not found")

    def snapshot(label):
        adb("shell", "am", "force-stop", package)
        directory = args.output / label
        directory.mkdir(exist_ok=True)
        adb("pull", private + "/shared_prefs/yanzi-mobile.xml", directory / "prefs.xml")
        values = {item.attrib.get("name"): item.text if item.tag == "string" else item.attrib.get("value")
                  for item in ET.parse(directory / "prefs.xml").getroot()}
        check(values.get("token") == token and values.get("deviceId") == "android-release-fixture", "Offline session and device identity retained: " + label)
        check(values.get("release_fixture_setting") == "升级中文偏好", "Preferences retained: " + label)
        adb("pull", private + "/files/release-fixture.bin", directory / "file.bin")
        check(hashlib.sha256((directory / "file.bin").read_bytes()).digest() == hashlib.sha256(payload).digest(), "Local file hash retained: " + label)
        adb("pull", private + "/databases", directory / "databases")
        db = sqlite3.connect(directory / "databases/yanzi-chat-history.db")
        try:
            rows = db.execute("SELECT content FROM chat_messages WHERE account_id=? ORDER BY time_ms,id", ("release-fixture",)).fetchall()
        finally:
            db.close()
        check([r[0] for r in rows] == [m["content"] for m in history], "All 75 legacy messages migrated exactly once: " + label)
        check(values.get("chat_history_sqlite_migrated_v1") == "true", "Migration marker retained: " + label)

    encoded = base64.urlsafe_b64encode(json.dumps({"sub": "release-fixture", "exp": 4102444800}).encode()).decode().rstrip("=")
    token = "offline." + encoded + ".fixture"
    history = [{"role": "peer", "kind": "text", "content": f"发布验收消息 {i} 中文\n第二行", "time": 1791000000000 + i} for i in range(75)]
    payload = bytes(range(256)) * 4096
    report = {"serial": args.serial, "baseline": str(args.baseline), "candidate": str(args.candidate),
              "syntheticOfflineSession": True, "liveAuthenticationTested": False, "checks": checks, "passed": False}
    try:
        # Initial clean baseline installation is intentional and emulator-only.
        adb("root")
        adb("wait-for-device")
        check(adb("shell", "id", "-u").strip() == "0", "Emulator root available for non-debuggable release fixtures")
        adb("uninstall", package, allow_failure=True)
        install(args.baseline)
        adb("shell", "am", "start", "-n", package + "/.MainActivity")
        time.sleep(2)
        adb("shell", "am", "force-stop", package)
        uid = adb("shell", "stat", "-c", "%u", private).strip()
        check(uid.isdigit(), "Application UID validated")
        preferences = ET.Element("map")
        for key, value in {"baseUrl": "http://127.0.0.1:9", "token": token,
                           "deviceId": "android-release-fixture", "release_fixture_setting": "升级中文偏好",
                           "desktop_chat_history": json.dumps(history, ensure_ascii=False)}.items():
            ET.SubElement(preferences, "string", name=key).text = value
        prefs_path = args.output / "seed-prefs.xml"
        ET.ElementTree(preferences).write(prefs_path, encoding="utf-8", xml_declaration=True)
        file_path = args.output / "seed-file.bin"
        file_path.write_bytes(payload)
        adb("push", prefs_path, "/data/local/tmp/release-seed.xml")
        adb("push", file_path, "/data/local/tmp/release-seed.bin")
        adb("shell", "mkdir", "-p", private + "/shared_prefs", private + "/files")
        for source, destination in [("release-seed.xml", "shared_prefs/yanzi-mobile.xml"), ("release-seed.bin", "files/release-fixture.bin")]:
            target = private + "/" + destination
            adb("shell", "cp", "/data/local/tmp/" + source, target)
            adb("shell", "chown", uid + ":" + uid, target)
            adb("shell", "chmod", "600", target)
            adb("shell", "restorecon", target)
        install(args.candidate, "-r")
        launch_chat()
        snapshot("upgraded")
        install(args.candidate, "-r")
        launch_chat()
        snapshot("reinstalled")
        downgrade = adb("install", "-r", "-d", args.baseline, allow_failure=True)
        report["rollbackInstallResult"] = downgrade.strip()
        if "Success" in downgrade:
            check(True, "System accepted data-preserving downgrade")
            launch_chat()
            snapshot("rolled-back")
            install(args.candidate, "-r")
            launch_chat()
            snapshot("reupgraded")
        else:
            check("INSTALL_FAILED_VERSION_DOWNGRADE" in downgrade, "Android blocks release downgrade without deleting data")
            launch_chat()
            snapshot("downgrade-rejected")
        report["passed"] = True
    finally:
        cleared = adb("shell", "pm", "clear", package, allow_failure=True)
        report["fixtureCleared"] = "Success" in cleared
        adb("shell", "rm", "-f", "/data/local/tmp/release-seed.xml", "/data/local/tmp/release-seed.bin", "/sdcard/release-upgrade-ui.xml", allow_failure=True)
        if report["fixtureCleared"]:
            adb("shell", "am", "start", "-n", package + "/.MainActivity", allow_failure=True)
            time.sleep(2)
            foreground = adb("shell", "dumpsys", "activity", "activities", allow_failure=True)
            report["cleanSmokeForeground"] = bool(re.search(r"mResumedActivity.*" + re.escape(package), foreground))
        (args.output / "android-release-verification.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    check(report["fixtureCleared"] and report.get("cleanSmokeForeground"), "Emulator fixture cleared and clean release smoke completed")


if __name__ == "__main__":
    main()
