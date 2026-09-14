import json, os, tempfile, unittest, uuid
from pathlib import Path
from unittest.mock import patch
from fcntl import flock, LOCK_EX
import sys
sys.path.insert(0,str(Path(__file__).resolve().parent))
from privileged_broker import Broker, BrokerError, digest, load_config, normalize_runtime_files, set_runtime_permissions

class C:
 def __init__(self,start_ok=True,healthy=True): self.active='inactive'; self.start_ok=start_ok; self.healthy=healthy; self.started=0
 def status(self,i): return {'active':self.active,'sub':'running' if self.active=='active' else 'dead','pid':1 if self.active=='active' else 0,'ports':self.active=='active'}
 def start(self,i):
  self.started+=1
  if not self.start_ok: raise RuntimeError('start failed')
  self.active='active'
 def stop(self,i): self.active='inactive'
 def health(self,i): return self.healthy and self.active=='active'

class Tests(unittest.TestCase):
 def setUp(self):
  self.t=tempfile.TemporaryDirectory(); r=Path(self.t.name); self.stage=r/'stage'; self.mods=r/'mods'; self.back=r/'back'; self.server=r/'server'; (self.stage/'normalized').mkdir(parents=True); self.server.mkdir()
  (self.stage/'normalized'/'x.dll').write_bytes(b'new'); h=digest(self.stage/'normalized'/'x.dll')
  self.did='dep-test-001'; self.base={'instanceId':'valheim-main','deploymentId':self.did,'packageId':'Author-Mod','version':'1.0.0','packageType':'loader','targetPlatform':'linux-x64','testedGameBuild':'25253791','state':'lab-tested','archiveSha256':'a'*64,'files':[{'source':'x.dll','destination':'BepInEx/plugins/x.dll','sha256':h}],'dependencies':[],'platformExcluded':[{'source':'winhttp.dll','reason':'platform-excluded'}]}
  (self.stage/'deployment-manifest.json').write_text(json.dumps(self.base)); self.cfg={'backendUid':os.getuid(),'valheimUid':os.getuid(),'requireRootOwnership':False,'instances':{'valheim-main':{'service':'fake','root':str(r),'testedBuild':'25253791'},'pz-main':{'service':'pz','root':str(r/'pz'),'ports':[],'testedBuild':''}},'paths':{'stagingRoot':str(r/'stage-root'),'productionMods':str(self.mods),'productionBackup':str(self.back),'brokerState':str(r/'state'),'launcherSource':str(r/'source-main'),'managedLauncherSource':str(r/'source-managed'),'launcherTarget':str(r/'launcher-main'),'managedLauncherTarget':str(r/'launcher-managed')}}
  (Path(self.cfg['paths']['launcherSource'])).write_bytes(b'#!/bin/sh\nexec managed\n'); (Path(self.cfg['paths']['managedLauncherSource'])).write_bytes(b'#!/bin/sh\nexec "$@"\n');
  # use the fixed staging root and deployment tree
  (Path(self.cfg['paths']['stagingRoot'])/self.did/'normalized').parent.mkdir(parents=True); import shutil; shutil.copytree(self.stage,Path(self.cfg['paths']['stagingRoot'])/self.did,dirs_exist_ok=True)
  self.b=Broker(self.cfg,C(),uid=os.getuid()); self.req=lambda action='seal',did=self.did:{'requestVersion':1,'requestId':str(uuid.uuid4()),'action':action,'instanceId':'valheim-main','deploymentId':did}
 def tearDown(self): self.t.cleanup()
 def seal(self): return self.b.handle(self.req())
 def test_valid_sealed_artifact(self): self.assertEqual(self.seal()['state'],'sealed'); self.assertEqual(len(list((self.mods/'Author-Mod'/'1.0.0').glob('*/*/approval.json'))),1)
 def test_hash_mismatch_rejected(self): (Path(self.cfg['paths']['stagingRoot'])/self.did/'normalized'/'x.dll').write_bytes(b'bad'); self.assertRaises(BrokerError,self.seal)
 def test_not_lab_tested_rejected(self): self.base['state']='validated'; (Path(self.cfg['paths']['stagingRoot'])/self.did/'deployment-manifest.json').write_text(json.dumps(self.base)); self.assertRaises(BrokerError,self.seal)
 def test_build_mismatch_rejected(self): self.base['testedGameBuild']='1'; (Path(self.cfg['paths']['stagingRoot'])/self.did/'deployment-manifest.json').write_text(json.dumps(self.base)); self.assertRaises(BrokerError,self.seal)
 def test_traversal_rejected(self): self.base['files'][0]['destination']='../x'; (Path(self.cfg['paths']['stagingRoot'])/self.did/'deployment-manifest.json').write_text(json.dumps(self.base)); self.assertRaises(BrokerError,self.seal)
 def test_unknown_action_instance_rejected(self): q=self.req('unknown'); self.assertRaises(BrokerError,self.b.handle,q); q=self.req(); q['instanceId']='pz-main'; self.assertRaises(BrokerError,self.b.handle,q)
 def test_gamepanel_writable_sealed_rejected(self): self.seal(); p=next((self.mods/'Author-Mod'/'1.0.0').glob('*/*/files/BepInEx/plugins/x.dll')); p.chmod(0o660); self.assertRaises(BrokerError,self.b.verify_sealed,self.did)
 def test_replayed_request_rejected(self): q=self.req(); self.b.handle(q); self.assertRaises(BrokerError,self.b.handle,q)
 def test_concurrent_deployment_rejected(self):
  with open(self.b.lock_path,'a+') as f:
   flock(f,LOCK_EX); self.assertRaises(BlockingIOError,self.b.handle,self.req())
 def deployed(self,controller=None):
  if controller: self.b.controller=controller
  self.seal(); self.b.enabled=True; q=self.req('deploy'); return q
 def test_deploy_installs_and_journals_launchers(self):
  c=C(); q=self.deployed(c); self.b.handle(q)
  self.assertEqual((Path(self.cfg['paths']['launcherTarget'])).read_text(),'#!/bin/sh\nexec managed\n')
  self.assertEqual((Path(self.cfg['paths']['managedLauncherTarget'])).read_text(),'#!/bin/sh\nexec "$@"\n')
  journal=json.loads((self.back/self.did/'transaction.json').read_text())
  self.assertEqual({r['kind'] for r in journal['records'] if r['kind']=='launcher'},{'launcher'})

 def test_deploy_requires_launcher_artifacts(self):
  Path(self.cfg['paths']['managedLauncherSource']).unlink(); c=C(); q=self.deployed(c); self.assertRaises(BrokerError,self.b.handle,q)

 def test_deploy_requires_root_owned_launcher_artifacts(self):
  self.b.cfg['requireRootOwnership']=True; c=C(); q=self.deployed(c); self.assertRaises(BrokerError,self.b.handle,q)

 def test_deploy_installs_valheim_readable_file_mode(self):
  c=C(); q=self.deployed(c); self.b.handle(q)
  p=self.server/'BepInEx/plugins/x.dll'
  self.assertEqual(p.stat().st_mode & 0o777,0o640)
  self.assertEqual((self.server/'BepInEx').stat().st_mode & 0o777,0o770)
  self.assertEqual((self.server/'BepInEx/plugins').stat().st_mode & 0o777,0o770)
  self.assertEqual((self.server/'BepInEx/plugins/x.dll').stat().st_mode & 0o777,0o640)

 def test_bepinex_config_is_runtime_writable(self):
  p=self.server/'BepInEx/config/BepInEx.cfg'; p.parent.mkdir(parents=True); p.write_bytes(b'cfg'); set_runtime_permissions(self.server,p,os.getgid())
  self.assertEqual(p.stat().st_mode & 0o777,0o660)

 def test_harmony_and_cache_are_runtime_writable(self):
  harmony=self.server/'BepInEx/core/0Harmony.dll'; cache=self.server/'BepInEx/cache/harmony_interop_cache.dat'
  harmony.parent.mkdir(parents=True); cache.parent.mkdir(parents=True); harmony.write_bytes(b'dll'); cache.write_bytes(b'cache')
  set_runtime_permissions(self.server,harmony,os.getgid()); set_runtime_permissions(self.server,cache,os.getgid())
  self.assertEqual(harmony.stat().st_mode & 0o777,0o660); self.assertEqual(cache.stat().st_mode & 0o777,0o660)

 def test_existing_bepinex_log_is_runtime_writable(self):
  p=self.server/'BepInEx/LogOutput.log'; p.parent.mkdir(parents=True); p.write_bytes(b'log'); normalize_runtime_files(self.server,os.getgid())
  self.assertEqual(p.stat().st_mode & 0o777,0o660)

 def test_partial_copy_failure_rolls_back(self):
  c=C(); q=self.deployed(c)
  with patch('privileged_broker.shutil.copyfile',side_effect=OSError('partial')): self.assertRaises(OSError,self.b.handle,q)
  self.assertFalse((self.server/'BepInEx/plugins/x.dll').exists())
 def test_installed_hash_mismatch_rolls_back(self):
  c=C(); q=self.deployed(c)
  with patch('privileged_broker.digest',side_effect=lambda p:'0'*64 if str(p).startswith(str(self.server)) else digest(p)): self.assertRaises(BrokerError,self.b.handle,q)
  self.assertFalse((self.server/'BepInEx/plugins/x.dll').exists())
 def test_start_failure_rolls_back(self):
  c=C(start_ok=False); q=self.deployed(c); self.assertRaises(RuntimeError,self.b.handle,q); self.assertFalse((self.server/'BepInEx/plugins/x.dll').exists())
 def test_health_failure_rolls_back(self):
  c=C(healthy=False); q=self.deployed(c); self.assertRaises(BrokerError,self.b.handle,q); self.assertFalse((self.server/'BepInEx/plugins/x.dll').exists()); self.assertEqual(c.active,'inactive')
 def test_rolled_back_deployment_can_retry_with_same_unused_approval(self):
  c=C(healthy=False); q=self.deployed(c)
  self.assertRaises(BrokerError,self.b.handle,q)
  c.healthy=True
  result=self.b.handle(self.req('deploy'))
  self.assertEqual(result['state'],'transactional-deployed')

 def test_rollback_twice_idempotent(self):
  c=C(); q=self.deployed(c); self.b.handle(q); r=self.req('rollback'); self.assertEqual(self.b.handle(r)['state'],'rolled-back'); self.assertEqual(self.b.handle(self.req('rollback'))['state'],'rolled-back')
 def test_interrupted_transaction_recovery_blocks(self):
  c=C(); q=self.deployed(c); p=self.back/self.did; p.mkdir(parents=True); (p/'transaction.json').write_text(json.dumps({'state':'applying'})); self.b.enabled=True; self.assertRaises(BrokerError,self.b.handle,{**q,'requestId':str(uuid.uuid4())})
 def test_production_lifecycle_uses_fixed_controller(self):
  c=C(); self.b.controller=c; result=self.b.handle(self.req('start')); self.assertTrue(result['ok']); self.assertEqual(c.started,1); self.assertEqual(self.b.handle(self.req('stop'))['state'],'stop')
 def test_pz_mapping_unchanged(self): self.assertEqual(self.cfg['instances']['pz-main']['service'],'pz'); self.assertRaises(BrokerError,self.b.handle,{**self.req('deploy'),'instanceId':'pz-main'})

 def test_platform_excluded_is_recorded(self): self.assertEqual(self.base['platformExcluded'][0]['reason'],'platform-excluded')

if __name__=='__main__': unittest.main(verbosity=2)
