#!/usr/bin/env python3
# Read-only inventory; full object keys stay in a root-only 0600 manifest.
import sys,json,urllib.request,urllib.parse,datetime,pathlib,os,collections,re
sys.path.insert(0,'/opt/yanzi-cloud-monitor')
import monitor
os.umask(0o077)
root=pathlib.Path('/var/lib/yanzi-cloud-monitor/r2')
root.mkdir(mode=0o700,parents=True,exist_ok=True)
token=monitor.load_token(monitor.CREDS)
base='https://api.cloudflare.com/client/v4/accounts/'+monitor.ACCOUNT_ID+'/r2/buckets/'
all_objects={}
def category(key):
 parts=key.split('/')
 if parts[0]=='users': return 'users/[private-account-data]'
 if parts[0] in ('downloads','extensions','extension-icons','mobile-attachments','attachments'):return '/'.join(parts[:2]) if len(parts)>2 else parts[0]
 return parts[0] if len(parts)>1 else '[root-object]'
for bucket in ('openquickhost-sync-assets','img'):
 cursor=None
 objects=[]
 for page in range(100):
  q={'per_page':1000}
  if cursor:q['cursor']=cursor
  req=urllib.request.Request(base+bucket+'/objects?'+urllib.parse.urlencode(q),headers={'Authorization':'Bearer '+token,'Accept':'application/json'})
  with urllib.request.urlopen(req,timeout=40) as f:d=json.load(f)
  if not d.get('success'):raise RuntimeError('R2 inventory failed')
  rows=d.get('result',[])
  objects.extend([{'key':str(x.get('key','')),'size':int(x.get('size') or 0),'last_modified':x.get('last_modified'),'etag':x.get('etag'),'storage_class':x.get('storage_class')} for x in rows])
  info=d.get('result_info') or {}
  if not info.get('is_truncated'):break
  new_cursor=info.get('cursor')
  if not new_cursor or new_cursor==cursor:raise RuntimeError('Pagination incomplete')
  cursor=new_cursor
 else:raise RuntimeError('Excessive pages; refusing incomplete inventory')
 all_objects[bucket]=objects
s=json.dumps({'generated_at_utc':datetime.datetime.now(datetime.timezone.utc).isoformat(),'complete':True,'buckets':all_objects},ensure_ascii=False,indent=1)
file=root/('inventory-'+datetime.datetime.now(datetime.timezone.utc).date().isoformat()+'.json')
file.write_text(s);file.chmod(0o600)
print('private_inventory_saved',str(file))
overall=collections.defaultdict(lambda:[0,0,0])
for bucket,items in all_objects.items():
 print('BUCKET',bucket,'objects',len(items),'bytes',sum(o['size'] for o in items))
 for x in items:
  label=category(x['key'])
  a=overall[(bucket,label)]
  a[0]+=1;a[1]+=x['size']
  dt=x['last_modified']
  if dt:
   try:
    if datetime.datetime.fromisoformat(dt.replace('Z','+00:00'))<datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(days=30):a[2]+=1
   except ValueError:pass
for (bucket,label),(count,size,old) in sorted(overall.items(),key=lambda x:-x[1][1]):
 print('PREFIX',bucket, ('extension-icons/[extension]' if label.startswith('extension-icons/') else label),'count',count,'bytes',size,'older30d',old)
print('TOTAL_OBJECTS',sum(len(x) for x in all_objects.values()))
