"""Console integration probes on isolated structure-test:prototype only."""
import json, re, subprocess, time
from pathlib import Path
import import_structure as imp
imp.SOCKET,imp.SESSION='structure-test','prototype'

def capture():
    return subprocess.check_output(['tmux','-L',imp.SOCKET,'capture-pane','-J','-pt',imp.SESSION,'-S','-1000'],text=True)

def command(c):
    imp.console(c)
    time.sleep(.2)
    return capture().rsplit('$ '+c,1)[-1].strip().removesuffix('$').strip()

def loaded(x=96,z=96):
    return 'Test passed' in command(f'execute if loaded {x} 200 {z}')

def wait_loaded(expected,seconds=90,x=96,z=96):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        if loaded(x,z)==expected: return True
        time.sleep(.5)
    return False

def main():
    import argparse
    p=argparse.ArgumentParser(); p.add_argument('mode',choices=['baseline','patched'])
    a=p.parse_args(); results={}
    results['players']=command('list')
    results['initial_unloaded']=not loaded()
    results['add']=command('forceload add 96 96')
    results['loaded_after_add']=wait_loaded(True,10 if a.mode=='baseline' else 90)
    if a.mode=='patched' and results['loaded_after_add']:
        results['duplicate_add']=command('forceload add 96 96')
        results['placement']=command('setblock 96 199 96 minecraft:stone_bricks keep')
        results['positive_predicate']=command('execute if block 96 199 96 minecraft:stone_bricks')
        results['negative_predicate']=command('execute if block 96 199 96 minecraft:diamond_block')
        results['summon_tick_probe']=command('summon minecraft:item 98 210 98 {Item:{id:"minecraft:stone",count:1},Age:0s,NoGravity:1b,PickupDelay:32767s}')
        results['age_before']=command('data get entity @e[type=minecraft:item,limit=1] Age')
        time.sleep(3)
        results['age_after']=command('data get entity @e[type=minecraft:item,limit=1] Age')
        before=re.search(r': (\d+)s',results['age_before'])
        after=re.search(r': (\d+)s',results['age_after'])
        results['entity_ticks_no_players']=bool(before and after and int(after[1])>int(before[1]))
        results['remove_tick_probe']=command('kill @e[type=minecraft:item]')
        results['retained_no_players']=loaded()
        results['remove']=command('forceload remove 96 96')
        results['unloaded_after_single_remove']=wait_loaded(False,30)
        results['negative_add']=command('forceload add -96 -96')
        results['negative_loaded']=wait_loaded(True,90,-96,-96)
        results['remove_all']=command('forceload remove all')
        results['negative_unloaded']=wait_loaded(False,30,-96,-96)
    else:
        results['remove']=command('forceload remove all')
    root=Path(__file__).parent
    (root/f'loader-{a.mode}.json').write_text(json.dumps(results,indent=2))
    (root/f'loader-{a.mode}-console.txt').write_text(capture())
    print(json.dumps(results,indent=2),flush=True)
    if a.mode=='patched':
        for k in ['initial_unloaded','loaded_after_add','retained_no_players','unloaded_after_single_remove','negative_loaded','negative_unloaded','entity_ticks_no_players']:
            if not results.get(k): raise SystemExit('FAILED: '+k)

if __name__=='__main__': main()
