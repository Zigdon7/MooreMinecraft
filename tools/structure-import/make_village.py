"""Original compact three-cottage example; no entities or stateful blocks."""
import gzip, struct
from pathlib import Path
from make_fixture import tag, integer

def build(path=None):
    size=(24,10,24)
    names=['air','cobblestone','oak_planks','oak_log','glass','dark_oak_planks',
           'stone_bricks','moss_block','blue_stained_glass','crafting_table']
    palette=['minecraft:'+s for s in names]
    cells={}
    def put(x,y,z,name): cells[x,y,z]=names.index(name)
    # Walkable foundation and a cross-shaped plaza/path network.
    for z in range(24):
        for x in range(24):
            put(x,0,z,'stone_bricks' if 10<=x<=13 or 10<=z<=13 else 'moss_block')
    for ox,oz in [(1,1),(15,1),(1,15)]:
        for z in range(7):
            for x in range(7):
                put(ox+x,0,oz+z,'oak_planks')
                for y in range(1,5):
                    if x in (0,6) or z in (0,6):
                        name='oak_log' if x in (0,6) and z in (0,6) else 'oak_planks'
                        if y in (2,3) and (x in (0,6) and z==3 or z in (0,6) and x==3):
                            name='glass'
                        put(ox+x,y,oz+z,name)
        # Open doorways; the importer never clears real-world blocks.
        if oz==15:
            door=[(ox+6,y,oz+3) for y in range(1,4)]
        else:
            door=[(ox+3,y,oz+6) for y in range(1,4)]
        for p in door: cells.pop(p,None)
        for x in range(-1,8):
            height=5+min(x+1,7-x)
            for z in range(-1,8): put(ox+x,height,oz+z,'dark_oak_planks')
            if 0<=x<=6:
                for z in (0,6):
                    for y in range(5,height): put(ox+x,y,oz+z,'oak_planks')
        put(ox+1,1,oz+1,'crafting_table')
    # Decorative covered well: glass water avoids fluid-update side effects.
    for z in range(16,21):
        for x in range(16,21):
            put(x,1,z,'cobblestone' if x in (16,20) or z in (16,20) else 'blue_stained_glass')
            put(x,5,z,'dark_oak_planks')
    for x,z in [(16,16),(20,16),(16,20),(20,20)]:
        for y in range(2,5): put(x,y,z,'oak_log')
    w,h,l=size
    assert all(0<=x<w and 0<=y<h and 0<=z<l for x,y,z in cells)
    pal=b''.join(tag(3,s,integer(i)) for i,s in enumerate(palette))+b'\x00'
    data=bytes(cells.get((x,y,z),0) for y in range(h) for z in range(l) for x in range(w))
    payload=(tag(3,'Version',integer(2))+tag(3,'DataVersion',integer(5023))+
        b''.join(tag(2,k,struct.pack('>h',v)) for k,v in zip(('Width','Height','Length'),size))+
        tag(11,'Offset',integer(3)+integer(0)*3)+tag(3,'PaletteMax',integer(len(palette)))+
        tag(10,'Palette',pal)+tag(7,'BlockData',integer(len(data))+data)+
        tag(9,'BlockEntities',b'\x0a'+integer(0))+b'\x00')
    path=path or Path(__file__).with_name('village.schem')
    path.write_bytes(gzip.compress(tag(10,'Schematic',payload),mtime=0))
    print(f'{path}: {len(cells)} blocks, dimensions={size}')
    return path

if __name__=='__main__': build()
