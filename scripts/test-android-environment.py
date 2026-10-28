"""Opt-in native environment integration; destructive fixture setup is emulator-only."""
import argparse, json, re, subprocess, time, urllib.request, xml.etree.ElementTree as ET
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--serial', required=True)
p.add_argument('--fixture', required=True)
p.add_argument('--output', required=True)
a = p.parse_args()
assert a.serial.startswith('emulator-'), 'Never seed or clear a physical phone'
f = json.loads(Path(a.fixture).read_text())
assert re.fullmatch(r'http://127\.0\.0\.1:\d+', f['baseUrl'])
adb = 'F:/SDK/platform-tools/adb.exe'
pkg = 'cc.luoluoluo.yanzi.mobile.dev'
port = f['baseUrl'].rsplit(':', 1)[1]
checks = []

def run(*args, data=None):
    r = subprocess.run([adb, '-s', a.serial, *args], input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if r.returncode: raise RuntimeError(r.stderr.decode(errors='replace'))
    return r.stdout

def prefs(name):
    return {x.attrib['name']: x.text if x.tag == 'string' else x.attrib.get('value') for x in ET.fromstring(run('exec-out', 'run-as', pkg, 'cat', 'shared_prefs/'+name+'.xml'))}

def cloud():
    req = urllib.request.Request(f['baseUrl']+'/v1/me/devices/'+f['deviceId']+'/environment/'+f['extensionId'], headers={'Authorization':'Bearer '+f['token']})
    return json.load(urllib.request.urlopen(req, timeout=10))

def ui():
    run('shell', 'uiautomator', 'dump', '/sdcard/environment-ui.xml')
    return ET.fromstring(run('exec-out', 'cat', '/sdcard/environment-ui.xml'))

def tap(text):
    for _ in range(12):
        for x in ui().iter('node'):
            if x.attrib.get('text') == text:
                nums = list(map(int, re.findall(r'\d+', x.attrib['bounds'])))
                if nums[3] > nums[1]:
                    run('shell', 'input', 'tap', str((nums[0]+nums[2])//2), str((nums[1]+nums[3])//2)); return
        run('shell', 'input', 'swipe', '500', '1400', '500', '500', '300')
    raise AssertionError('UI control missing: '+text)

def wait(test, label, timeout=35):
    end = time.time()+timeout
    while time.time()<end:
        try:
            if test(): checks.append(label); return
        except Exception: pass
        time.sleep(1)
    raise AssertionError(label)

try:
    run('shell', 'am', 'force-stop', pkg)
    run('shell', 'pm', 'clear', pkg)
    run('reverse', 'tcp:'+port, 'tcp:'+port)
    definition = {'id':f['extensionId'], 'name':'位置', 'runtime':'mobile-js', 'permissions':['device.environment'], 'script':{'source':'async function run(context){context.mobile.openEnvironmentSettings();}'}}
    root = ET.Element('map')
    for k,v in {**f, 'mobileExtensions':json.dumps([definition], ensure_ascii=False)}.items(): ET.SubElement(root, 'string', name=k).text=v
    run('shell', 'run-as', pkg, 'mkdir', '-p', 'shared_prefs')
    run('exec-in', 'run-as', pkg, 'sh', '-c', 'cat > shared_prefs/yanzi-mobile.xml', data=ET.tostring(root, encoding='utf-8'))
    run('root')
    assert 'uid=0' in run('shell','id').decode(), 'Native non-exported activity test needs a rooted emulator'
    run('shell','am','start','-n',pkg+'/cc.luoluoluo.yanzi.mobile.EnvironmentSettingsActivity','--es','extensionId',f['extensionId'])
    wait(lambda: any('位置与环境' in x.attrib.get('text','') for x in ui().iter('node')), 'Native extension settings opened')
    tap('立即检测并上报')
    wait(lambda: any('定位权限未授权' in x.attrib.get('text','') for x in ui().iter('node')), 'Permission denial remains unknown')
    for permission in ['ACCESS_COARSE_LOCATION','ACCESS_FINE_LOCATION','ACCESS_BACKGROUND_LOCATION']:
        run('shell','pm','grant',pkg,'android.permission.'+permission)
    run('shell','settings','put','secure','location_mode','3')
    run('emu','geo','fix','121.4737','31.2304')
    tap('将当前位置设为家')
    wait(lambda: any('家的位置：已设置' in x.attrib.get('text','') for x in ui().iter('node')), 'Home stored locally')
    tap('开启环境上报')
    wait(lambda: cloud()['exists'], 'Opt-in snapshot uploaded')
    assert 'location' not in cloud()['value']; checks.append('Coordinates excluded by default')
    # A forced native job must upload a new sequence, even when it reuses a valid cached fix.
    sequence = cloud()['value']['sequence']
    time.sleep(2); run('emu','geo','fix','121.4737','31.2304')
    run('shell','cmd','jobscheduler','run','-f',pkg,'51901')
    wait(lambda: cloud()['exists'] and cloud()['value']['sequence']>sequence and cloud()['value']['availability']=='available', 'Persisted JobScheduler sample')
    run('reverse','--remove','tcp:'+port)
    # Removing reverse does not close already pooled HTTP sockets; restart only the fixture process.
    run('shell','am','force-stop',pkg)
    run('shell','am','start','-n',pkg+'/cc.luoluoluo.yanzi.mobile.EnvironmentSettingsActivity','--es','extensionId',f['extensionId'])
    tap('立即检测并上报')
    wait(lambda: any(k.endswith('.pending') for k in prefs('mobile-environment')), 'Offline latest snapshot retained')
    run('reverse','tcp:'+port,'tcp:'+port)
    sequence = cloud()['value']['sequence']
    run('shell','cmd','jobscheduler','run','-f',pkg,'51901')
    wait(lambda: cloud()['value']['sequence']>sequence and not any(k.endswith('.pending') for k in prefs('mobile-environment')), 'Pending snapshot recovered without history replay')
    tap('向同账号电脑共享精确坐标（可关闭）')
    tap('立即检测并上报')
    wait(lambda: 'location' in cloud()['value'], 'Explicit coordinate opt-in')
    tap('向同账号电脑共享精确坐标（可关闭）')
    wait(lambda: 'location' not in cloud()['value'], 'Coordinate opt-out clears cloud immediately')
    tap('停止并清除共享状态')
    wait(lambda: not cloud()['exists'], 'Stop clears current cloud snapshot')
    run('shell','cmd','jobscheduler','run','-f',pkg,'51901')
    time.sleep(2); assert not cloud()['exists']; checks.append('Stopped task cannot resume sharing')
    run('shell','am','force-stop',pkg)
    for node in root:
        if node.attrib['name']=='deviceId':node.text='phone-different-installation'
    run('exec-in','run-as',pkg,'sh','-c','cat > shared_prefs/yanzi-mobile.xml',data=ET.tostring(root,encoding='utf-8'))
    run('shell','am','start','-n',pkg+'/cc.luoluoluo.yanzi.mobile.EnvironmentSettingsActivity','--es','extensionId',f['extensionId'])
    wait(lambda: any('家的位置：未设置' in x.attrib.get('text','') and '尚未采集' in x.attrib.get('text','') for x in ui().iter('node')), 'Other installation scope cannot inherit local home or samples')
    print('Android native environment: '+str(len(checks))+' integration checks passed.')
finally:
    try:
        Path(a.output, 'native-state.xml').write_bytes(run('exec-out','run-as',pkg,'cat','shared_prefs/mobile-environment.xml'))
        Path(a.output, 'native-ui.xml').write_bytes(ET.tostring(ui()))
    except Exception: pass
    Path(a.output, 'android-environment-result.json').write_text(json.dumps({'checks':checks}, ensure_ascii=False, indent=2), encoding='utf-8')
    run('shell','am','force-stop',pkg)
    run('shell','pm','clear',pkg)
    subprocess.run([adb,'-s',a.serial,'reverse','--remove','tcp:'+port],capture_output=True)
    run('shell','am','start','-n',pkg+'/cc.luoluoluo.yanzi.mobile.MainActivity')
