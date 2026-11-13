import unittest
import tempfile
import subprocess,sys
from pathlib import Path
from import_structure import decode,load,state
from make_fixture import build

class ImportTests(unittest.TestCase):
    def test_surface_plan_requires_loaded_only(self):
        root=Path(__file__).parent
        args=[sys.executable,str(root/'import_structure.py'),str(root/'ship.schem'),'--origin','-1216','64','1109','--surface-site']
        rejected=subprocess.run(args,capture_output=True,text=True)
        self.assertNotEqual(rejected.returncode,0)
        self.assertIn('--surface-site requires --loaded-only',rejected.stderr)
        accepted=subprocess.run(args+['--loaded-only'],capture_output=True,text=True)
        self.assertEqual(accepted.returncode,0,accepted.stderr)
    def test_production_requires_loaded_only_and_exclusive_target(self):
        root=Path(__file__).parent
        base=[sys.executable,str(root/'import_structure.py'),str(root/'ship.schem'),'--apply-production']
        for extra in ([],['--loaded-only','--apply-staging'],['--loaded-only','--test-scratch']):
            result=subprocess.run(base+extra,capture_output=True,text=True)
            self.assertNotEqual(result.returncode,0)
            self.assertIn('Production requires --loaded-only',result.stderr)
    def test_ship_keel_cabin_and_sail(self):
        from make_ship import build as ship
        with tempfile.TemporaryDirectory() as folder:
            size,blocks=decode(load(ship(Path(folder)/'ship.schem')))
        self.assertEqual(size,[9,13,22])
        cells=dict(blocks)
        for z in range(22): self.assertEqual(cells[(4,0,z)],'minecraft:dark_oak_planks')
        for y in (2,3): self.assertNotIn((4,y,17),cells)
        self.assertEqual(cells[(4,9,9)],'minecraft:white_wool')
        self.assertEqual(cells[(6,12,8)],'minecraft:red_wool')
        self.assertTrue(all('[' not in s for s in cells.values()))
    def test_village_walkable_doorways(self):
        from make_village import build as village
        with tempfile.TemporaryDirectory() as folder:
            size,blocks=decode(load(village(Path(folder)/'village.schem')))
        self.assertEqual(size,[24,10,24])
        cells=dict(blocks)
        self.assertEqual(sum(s=='minecraft:crafting_table' for s in cells.values()),3)
        for x,z in [(4,7),(18,7),(7,18)]:
            self.assertIn((x,0,z),cells)
            for y in (1,2,3): self.assertNotIn((x,y,z),cells)
        self.assertTrue(all('[' not in s for s in cells.values()))
    def test_fixture(self):
        build(); size,blocks=decode(load(Path(__file__).with_name('gatehouse.schem')))
        self.assertEqual(size,[13,9,7]); self.assertGreater(len(blocks),300)
        self.assertTrue(any(s=='minecraft:oak_planks' for _,s in blocks))
        self.assertEqual(len({p for p,s in blocks}),len(blocks))
    def test_command_injection(self):
        for s in ['minecraft:stone\nop bad','minecraft:stone;stop','minecraft:command_block']:
            with self.assertRaises(ValueError): state(s)
    def test_block_entity_rejected(self):
        with self.assertRaises(ValueError):
            decode({'palette':[{'Name':'minecraft:chest'}],'size':[1,1,1],'blocks':[{'pos':[0,0,0],'state':0,'nbt':{}}]})
    def test_bounds(self):
        with self.assertRaises(ValueError):
            decode({'palette':[{'Name':'minecraft:stone'}],'size':[1,1,1],'blocks':[{'pos':[1,0,0],'state':0}]})
    def test_sponge_v3(self):
        size,b=decode({'Schematic':{'Version':3,'Width':1,'Height':1,'Length':1,'Blocks':{'Palette':{'minecraft:stone':0},'Data':b'\x00'}}})
        self.assertEqual(b,[((0,0,0),'minecraft:stone')])
    def test_bad_varint(self):
        with self.assertRaises(ValueError):
            decode({'Version':2,'Width':1,'Height':1,'Length':1,'Palette':{'minecraft:stone':0},'BlockData':b'\x80'})

if __name__=='__main__': unittest.main()
