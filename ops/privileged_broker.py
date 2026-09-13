#!/usr/bin/env python3
"""Fail-closed seal/transaction broker. Production mutations require explicit enablement."""
from __future__ import annotations
import errno, fcntl, hashlib, json, os, posixpath, pwd, re, shutil, stat, subprocess, tempfile, time, uuid
from pathlib import Path

ID_RE=re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$")
PKG_RE=re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{1,127}$")
VER_RE=re.compile(r"^[0-9A-Za-z][0-9A-Za-z.+_-]{0,63}$")
HASH_RE=re.compile(r"^[0-9a-fA-F]{64}$")
PACKAGE_TYPES={"loader","plugin","mod"}
ROUTES=("BepInEx/plugins/","BepInEx/config/","BepInEx/core/","BepInEx/patchers/","BepInEx/monomod/","doorstop_libs/")

class BrokerError(ValueError): pass

def fail(s): raise BrokerError(s)
def digest(p):
 h=hashlib.sha256()
 with open(p,"rb") as f:
  for b in iter(lambda:f.read(1024*1024),b""): h.update(b)
 return h.hexdigest()
def canonical_bytes(x): return json.dumps(x,sort_keys=True,separators=(",",":"),ensure_ascii=True).encode()
def fsync_file(p):
 with open(p,"rb") as f: os.fsync(f.fileno())
def fsync_dir(p):
 fd=os.open(p,os.O_RDONLY|os.O_DIRECTORY); os.fsync(fd); os.close(fd)
def safe_rel(n):
 if not isinstance(n,str) or not n or "\\" in n or "\x00" in n or n.startswith("/") or (len(n)>1 and n[1]==":"): fail("unsafe path")
 x=posixpath.normpath(n)
 if x!=n or x in (".","..") or x.startswith("../"): fail("path traversal")
 return x
def under(root,p):
 root=Path(root).resolve(); p=Path(p)
 if p.is_symlink(): fail("symlink path component")
 cur=p
 while cur!=cur.parent:
  if cur.is_symlink(): fail("symlink path component")
  cur=cur.parent
 try: p.resolve().relative_to(root)
 except ValueError: fail("path outside fixed root")
 return p

def tree_hash(items): return hashlib.sha256(canonical_bytes(sorted(items,key=lambda x:x["destination"]))).hexdigest()

class LocalController:
 def __init__(self, cfg): self.cfg=cfg
 def status(self, instance):
  c=self.cfg["instances"][instance]; service=c["service"]
  if not service: return {"active":"inactive","sub":"dead","pid":0,"ports":False}
  p=subprocess.run(["/usr/bin/systemctl","show",service,"-p","ActiveState","-p","SubState","-p","MainPID","--no-pager"],capture_output=True,text=True,timeout=30)
  d=dict(x.split("=",1) for x in p.stdout.splitlines() if "=" in x)
  ports=subprocess.run(["/usr/bin/ss","-H","-lun"],capture_output=True,text=True,timeout=10).stdout
  return {"active":d.get("ActiveState"),"sub":d.get("SubState"),"pid":int(d.get("MainPID","0") or 0),"ports":any(":"+str(x) in ports for x in c["ports"])}
 def stop(self,instance):
  service=self.cfg["instances"][instance]["service"]; subprocess.run(["/usr/bin/systemctl","stop",service],check=True,timeout=180)
 def start(self,instance):
  service=self.cfg["instances"][instance]["service"]; subprocess.run(["/usr/bin/systemctl","start",service],check=True,timeout=180)
 def health(self,instance):
  s=self.status(instance)
  if not (s["active"]=="active" and s["sub"]=="running" and s["pid"]>0 and s["ports"]): return False
  service=self.cfg["instances"][instance]["service"]
  text=subprocess.run(["/usr/bin/journalctl","-u",service,"-n","300","--no-pager","-o","cat"],capture_output=True,text=True,timeout=30).stdout
  return all(re.search(p,text,re.I) for p in (r"BepInEx",r"Chainloader",r"Game server connected")) and not re.search(r"fatal loader error|permission denied|missing dependency",text,re.I)

