#!/usr/bin/env python3
"""Build pinned staging mod manifest from actual local release artifacts."""
import argparse, hashlib, json, pathlib, re, zipfile
p=argparse.ArgumentParser();p.add_argument('--artifacts',type=pathlib.Path,required=True);p.add_argument('--tag',required=True);p.add_argument('--output',type=pathlib.Path,required=True);a=p.parse_args()
if not re.fullmatch(r'staging-v\d+\.\d+\.\d+',a.tag):raise SystemExit('Only versioned staging tags are supported')
mods=[]
for file in sorted(a.artifacts.glob('*.jar')):
 with zipfile.ZipFile(file) as z:
  meta=json.loads(z.read('fabric.mod.json'))
 mods.append({'id':meta['id'],'repo':'Zigdon7/MooreMinecraft','tag':a.tag,'asset':file.name,'sha256':hashlib.sha256(file.read_bytes()).hexdigest(),'size':file.stat().st_size})
if {m['id'] for m in mods}!={'saturn_atlas','fabric-api'}:raise SystemExit('Expected exactly Atlas companion and Fabric API')
manifest={'schema':1,'packId':'saturn','version':a.tag.removeprefix('staging-v')+'-staging','minecraft':'26.3','fabricLoader':'0.20.1','mods':mods}
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(manifest,indent=2)+'\n')
print(a.output)
