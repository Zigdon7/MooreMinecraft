#!/usr/bin/env python3
"""Small, fail-closed NBT/Sponge importer. No plugin, no production target."""
import argparse, gzip, io, json, re, struct, subprocess, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
LOG = Path('/home/mc/.openclaw/workspace/saturn-lodestones/staging/logs/latest.log')
SOCKET = 'saturn-stage'
SESSION = 'atlas'

class NBT:
    def __init__(self, data): self.f = io.BytesIO(data)
    def read(self, n):
        b = self.f.read(n)
        if len(b) != n: raise ValueError('Truncated NBT')
        return b
    def num(self, fmt): return struct.unpack('>'+fmt, self.read(struct.calcsize('>'+fmt)))[0]
    def string(self): return self.read(self.num('H')).decode('utf-8')
    def count(self):
        n = self.num('i')
        if not 0 <= n <= 2_000_000: raise ValueError('NBT collection too large')
        return n
    def tag(self, t, depth=0):
        if depth > 32: raise ValueError('NBT nesting too deep')
        if t in range(1,7): return self.num({1:'b',2:'h',3:'i',4:'q',5:'f',6:'d'}[t])
        if t == 7: return self.read(self.count())
        if t == 8: return self.string()
        if t == 9:
            k = self.num('B'); return [self.tag(k,depth+1) for _ in range(self.count())]
        if t == 10:
            d = {}
            while (k := self.num('B')):
                name = self.string()
                if name in d: raise ValueError('Duplicate NBT key')
                d[name] = self.tag(k,depth+1)
            return d
        if t in (11,12): return [self.num('i' if t==11 else 'q') for _ in range(self.count())]
        raise ValueError('Unsupported NBT tag')
    def root(self):
        if self.num('B') != 10: raise ValueError('Expected compound')
        self.string(); return self.tag(10)

def load(path):
    if path.stat().st_size > 8_000_000: raise ValueError('Input too large')
    with path.open('rb') as f:
        compressed = f.read(2) == b'\x1f\x8b'
    with (gzip.open(path,'rb') if compressed else path.open('rb')) as f:
        raw = f.read(16_000_001)
    if len(raw)>16_000_000: raise ValueError('Expanded input too large')
    return NBT(raw).root()

def state(s):
    if not re.fullmatch(r'minecraft:[a-z0-9_]+(?:\[[a-z0-9_=,]+\])?',s):
        raise ValueError('Invalid/unsupported block state')
    if any(x in s.split('[')[0] for x in ('command_block','structure_block','jigsaw')):
        raise ValueError('Executable/structure blocks not supported')
    return s

def decode(d):
    d = d.get('Schematic',d)
    if 'palette' in d:
        size = list(map(int,d['size']))
        if d.get('entities') or any('nbt' in b for b in d['blocks']):
            raise ValueError('Entities/block entities not supported in prototype')
        palette = [state(p['Name']+('['+','.join(k+'='+v for k,v in sorted(p.get('Properties',{}).items()))+']' if p.get('Properties') else '')) for p in d['palette']]
        if any(not 0 <= b['state'] < len(palette) for b in d['blocks']):
            raise ValueError('Invalid palette index')
        blocks = [(tuple(b['pos']),palette[b['state']]) for b in d['blocks']]
    else:
        if d.get('Version') not in (2,3): raise ValueError('Only Sponge schematic v2/v3 and Java structure NBT supported')
        size = [int(d[k]) for k in ('Width','Height','Length')]
        b = d.get('Blocks',d)
        if d.get('Entities') or b.get('BlockEntities'):
            raise ValueError('Entities/block entities not supported in prototype')
        palette = {int(v):state(k) for k,v in b['Palette'].items()}
        ids=[]; value=shift=0
        for byte in b.get('Data',b.get('BlockData',b'')):
            value |= (byte&127)<<shift
            if byte&128:
                shift+=7
                if shift>28: raise ValueError('Bad VarInt')
            else: ids.append(value); value=shift=0
        if shift or len(ids)!=size[0]*size[1]*size[2]: raise ValueError('Invalid block data length')
        w,h,l=size
        blocks=[((i%w,i//(w*l),(i//w)%l),palette[v]) for i,v in enumerate(ids)]
    if len(size)!=3 or any(n<1 or n>128 for n in size) or len(blocks)>10000:
        raise ValueError('Prototype limits: 128 per axis, 10000 blocks')
    seen=set()
    for pos,s in blocks:
        if len(pos)!=3 or any(not 0<=v<size[i] for i,v in enumerate(pos)) or pos in seen:
            raise ValueError('Invalid/duplicate block position')
        seen.add(pos)
    return size,[(p,s) for p,s in blocks if s not in ('minecraft:air','minecraft:cave_air','minecraft:void_air')]

def console(cmd):
    subprocess.run(['tmux','-L',SOCKET,'send-keys','-t',SESSION,'-l',cmd],check=True)
    subprocess.run(['tmux','-L',SOCKET,'send-keys','-t',SESSION,'Enter'],check=True)
    time.sleep(.12)

def main():
    global SOCKET, SESSION
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('file',type=Path); p.add_argument('--origin',nargs=3,type=int,default=[96,200,96])
    p.add_argument('--apply-staging',action='store_true')
    p.add_argument('--test-scratch',action='store_true',help='Target isolated structure-test:prototype instead of existing staging')
    a=p.parse_args(); size,blocks=decode(load(a.file))
    if a.test_scratch: SOCKET,SESSION='structure-test','prototype'
    x,y,z=a.origin
    if not (0<=x<=100000 and 160<=y and y+size[1]<=319 and 0<=z<=100000):
        raise ValueError('Prototype restricted to high-altitude staging plots')
    commands=[f'setblock {x+pos[0]} {y+pos[1]} {z+pos[2]} {s} keep' for pos,s in blocks]
    plan={'source':str(a.file),'origin':a.origin,'size':size,'blocks':len(blocks),'commands':commands}
    (ROOT/'last-plan.json').write_text(json.dumps(plan,indent=2))
    print(json.dumps({k:v for k,v in plan.items() if k!='commands'}),flush=True)
    if not a.apply_staging: return
    if any('[' in s for _,s in blocks):
        raise ValueError('This Pumpkin release cannot place state properties via setblock; refusing rather than losing orientation')
    # No arbitrary console input from files: all coordinates/state syntax validated above.
    console(f'forceload add {x} {z} {x+size[0]-1} {z+size[2]-1}')
    time.sleep(2)
    try:
        for i,c in enumerate(commands):
            console(c)
            end=time.monotonic()+8
            while time.monotonic()<end:
                screen=subprocess.check_output(['tmux','-L',SOCKET,'capture-pane','-J','-pt',SESSION,'-S','-20'],text=True)
                reply=screen.rsplit('$ '+c,1)[-1]
                if 'Changed the block at' in reply: break
                if any(e in reply for e in ('not loaded','Unknown','Invalid','Could not','could not','Failed','failed','Incorrect')):
                    raise RuntimeError(f'Block {i}: {reply}')
                time.sleep(.1)
            else: raise RuntimeError(f'No success acknowledgement for block {i}: {reply}')
        console('save-all')
        print(f'ACKNOWLEDGED {len(commands)} placements; save-all requested',flush=True)
    finally:
        console(f'forceload remove {x} {z} {x+size[0]-1} {z+size[2]-1}')

if __name__=='__main__': main()
