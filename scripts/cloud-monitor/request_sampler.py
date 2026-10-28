#!/usr/bin/env python3
"""Bounded, privacy-preserving Workers Tail sampler; never stores raw events."""
import collections, datetime, json, os, pathlib, subprocess, select, time, re, hashlib, ipaddress, secrets
DURATION_SECONDS = max(15, min(180, int(os.environ.get('YANZI_TAIL_SECONDS', '180'))))
ROOT=pathlib.Path('/var/lib/yanzi-cloud-monitor/request-samples')
ROOT.mkdir(mode=0o700,parents=True,exist_ok=True)
# Keep only recent aggregates, never raw request logs.
for old in ROOT.glob('*.json'):
 if old.stat().st_mtime < __import__('time').time()-14*86400:old.unlink()
os.chmod(ROOT,0o700)
# Keep the attribution salt private; only pseudonyms and aggregates reach disk.
KEY_PATH = ROOT/'.source-attribution-key'
try:
 fd = os.open(KEY_PATH, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
 try: os.write(fd, secrets.token_bytes(32))
 finally: os.close(fd)
except FileExistsError: pass
os.chmod(KEY_PATH,0o600)
KEY = KEY_PATH.read_bytes()
if len(KEY) != 32: raise RuntimeError('invalid attribution key length')
def digest(domain, value):
 if not value: return None
 return hashlib.blake2s((domain + ':' + value).encode(),key=KEY,digest_size=12).hexdigest()
def safe_version(value):
 v = str(value or '').strip().lower()
 return v if re.fullmatch(r'\d{1,4}(?:\.\d{1,4}){1,3}(?:[-.][a-z0-9]{1,16})?',v) else 'unknown'
def valid_device(value):
 v = str(value or '')
 return v if re.fullmatch(r'[a-zA-Z0-9_-]{8,100}',v) else None
def verified_ip(value):
 try: return str(ipaddress.ip_address(value or ''))
 except (ValueError,TypeError): return None
def safe_endpoint(path):
 # Strict whitelist; never emit arbitrary path segments, invite secrets or user data.
 if path in ('/v1/applications/access-requests','/v1/applications/access-resources',
             '/v1/applications/access-grants','/v1/applications/catalog',
             '/v1/applications/library','/v1/auth/login','/v1/auth/me',
             '/v1/sync/webdav-config','/health'):
  return path
 if path.startswith('/v1/applications/access-requests/'):
  return '/v1/applications/access-requests/:id'
 if path.startswith('/v1/applications/library/'):
  return '/v1/applications/library/:id'
 if path.startswith('/v1/applications/access-grants/'):
  return '/v1/applications/access-grants/:id'
 if path.startswith('/v1/me/mobile/messages'):
  return '/v1/me/mobile/messages/*'
 if path.startswith('/v1/me/devices'):
  return '/v1/me/devices/*'
 if path.startswith('/v1/sync/'):
  return '/v1/sync/*'
 if path.startswith('/v1/applications/'):
  return '/v1/applications/*'
 if path.startswith('/v1/auth/'):
  return '/v1/auth/*'
 return '/other'

cmd=['timeout',f'{DURATION_SECONDS}s','npx','--yes','wrangler@4.149.0','tail','yanzi-sync','--format','json']
env=os.environ.copy()
for k in ('CLOUDFLARE_API_TOKEN','CLOUDFLARE_API_KEY','CLOUDFLARE_EMAIL'):env.pop(k,None)
env['HOME']='/root'
counts=collections.Counter(); statuses=collections.Counter(); users=collections.Counter(); devices=collections.Counter(); total=0; attributed=0; log_messages=0; structured=0; structured_statuses=collections.Counter(); key_presence=collections.Counter(); route_status=collections.Counter(); auth_endpoints=collections.Counter(); http_statuses=collections.Counter(); http_route_status=collections.Counter()
source_counts=collections.Counter()
source_routes=collections.Counter()
endpoint_counts=collections.Counter()
source_endpoints=collections.Counter()
source_statuses=collections.Counter()
source_types=collections.Counter()
version_counts=collections.Counter()
attempted_accounts=collections.Counter()
failure_codes=collections.Counter()
verified_devices=collections.Counter()
claimed_devices=collections.Counter()
user_routes=collections.Counter()
network_counts=collections.Counter()
login_sources=collections.Counter()
user_sources=collections.Counter()
user_lookups=collections.Counter()
device_lookups=collections.Counter()

p=subprocess.Popen(cmd,cwd='/opt/yanzi-observability',env=env,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True,start_new_session=True)
dec=json.JSONDecoder(); buf=''
try:
 deadline=time.monotonic()+DURATION_SECONDS+5
 while time.monotonic()<deadline:
  ready,_,_=select.select([p.stdout],[],[],min(2,max(0,deadline-time.monotonic())))
  if not ready:continue
  line=p.stdout.readline()
  if not line:break
  buf+=line
  if len(buf)>262144:buf='';continue
  try: obj,end=dec.raw_decode(buf.lstrip())
  except json.JSONDecodeError:continue
  buf=buf.lstrip()[end:];total+=1
  req=obj.get('event',{}).get('request',{})
  headers={str(k).lower():v for k,v in (req.get('headers') or {}).items()}
  from urllib.parse import urlsplit
  path=urlsplit(req.get('url','')).path
  family=path.split('/')[2] if path.startswith('/v1/') and len(path.split('/'))>2 else 'other'
  route='/health' if path=='/health' else '/v1/'+family if family in ('sync','auth','devices','device','messaging','messages','records','apps','applications','me') else '/other'
  endpoint=safe_endpoint(path)
  client=headers.get('x-yanzi-client','unknown')
  if client not in ('desktop','server','android','mobile','web'):client='other'
  counts[(route,client)]+=1
  endpoint_counts[(endpoint,client)]+=1
  version=safe_version(headers.get('x-yanzi-client-version'))
  version_counts[(client,version)]+=1
  # 100% of tail events, not just application error logs, carry safe source attribution.
  claimed=valid_device(headers.get('x-yanzi-device-id'))
  network=verified_ip(headers.get('cf-connecting-ip'))
  day=datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%d')
  claimed_key=digest('device',claimed)
  network_key=digest('network-'+day,network)
  kind='claimed_device' if claimed_key else 'network' if network_key else 'unknown'
  source_key=claimed_key if claimed_key else network_key if network_key else 'unknown'
  source=(kind,source_key,client,version)
  source_counts[source]+=1
  source_routes[(source,route)]+=1
  source_endpoints[(source,endpoint)]+=1
  source_types[kind]+=1
  if network_key:network_counts[network_key]+=1
  statuses[str(obj.get('outcome','unknown'))]+=1
  http_status = obj.get('event',{}).get('response',{}).get('status')
  if isinstance(http_status,int) and 100 <= http_status <= 599:
   http_statuses[str(http_status)]+=1
   source_statuses[(source,str(http_status))]+=1
   http_route_status[(route,str(http_status))]+=1
  for entry in obj.get('logs',[]):
   log_messages+=1
   for value in entry.get('message',[]):
    if not isinstance(value,str) or not value.startswith('{'):continue
    try: detail=json.loads(value)
    except (ValueError,TypeError):continue
    if detail.get('event')!='yanzi_request':continue
    structured+=1
    user_ref=detail.get('user_lookup_ref')
    if isinstance(user_ref,str) and re.fullmatch(r'[0-9a-f]{24}',user_ref):user_lookups[(user_ref,source[0],source[1])]+=1
    device_ref=detail.get('device_lookup_ref')
    trust=detail.get('device_lookup_trust')
    if isinstance(device_ref,str) and re.fullmatch(r'[0-9a-f]{24}',device_ref) and trust in ('verified','claimed'):
     device_lookups[(device_ref,trust,source[0],source[1])]+=1
    err=detail.get('failure_code')
    if isinstance(err,str) and re.fullmatch(r'[a-z_]{3,40}',err):failure_codes[err]+=1
    attempted=detail.get('attempted_account_key')
    if isinstance(attempted,str) and re.fullmatch(r'[A-Za-z0-9_-]{16}',attempted):
     attempted_accounts[(attempted,str(detail.get('status','missing')))]+=1
     login_sources[(attempted,source[0],source[1],str(detail.get('status','missing')))]+=1
    verified=detail.get('verified_device_key') or detail.get('device_key')
    if isinstance(verified,str) and re.fullmatch(r'[A-Za-z0-9_-]{16}',verified):verified_devices[verified]+=1
    claimedLog=detail.get('claimed_device_key')
    if isinstance(claimedLog,str) and re.fullmatch(r'[A-Za-z0-9_-]{16}',claimedLog):claimed_devices[claimedLog]+=1
    st=str(detail.get('status','missing'))
    route_status[(route,st)]+=1
    if route=='/v1/auth':
     recognized={'login','me','register','send-code','send-reset-code','reset-password'}
     endpoint=path[len('/v1/auth/'):].strip('/') if path.startswith('/v1/auth/') else ''
     auth_endpoints[(endpoint if endpoint in recognized else 'other',req.get('method','other'),st)]+=1
    structured_statuses[str(detail.get("status","missing"))]+=1
    key_presence["user_present" if detail.get("user_key") else "user_missing"]+=1
    key_presence["device_present" if (detail.get("verified_device_key") or detail.get("claimed_device_key") or detail.get("device_key")) else "device_missing"]+=1
    key=detail.get('user_key');device=detail.get('verified_device_key') or detail.get('device_key')
    if isinstance(key,str) and re.fullmatch(r'[A-Za-z0-9_-]{16}',key):
     attributed+=1;users[key]+=1;user_routes[(key,route,st)]+=1;user_sources[(key,source[0],source[1])]+=1
     if isinstance(device,str) and re.fullmatch(r'[A-Za-z0-9_-]{16}',device):devices[(key,device)]+=1
finally:
 if p.poll() is None:
  import signal
  os.killpg(p.pid,signal.SIGTERM)
 try:p.wait(timeout=5)
 except subprocess.TimeoutExpired:os.killpg(p.pid,signal.SIGKILL);p.wait()
now=datetime.datetime.now(datetime.timezone.utc)
top_sources=[]
for source,n in source_counts.most_common(50):
 kind,key,cl,ver=source
 top_sources.append({'source_type':kind,'source_key':key,'client':cl,'version':ver,
  'sampled_requests':n,
  'routes':[{'route':r,'count':count} for (s,r),count in source_routes.most_common() if s==source][:15],
  'endpoints':[{'endpoint':e,'count':count} for (s,e),count in source_endpoints.most_common() if s==source][:15],
  'statuses':[{'status':st,'count':count} for (s,st),count in source_statuses.most_common() if s==source][:12]})
output={'sample_utc':now.isoformat(),'duration_seconds':DURATION_SECONDS,'total_events':total,'by_route_client':[{'route':r,'client':c,'count':n} for (r,c),n in counts.most_common()],'by_endpoint_client':[{'endpoint':e,'client':c,'count':n} for (e,c),n in endpoint_counts.most_common()],'outcomes':dict(statuses),'http_statuses':dict(http_statuses),'http_route_status':[{'route':r,'status':st,'count':n} for (r,st),n in http_route_status.most_common()],'log_messages':log_messages,'structured_events':structured,'structured_statuses':dict(structured_statuses),'by_route_status':[{'route':r,'status':st,'count':n} for (r,st),n in route_status.most_common()],'auth_endpoints':[{'endpoint':e,'method':m,'status':st,'count':n} for (e,m,st),n in auth_endpoints.most_common()],'key_presence':dict(key_presence),'attributed_events':attributed,'by_user':[{'user_key':k,'sampled_requests':v} for k,v in users.most_common()], 'by_device':[{'user_key':k,'device_key':d,'sampled_requests':v} for (k,d),v in devices.most_common()],
 'by_source':top_sources,'source_type_counts':dict(source_types),
 'by_version':[{'client':cl,'version':ver,'count':n} for (cl,ver),n in version_counts.most_common()],
 'by_network':[{'network_key':k,'count':n} for k,n in network_counts.most_common(30)],
 'by_login_attempt':[{'attempted_account_key':k,'status':st,'count':n} for (k,st),n in attempted_accounts.most_common(30)],
 'failure_codes':dict(failure_codes),
 'by_verified_device':[{'verified_device_key':k,'sampled_logs':n} for k,n in verified_devices.most_common(30)],
 'by_claimed_device':[{'claimed_device_key':k,'sampled_logs':n} for k,n in claimed_devices.most_common(30)],
 'by_user_route':[{'user_key':k,'route':r,'status':st,'sampled_logs':n} for (k,r,st),n in user_routes.most_common(50)],
 'by_login_source':[{'attempted_account_key':a,'source_type':t,'source_key':k,'status':st,'sampled_logs':n} for (a,t,k,st),n in login_sources.most_common(50)],
 'by_user_source':[{'user_key':u,'source_type':t,'source_key':k,'sampled_logs':n} for (u,t,k),n in user_sources.most_common(50)],
 'by_user_lookup':[{'user_lookup_ref':u,'source_type':t,'source_key':k,'sampled_logs':n} for (u,t,k),n in user_lookups.most_common(50)],
 'by_device_lookup':[{'device_lookup_ref':d,'trust':trust,'source_type':t,'source_key':k,'sampled_logs':n} for (d,trust,t,k),n in device_lookups.most_common(50)], 'scope':'worker-wide; 100% of tail-window events grouped by claimed device (unverified) or network (may contain multiple devices). Authenticated identity and login attempts come from sampled structured logs; pseudonyms only; not daily totals' }
tmp=ROOT/(now.strftime('%Y%m%dT%H%M%SZ')+'.tmp');dest=tmp.with_suffix('.json')
tmp.write_text(json.dumps(output,ensure_ascii=False));os.chmod(tmp,0o600);tmp.replace(dest)
print(json.dumps({'file':str(dest),'events':total,'groups':len(counts)}))
