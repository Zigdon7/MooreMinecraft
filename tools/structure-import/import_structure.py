#!/usr/bin/env python3
"""Small NBT/Sponge importer; live targets require explicit operator flags."""
import argparse, gzip, io, json, re, struct, subprocess, time
from pathlib import Path

ROOT = Path(__file__).resolve().parent
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

def console(cmd,delay=.12):
    subprocess.run(['tmux','-L',SOCKET,'send-keys','-t',SESSION,'-l',cmd],check=True)
    subprocess.run(['tmux','-L',SOCKET,'send-keys','-t',SESSION,'Enter'],check=True)
    time.sleep(delay)

def require_predicate(cmd):
    console(cmd,delay=.05)
    end=time.monotonic()+8
    while time.monotonic()<end:
        screen=subprocess.check_output(['tmux','-L',SOCKET,'capture-pane','-J','-pt',SESSION,'-S','-20'],text=True)
        if '$ '+cmd not in screen:
            time.sleep(.025); continue
        reply=screen.rsplit('$ '+cmd,1)[1]
        if 'Test passed' in reply: return
        if any(e in reply for e in ('Test failed','Unknown','Invalid','Incorrect','not loaded')):
            raise RuntimeError(f'Site preflight refused: {cmd}: {reply}')
        time.sleep(.025)
    raise RuntimeError(f'No predicate acknowledgement: {cmd}')

def main():
    global SOCKET, SESSION
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('file',type=Path); p.add_argument('--origin',nargs=3,type=int,default=[96,200,96])
    p.add_argument('--apply-staging',action='store_true')
    p.add_argument('--apply-production',action='store_true',help='Explicit operator-authorized production target; requires --loaded-only')
    p.add_argument('--test-scratch',action='store_true',help='Target isolated structure-test:prototype instead of existing staging')
    p.add_argument('--loaded-only',action='store_true',help='Require loaded chunks; do not change force-load registrations')
    p.add_argument('--surface-site',action='store_true',help='Permit a surveyed lower-altitude site; requires --loaded-only and empty-volume preflight')
    a=p.parse_args(); size,blocks=decode(load(a.file))
    if a.test_scratch: SOCKET,SESSION='structure-test','prototype'
    if a.apply_production:
        if a.apply_staging or a.test_scratch or not a.loaded_only:
            raise ValueError('Production requires --loaded-only and cannot be combined with staging/scratch')
        SOCKET=SESSION='pumpkin'
    x,y,z=a.origin
    if a.surface_site and not a.loaded_only:
        raise ValueError('--surface-site requires --loaded-only')
    min_y=-64 if a.surface_site else 160
    if not (-100000<=x<=100000 and min_y<=y and y+size[1]<=319 and -100000<=z<=100000):
        raise ValueError('Origin/height exceeds prototype bounds')
    commands=[f'setblock {x+pos[0]} {y+pos[1]} {z+pos[2]} {s} keep' for pos,s in blocks]
    plan={'source':str(a.file),'target':SOCKET,'origin':a.origin,'size':size,'blocks':len(blocks),'commands':commands}
    (ROOT/'last-plan.json').write_text(json.dumps(plan,indent=2))
    print(json.dumps({k:v for k,v in plan.items() if k!='commands'}),flush=True)
    if not (a.apply_staging or a.apply_production): return
    if any('[' in s for _,s in blocks):
        raise ValueError('This Pumpkin release cannot place state properties via setblock; refusing rather than losing orientation')
    # No arbitrary console input from files: all coordinates/state syntax validated above.
    if a.loaded_only:
        for cx in range(x//16,(x+size[0]-1)//16+1):
            for cz in range(z//16,(z+size[2]-1)//16+1):
                require_predicate(f'execute if loaded {cx*16} {y} {cz*16}')
        # A missing chunk reads as air on this host. Check loaded and air together
        # for every cell, including interior spaces, before any placement.
        checked=0; total=size[0]*size[1]*size[2]
        for dy in range(size[1]):
            for dz in range(size[2]):
                for dx in range(size[0]):
                    at=f'{x+dx} {y+dy} {z+dz}'
                    require_predicate(f'execute if loaded {at} if block {at} minecraft:air')
                    checked+=1
                    if checked%256==0: print(f'Preflight {checked}/{total} cells clear',flush=True)
        print('Loaded empty-volume preflight passed',flush=True)
    else:
        console(f'forceload add {x} {z} {x+size[0]-1} {z+size[2]-1}')
        time.sleep(2)
    try:
        for i,c in enumerate(commands):
            console(c)
            end=time.monotonic()+8
            while time.monotonic()<end:
                screen=subprocess.check_output(['tmux','-L',SOCKET,'capture-pane','-J','-pt',SESSION,'-S','-20'],text=True)
                if '$ '+c not in screen:
                    time.sleep(.025); continue
                reply=screen.rsplit('$ '+c,1)[1]
                if 'Changed the block at' in reply: break
                if any(e in reply for e in ('not loaded','Unknown','Invalid','Could not','could not','Failed','failed','Incorrect')):
                    raise RuntimeError(f'Block {i}: {reply}')
                time.sleep(.1)
            else: raise RuntimeError(f'No success acknowledgement for block {i}: {reply}')
        console('save-all')
        print(f'ACKNOWLEDGED {len(commands)} placements; save-all requested',flush=True)
    finally:
        if not a.loaded_only:
            console(f'forceload remove {x} {z} {x+size[0]-1} {z+size[2]-1}')

if __name__=='__main__': main()
