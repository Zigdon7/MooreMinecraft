"""Verify every imported block through server predicates, with a negative control."""
import argparse,json,time
from pathlib import Path
from import_structure import decode,load
from probe_loader import command,wait_loaded

p=argparse.ArgumentParser(); p.add_argument('--label',required=True); a=p.parse_args()
root=Path(__file__).parent
size,blocks=decode(load(root/'gatehouse.schem'))
print(command('forceload add 96 96 108 102'),flush=True)
if not wait_loaded(True): raise SystemExit('Plot not loaded')
negative=command('execute if block 96 200 96 minecraft:diamond_block')
if 'Test failed' not in negative: raise SystemExit('Negative control failed: '+negative)
failed=[]
for i,(pos,s) in enumerate(blocks,1):
    x,y,z=[o+v for o,v in zip((96,200,96),pos)]
    reply=command(f'execute if block {x} {y} {z} {s}')
    if 'Test passed' not in reply: failed.append({'pos':[x,y,z],'expected':s,'reply':reply})
    if i%100==0: print(f'Checked {i}/{len(blocks)}; mismatches={len(failed)}',flush=True)
result={'label':a.label,'checked':len(blocks),'failed':failed,'negative_control':negative,'players':command('list')}
(root/f'verification-{a.label}.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result),flush=True)
if failed: raise SystemExit(1)
