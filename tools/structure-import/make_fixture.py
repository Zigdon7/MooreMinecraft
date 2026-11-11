"""Original miniature gatehouse, exported as a real Sponge v2 .schem."""
import gzip, struct
from pathlib import Path

def string(s):
    b=s.encode(); return struct.pack('>H',len(b))+b
def tag(t,name,payload): return bytes([t])+string(name)+payload
def integer(n): return struct.pack('>i',n)

def build():
    w,h,l=13,9,7
    states=['minecraft:air','minecraft:stone_bricks','minecraft:chiseled_stone_bricks','minecraft:oak_planks','minecraft:stone_brick_stairs[facing=north,half=bottom,shape=straight,waterlogged=false]']
    cells={}
    for z in range(l):
        for x in range(w):
            cells[x,0,z]=1
    for x0 in (0,9):
        for y in range(1,7):
            for z in range(l):
                for x in range(x0,x0+4):
                    if x in (x0,x0+3) or z in (0,l-1): cells[x,y,z]=1
        for z in range(l):
            for x in range(x0,x0+4):
                cells[x,6,z]=3
                if (x in (x0,x0+3) or z in (0,l-1)) and (x+z)%2==0:
                    cells[x,7,z]=2
    for x in range(4,9):
        for z in range(l): cells[x,5,z]=1; cells[x,6,z]=3
        cells[x,7,0]=3
    palette=b''.join(tag(3,s,integer(i)) for i,s in enumerate(states))+b'\x00'
    data=bytes(cells.get((x,y,z),0) for y in range(h) for z in range(l) for x in range(w))
    root=(tag(3,'Version',integer(2))+tag(3,'DataVersion',integer(5023))+
          b''.join(tag(2,k,struct.pack('>h',v)) for k,v in zip(('Width','Height','Length'),(w,h,l)))+
          tag(11,'Offset',integer(3)+integer(0)*3)+tag(3,'PaletteMax',integer(len(states)))+
          tag(10,'Palette',palette)+tag(7,'BlockData',integer(len(data))+data)+
          tag(9,'BlockEntities',b'\x0a'+integer(0))+b'\x00')
    path=Path(__file__).with_name('gatehouse.schem')
    path.write_bytes(gzip.compress(tag(10,'Schematic',root),mtime=0))
    print(path, 'non-air blocks:',len(cells))

if __name__=='__main__': build()