class Broker:
 def __init__(self,cfg,controller=None,uid=None):
  self.cfg=cfg; self.controller=controller or LocalController(cfg); self.uid=uid if uid is not None else os.getuid()
  self.backend_uid=cfg.get("backendUid",pwd.getpwnam(cfg.get("backendUser","gamepanel")).pw_uid); self.valheim_uid=cfg.get("valheimUid",pwd.getpwnam("valheim").pw_uid)
  self.state=Path(cfg["paths"].get("brokerState",cfg["paths"]["productionBackup"]+"/broker-state")); self.state.mkdir(parents=True,exist_ok=True)
  self.replay=self.state/"request-ids.json"; self.lock_path=self.state/"broker.lock"; self.audit_path=self.state/"audit.jsonl"
  self.recovery_required=any(json.loads(p.read_text()).get("state") in {"prepared","applying"} for p in self.state.parent.glob("*/transaction.json") if p.is_file())
  self.enabled=bool(cfg.get("features",{}).get("productionDeploymentEnabled",False))
 def audit(self,req,event,result):
  row={"event":event,"requestId":req["requestId"],"action":req["action"],"instanceId":req["instanceId"],"deploymentId":req["deploymentId"],"result":result,"atUtc":time.strftime("%Y-%m-%dT%H:%M:%SZ",time.gmtime())}
  with open(self.audit_path,"a",encoding="utf-8") as f: f.write(json.dumps(row,separators=(",",":"))+"\n"); f.flush(); os.fsync(f.fileno())
 def envelope(self,req):
  if set(req)!={"requestVersion","requestId","action","instanceId","deploymentId"}: fail("request fields are not allowlisted")
  if req["requestVersion"]!=1 or not isinstance(req["requestId"],str) or not re.fullmatch(r"[0-9a-fA-F-]{36}",req["requestId"]): fail("invalid request envelope")
  if req["action"] not in {"status","start","stop","restart","seal","deploy","rollback"}: fail("action not allowlisted")
  if req["instanceId"] not in self.cfg["instances"]: fail("instance not allowlisted")
  if not isinstance(req["deploymentId"],str) or not ID_RE.fullmatch(req["deploymentId"]): fail("invalid deploymentId")
 def peer(self,mutating):
  if mutating and self.uid not in {0,self.backend_uid}: fail("peer credential rejected")
 def replay_check(self,rid,consume=False):
  d=json.loads(self.replay.read_text()) if self.replay.exists() else []
  if rid in d: fail("requestId replay")
  if consume:
   d=(d+[rid])[-4096:]; tmp=self.replay.with_suffix(".tmp"); tmp.write_text(json.dumps(d)); os.replace(tmp,self.replay); fsync_file(self.replay); fsync_dir(self.state)
 def fixed_stage(self,did): return under(self.cfg["paths"]["stagingRoot"],Path(self.cfg["paths"]["stagingRoot"])/did)
 def read_manifest(self,did):
  root=self.fixed_stage(did); p=root/"deployment-manifest.json"
  if not p.is_file() or p.is_symlink(): fail("staging manifest missing")
  d=json.loads(p.read_text()); return root,p,d
 def validate_manifest(self,did):
  root,p,d=self.read_manifest(did)
  required={"instanceId","deploymentId","packageId","version","packageType","targetPlatform","testedGameBuild","state","archiveSha256","files","dependencies","platformExcluded"}
  if set(d)!=required: fail("manifest fields mismatch")
  if d["instanceId"]!="valheim-main" or d["deploymentId"]!=did or d["packageType"] not in PACKAGE_TYPES or d["targetPlatform"]!="linux-x64" or d["state"]!="lab-tested": fail("manifest approval gate failed")
  if not PKG_RE.fullmatch(d["packageId"]) or not VER_RE.fullmatch(d["version"]) or not HASH_RE.fullmatch(d["archiveSha256"]): fail("manifest identity invalid")
  if str(d["testedGameBuild"])!=str(self.cfg["instances"]["valheim-main"]["testedBuild"]): fail("build mismatch")
  if not isinstance(d["dependencies"],list) or any(not isinstance(x,str) for x in d["dependencies"]) or len(set(d["dependencies"]))!=len(d["dependencies"]): fail("dependencies invalid")
  items=[]
  for x in d["files"]:
   if not isinstance(x,dict) or set(x)!={"source","destination","sha256"}: fail("file entry invalid")
   rel=safe_rel(x["destination"]); src=under(root/"normalized",root/"normalized"/safe_rel(x["source"]))
   if not src.is_file() or src.is_symlink() or not HASH_RE.fullmatch(x["sha256"]): fail("source/hash invalid")
   if not any(rel.startswith(r) for r in ROUTES): fail("route not allowlisted")
   if digest(src).lower()!=x["sha256"].lower(): fail("file hash mismatch")
   items.append({"destination":rel,"sha256":x["sha256"].lower(),"sizeBytes":src.stat().st_size})
  if len({x["destination"] for x in items})!=len(items): fail("duplicate files")
  return root,p,d,items,tree_hash(items)
 def seal(self,req):
  self.peer(True); self.replay_check(req["requestId"],True); self.audit(req,"seal","started")
  if req["instanceId"]!="valheim-main": fail("seal instance denied")
  root,mp,d,items,th=self.validate_manifest(req["deploymentId"])
  final=Path(self.cfg["paths"]["productionMods"])/d["packageId"]/d["version"]/d["archiveSha256"].lower()
  if final.exists(): fail("sealed artifact already exists")
  final.parent.mkdir(parents=True,exist_ok=True); tmp=Path(tempfile.mkdtemp(prefix=".seal-",dir=final.parent))
  try:
   (tmp/"files").mkdir()
   for x in items:
    src=root/"normalized"/next(y["source"] for y in d["files"] if y["destination"]==x["destination"]); dst=tmp/"files"/x["destination"]; dst.parent.mkdir(parents=True,exist_ok=True); shutil.copyfile(src,dst); fsync_file(dst)
   mh=digest(mp); approval={"packageId":d["packageId"],"version":d["version"],"archiveSha256":d["archiveSha256"].lower(),"normalizedTreeHash":th,"manifestHash":mh,"targetInstance":d["instanceId"],"testedGameBuild":str(d["testedGameBuild"]),"deploymentId":req["deploymentId"],"packageType":d["packageType"],"state":"approved","used":False}
   (tmp/"manifest.json").write_bytes(mp.read_bytes()); (tmp/"approval.json").write_bytes(canonical_bytes(approval)+b"\n"); fsync_file(tmp/"manifest.json"); fsync_file(tmp/"approval.json"); fsync_dir(tmp); os.chmod(tmp,0o750); os.rename(tmp,final); fsync_dir(final.parent)
   if os.geteuid()==0:
    for dp,ds,fs in os.walk(final):
     os.chown(dp,0,self.valheim_uid,follow_symlinks=False); os.chmod(dp,0o750)
     for n in fs: os.chown(Path(dp)/n,0,self.valheim_uid,follow_symlinks=False); os.chmod(Path(dp)/n,0o640)
   else:
    for dp,ds,fs in os.walk(final):
     os.chmod(dp,0o750)
     for n in fs: os.chmod(Path(dp)/n,0o640)
  except Exception:
   shutil.rmtree(tmp,ignore_errors=True); raise
  self.audit(req,"seal","sealed")
  return {"ok":True,"state":"sealed","sealedPath":str(final),"approval":approval}
 def sealed(self,did):
  base=Path(self.cfg["paths"]["productionMods"]); matches=[]
  for p in base.glob("*/*/*/approval.json"):
   try:
    a=json.loads(p.read_text())
    if a.get("deploymentId")==did: matches.append((p.parent,a))
   except Exception: pass
  if len(matches)!=1: fail("sealed approval missing or ambiguous")
  return matches[0]
 def verify_sealed(self,did):
  root,a=self.sealed(did)
  if a.get("state") not in {"approved","deployed"} or (a.get("used") and a.get("state")!="deployed"): fail("approval used or invalid")
  for dp,ds,fs in os.walk(root):
   for n in fs:
    p=Path(dp)/n
    if p.is_symlink() or (p.stat().st_mode & 0o022): fail("sealed artifact is writable by group/other")
    if self.cfg.get("requireRootOwnership",True) and p.stat().st_uid!=0: fail("sealed artifact is not root-owned")
  if self.cfg.get("requireRootOwnership",True) and root.stat().st_uid!=0: fail("sealed root is not root-owned")
  if not HASH_RE.fullmatch(a.get("archiveSha256","")): fail("approval hash invalid")
  m=json.loads((root/"manifest.json").read_text()); items=[]
  for x in m["files"]:
   rel=safe_rel(x["destination"]); p=under(root/"files",root/"files"/rel)
   if not p.is_file() or p.is_symlink() or digest(p).lower()!=x["sha256"].lower(): fail("sealed hash mismatch")
   items.append({"destination":rel,"sha256":digest(p),"sizeBytes":p.stat().st_size})
  if tree_hash(items)!=a["normalizedTreeHash"]: fail("sealed tree mismatch")
  return root,a,m,items
 def transaction(self,req,action):
  self.peer(True); self.replay_check(req["requestId"],True)
  if not self.enabled: fail("production deployment feature disabled")
  if req["instanceId"]!="valheim-main": fail("deployment instance denied")
  if action=="rollback":
   journal=Path(self.cfg["paths"]["productionBackup"])/req["deploymentId"]/"transaction.json"
   if not journal.is_file(): fail("transaction journal missing")
   result=self.rollback_journal(json.loads(journal.read_text())); self.audit(req,"rollback","rolled-back"); return result
  root,a,m,items=self.verify_sealed(req["deploymentId"])
  if self.recovery_required: fail("incomplete transaction requires recovery")
  pending=[p for p in Path(self.cfg["paths"]["productionBackup"]).glob("*/transaction.json") if json.loads(p.read_text()).get("state") in {"prepared","applying"}]
  if pending: fail("incomplete transaction requires recovery")
  s=self.controller.status("valheim-main")
  if s["active"]!="inactive" or s["sub"]!="dead" or s["pid"]!=0 or s["ports"]: fail("production must be stopped")
  if str(m["testedGameBuild"])!=str(self.cfg["instances"]["valheim-main"]["testedBuild"]): fail("production build mismatch")
  journal=Path(self.cfg["paths"]["productionBackup"])/req["deploymentId"]/"transaction.json"; journal.parent.mkdir(parents=True,exist_ok=True)
  if journal.exists():
   old=json.loads(journal.read_text())
   if action=="rollback": return self.rollback_journal(old)
   fail("transaction already exists")
  server=Path(self.cfg["instances"]["valheim-main"]["root"])/"server"; backup=journal.parent/"backup"; backup.mkdir(parents=True,exist_ok=True)
  records=[]
  for x in items:
   dst=server/x["destination"]; exists=dst.exists(); rec={"destination":x["destination"],"created":not exists,"backup":None}
   if exists:
    if dst.is_symlink() or not dst.is_file(): fail("unsafe production target")
    b=backup/x["destination"]; b.parent.mkdir(parents=True,exist_ok=True); shutil.copy2(dst,b); rec["backup"]=str(b)
   records.append(rec)
  j={"deploymentId":req["deploymentId"],"state":"prepared","initialStatus":s,"records":records,"sealedRoot":str(root),"instanceId":"valheim-main"}; journal.write_text(json.dumps(j,indent=2)+"\n"); fsync_file(journal); fsync_dir(journal.parent)
  try:
   j["state"]="applying"; journal.write_text(json.dumps(j,indent=2)+"\n")
   for x in items:
    src=root/"files"/x["destination"]; dst=server/x["destination"]; dst.parent.mkdir(parents=True,exist_ok=True); fd,tmp=tempfile.mkstemp(prefix=".gamepanel-",dir=dst.parent); os.close(fd); shutil.copyfile(src,tmp); fsync_file(tmp); os.replace(tmp,dst); fsync_dir(dst.parent)
   for x in items:
    if digest(server/x["destination"]).lower()!=x["sha256"].lower(): fail("installed hash mismatch")
   self.controller.start("valheim-main")
   if not self.controller.health("valheim-main"): fail("health check failed")
   j["state"]="installed"; journal.write_text(json.dumps(j,indent=2)+"\n"); a["used"]=True; a["state"]="deployed"; (root/"approval.json").write_bytes(canonical_bytes(a)+b"\n"); self.audit(req,"deploy","deployed"); return {"ok":True,"state":"transactional-deployed","journal":str(journal)}
  except Exception:
   self.rollback_journal(j); raise
 def rollback_journal(self,j):
  server=Path(self.cfg["instances"]["valheim-main"]["root"])/"server"
  if self.controller.status("valheim-main")["active"]=="active":
   self.controller.stop("valheim-main")
   if self.controller.status("valheim-main")["active"]!="inactive": fail("rollback could not stop service")
  for r in j["records"]:
   dst=server/r["destination"]
   if r["backup"]: shutil.copy2(r["backup"],dst)
   elif r["created"]: dst.unlink(missing_ok=True)
  j["state"]="rolled-back"; p=Path(self.cfg["paths"]["productionBackup"])/j["deploymentId"]/"transaction.json"; p.write_text(json.dumps(j,indent=2)+"\n"); return {"ok":True,"state":"rolled-back","idempotent":True}
 def handle(self,req):
  self.envelope(req); action=req["action"]
  if action=="status": return {"ok":True,"status":self.controller.status(req["instanceId"])}
  if action in {"start","stop","restart"}: self.peer(True); self.replay_check(req["requestId"],True); return {"ok":False,"error":"lifecycle unchanged in Phase 3A"}
  lock=open(self.lock_path,"a+")
  try: fcntl.flock(lock,fcntl.LOCK_EX|fcntl.LOCK_NB)
  except Exception: lock.close(); raise
  try:
   if action=="seal": return self.seal(req)
   if action=="deploy": return self.transaction(req,"deploy")
   if action=="rollback": return self.transaction(req,"rollback")
  finally: fcntl.flock(lock,fcntl.LOCK_UN); lock.close()

def load_config(p): return json.loads(Path(p).read_text())
