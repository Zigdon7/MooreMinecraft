"""Original static sailing-ship example; all blocks use default states."""
import gzip, struct
from pathlib import Path
from make_fixture import tag, integer

def build(path=None):
    size=(9,13,22)
    names=['air','dark_oak_planks','oak_planks','oak_log','glass','white_wool','red_wool','gold_block']
    cells={}
    def put(x,y,z,name): cells[x,y,z]=names.index(name)
    for z in range(22):
        half=0 if z==0 else 1 if z==1 else 2 if z in (2,19,20,21) else 3
        # Flat keel/hull meets the water surface; deck is one block above it.
        for x in range(4-max(0,half-1),5+max(0,half-1)):
            put(x,0,z,'dark_oak_planks')
        for x in range(4-half,5+half):
            put(x,1,z,'dark_oak_planks' if abs(x-4)==half else 'oak_planks')
        if half and 3<=z<=18:
            for x in (4-half,4+half): put(x,2,z,'dark_oak_planks')
    for z in range(4): put(4,3,z,'oak_planks')
    # Stern cabin, glass windows, and a two-block-high doorway facing the bow.
    for z in range(17,22):
        for x in range(2,7):
            for y in range(2,5):
                if x in (2,6) or z in (17,21):
                    name='glass' if y==3 and (x in (2,6) and z==19 or z==21 and x in (3,5)) else 'oak_planks'
                    put(x,y,z,name)
            put(x,5,z,'dark_oak_planks')
    for y in (2,3): cells.pop((4,y,17),None)
    # Mast and crossbeam; rectangular main sail and a triangular forward jib.
    for y in range(2,13): put(4,y,8,'oak_log')
    for x in range(1,8): put(x,10,8,'oak_planks')
    for y in range(4,10):
        span=2 if y==9 else 3
        for x in range(4-span,5+span): put(x,y,9,'white_wool')
    for y in range(4,9):
        for z in range(8-(9-y),8): put(4,y,z,'white_wool')
    put(5,12,8,'red_wool'); put(6,12,8,'red_wool')
    put(4,2,16,'gold_block')
    w,h,l=size
    assert all(0<=x<w and 0<=y<h and 0<=z<l for x,y,z in cells)
    palette=b''.join(tag(3,'minecraft:'+s,integer(i)) for i,s in enumerate(names))+b'\x00'
    data=bytes(cells.get((x,y,z),0) for y in range(h) for z in range(l) for x in range(w))
    payload=(tag(3,'Version',integer(2))+tag(3,'DataVersion',integer(5023))+
        b''.join(tag(2,k,struct.pack('>h',v)) for k,v in zip(('Width','Height','Length'),size))+
        tag(11,'Offset',integer(3)+integer(0)*3)+tag(3,'PaletteMax',integer(len(names)))+
        tag(10,'Palette',palette)+tag(7,'BlockData',integer(len(data))+data)+
        tag(9,'BlockEntities',b'\x0a'+integer(0))+b'\x00')
    path=path or Path(__file__).with_name('ship.schem')
    path.write_bytes(gzip.compress(tag(10,'Schematic',payload),mtime=0))
    print(f'{path}: {len(cells)} blocks, dimensions={size}')
    return path

if __name__=='__main__': build()
