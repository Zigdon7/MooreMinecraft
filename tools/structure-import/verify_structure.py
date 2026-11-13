"""Read-only exact block checks on an explicitly selected live console."""
import argparse, hashlib, json
from pathlib import Path
import import_structure as imp

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('file',type=Path)
    p.add_argument('--origin',nargs=3,type=int,required=True)
    p.add_argument('--target',choices=['production','staging','scratch'],required=True)
    p.add_argument('--water-y',type=int)
    p.add_argument('--output',type=Path,default=imp.ROOT/'runtime'/'verification.json')
    a=p.parse_args()
    imp.SOCKET,imp.SESSION={'production':('pumpkin','pumpkin'),'staging':('saturn-stage','atlas'),'scratch':('structure-test','prototype')}[a.target]
    size,blocks=imp.decode(imp.load(a.file))
    x,y,z=a.origin
    for i,(pos,state) in enumerate(blocks,1):
        at=f'{x+pos[0]} {y+pos[1]} {z+pos[2]}'
        imp.require_predicate(f'execute if loaded {at} if block {at} {state}')
        if i%100==0: print(f'Verified {i}/{len(blocks)} blocks',flush=True)
    water=0
    if a.water_y is not None:
        for dz in range(size[2]):
            for dx in range(size[0]):
                at=f'{x+dx} {a.water_y} {z+dz}'
                imp.require_predicate(f'execute if loaded {at} if block {at} minecraft:water')
                water+=1
    result={'target':a.target,'origin':a.origin,'size':size,'verified_blocks':len(blocks),
            'water_y':a.water_y,'verified_water_cells':water,
            'schematic_sha256':hashlib.sha256(a.file.read_bytes()).hexdigest()}
    a.output.parent.mkdir(parents=True,exist_ok=True)
    a.output.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result),flush=True)

if __name__=='__main__': main()
