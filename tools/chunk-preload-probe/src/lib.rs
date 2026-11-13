//! ISOLATED DISPOSABLE WORLDS ONLY: writes two high-altitude marker blocks.
use pumpkin_plugin_api::{Context,Plugin,PluginMetadata,Server,common::BlockPos,
    scheduler::SchedulerExt,register_plugin};
use pumpkin_plugin_api::wit::pumpkin::plugin::world::BlockFlags;
struct Probe;
fn phase(server:Server, initial:bool) {
    assert!(server.get_all_players().is_empty(),"Probe requires zero players");
    let world=server.get_all_worlds().into_iter().find(|w|w.get_dimension()=="minecraft:overworld").unwrap();
    for (x,z) in [(96,96),(-96,-96)] {
        assert!(world.get_chunk(x,z).is_none(),"Test chunk must be unloaded before preload");
        let _chunk=world.load_chunk(x,z);
        assert!(world.get_chunk(x,z).is_some(),"Preload did not make chunk readable");
        let pos=BlockPos{x:x*16+1,y:200,z:z*16+1};
        if initial { assert!(world.set_block_by_name(pos,"minecraft:lodestone",BlockFlags::empty())); }
        let state=world.get_block_state(pos);
        assert_eq!(state.block_name.trim_start_matches("minecraft:"),"lodestone","Stored marker did not survive reload");
        // Duplicate lease: returning must not consume another caller's hold.
        let _second=world.load_chunk(x,z);
        assert!(world.get_chunk(x,z).is_some());
    }
    tracing::info!("PRELOAD_PROBE_OK phase={} positive_negative=true zero_players=true",if initial{1}else{2});
}
impl Plugin for Probe {
 fn new()->Self{Self}
 fn metadata(&self)->PluginMetadata{PluginMetadata{name:"saturn-chunk-preload-probe".into(),version:"0.1.0".into(),authors:vec!["Saturn".into()],description:"Disposable-world chunk loading regression probe; never install live".into(),dependencies:vec![],permissions:vec![]}}
 fn on_load(&self,c:Context)->Result<(),String>{c.schedule_delayed_task(20,|s|phase(s,true));c.schedule_delayed_task(800,|s|phase(s,false));Ok(())}
 fn on_unload(&self,_:Context)->Result<(),String>{Ok(())}
}
register_plugin!(Probe);
