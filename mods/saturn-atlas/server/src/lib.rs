//! Saturn Atlas + Lodestones: persistent state feasibility milestone.
//! Not a completed gameplay release. No teleport or inventory mutations yet.
mod game;
use pumpkin_plugin_api::{Context, Plugin, PluginMetadata, register_plugin};
use saturn_atlas_core::Atlas;
use serde::{Deserialize, Serialize};
use std::{
    io::Write,
    path::PathBuf,
    sync::{Arc, Mutex},
};
#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Save {
    schema: u32,
    atlas: Atlas,
    #[serde(default)]
    tiles: std::collections::BTreeMap<String, game::Tile>,
    #[serde(default)]
    pending: std::collections::BTreeMap<String, game::Payment>,
}
impl Default for Save {
    fn default() -> Self {
        Self {
            schema: 1,
            atlas: Atlas::default(),
            tiles: Default::default(),
            pending: Default::default(),
        }
    }
}
struct Store {
    path: PathBuf,
    state: Save,
}
impl Store {
    fn load(path: PathBuf) -> Result<Self, String> {
        let state = match std::fs::read_to_string(&path) {
            Ok(raw) => serde_json::from_str::<Save>(&raw)
                .map_err(|e| format!("Refusing corrupt Atlas store: {e}"))?,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Save::default(),
            Err(e) => return Err(format!("Cannot read Atlas store: {e}")),
        };
        if state.schema != 1 {
            return Err(format!(
                "Unsupported Atlas schema {}; refusing overwrite",
                state.schema
            ));
        }
        Ok(Self { path, state })
    }
    fn save(&self) -> Result<(), String> {
        let data = serde_json::to_vec(&self.state).map_err(|e| e.to_string())?;
        let temp = self.path.with_extension("json.new");
        let mut file = std::fs::File::create(&temp).map_err(|e| e.to_string())?;
        file.write_all(&data)
            .and_then(|_| file.sync_all())
            .map_err(|e| e.to_string())?;
        std::fs::rename(&temp, &self.path).map_err(|e| e.to_string())?;
        Ok(())
    }
}
pub struct SaturnAtlas {
    store: Mutex<Option<game::Shared>>,
}
impl Plugin for SaturnAtlas {
    fn new() -> Self {
        Self {
            store: Mutex::new(None),
        }
    }
    fn metadata(&self) -> PluginMetadata {
        PluginMetadata {
            name: "saturn-atlas".into(),
            version: env!("CARGO_PKG_VERSION").into(),
            authors: vec!["Saturn".into()],
            description: "Staging Shared Atlas and Lodestone gameplay prototype".into(),
            dependencies: vec![],
            permissions: vec!["fs.read.data".into(), "fs.write.data".into()],
        }
    }
    fn on_load(&self, context: Context) -> Result<(), String> {
        let folder = PathBuf::from(context.get_data_folder());
        std::fs::create_dir_all(&folder).map_err(|e| e.to_string())?;
        let store = Store::load(folder.join("atlas.json"))?;
        store.save()?;
        let restored = Store::load(store.path.clone())?;
        tracing::info!(
            "SATURN_ATLAS_STORAGE_OK schema={} anchors={} explored_chunks={}",
            restored.state.schema,
            restored.state.atlas.anchors.len(),
            restored.state.atlas.explored.len()
        );
        let shared = Arc::new(Mutex::new(game::Runtime {
            store,
            quotes: Default::default(),
            queue: Default::default(),
            queued: Default::default(),
            views: Default::default(),
            serial: 0,
            dirty: false,
        }));
        let mut identities = std::collections::BTreeSet::new();
        for world in context.get_server().get_all_worlds() {
            let id = world.get_id();
            if !identities.insert(id) {
                return Err("Host reports ambiguous dimension identities; restrict this staging build to one dimension".into());
            }
        }
        game::setup(&context, shared.clone())?;
        *self.store.lock().map_err(|e| e.to_string())? = Some(shared);
        Ok(())
    }
    fn on_unload(&self, _: Context) -> Result<(), String> {
        if let Some(store) = self.store.lock().map_err(|e| e.to_string())?.as_ref() {
            store.lock().map_err(|e| e.to_string())?.store.save()?
        };
        Ok(())
    }
}
register_plugin!(SaturnAtlas);
