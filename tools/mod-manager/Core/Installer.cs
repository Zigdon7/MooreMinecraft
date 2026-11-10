using System.IO.Compression;
using System.Text.Json;
namespace Saturn.Manager;
public sealed class Installer{
 readonly string profile,mods,root;readonly IArtifacts source;readonly Action<string> log;
 public Installer(string profile,IArtifacts source,Action<string>? log=null){this.profile=Path.GetFullPath(profile);if(!Directory.Exists(this.profile))throw new DirectoryNotFoundException("Choose an existing Minecraft game/profile folder");mods=Path.Combine(this.profile,"mods");root=Path.Combine(this.profile,".saturn-manager");this.source=source;this.log=log??(_=>{});Rules.NoLink(mods);Rules.NoLink(root);Rules.NoLink(Path.Combine(root,"transactions"));Directory.CreateDirectory(root);Directory.CreateDirectory(mods);}
 string State=>Path.Combine(root,"installed.json");
 FileStream Lock()=>new(Path.Combine(root,"manager.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
 static string? ModId(string path){try{using var zip=ZipFile.OpenRead(path);var entry=zip.GetEntry("fabric.mod.json");if(entry is null||entry.Length>65536)return null;using var s=entry.Open();using var doc=JsonDocument.Parse(s);return doc.RootElement.TryGetProperty("id",out var id)?id.GetString():null;}catch(InvalidDataException){return null;}catch(JsonException){return null;}}
 public async Task Install(PackManifest pack,CancellationToken token=default,Action<int>? afterMutation=null){
  pack.Validate();using var guard=Lock();RecoverUnsafe();Installed? old=File.Exists(State)?Rules.Read<Installed>(State):null;
  if(old is not null&&old.PackId!=pack.PackId)throw new InvalidDataException("This profile is already managed by another pack");
  var existing=new Dictionary<string,(string Id,string Hash)>(StringComparer.OrdinalIgnoreCase);
  if(old is not null)foreach(var file in old.Files){if(!Rules.Asset(file.Name)||!Rules.Hash(file.Hash)||!Rules.Id(file.Id))throw new InvalidDataException("Invalid installed state");var path=Path.Combine(mods,file.Name);Rules.NoLink(path);if(File.Exists(path)){var hash=Rules.Digest(path);if(!hash.Equals(file.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Managed file changed locally; refusing to overwrite: "+file.Name);existing.Add(file.Name,(file.Id,hash));}}
  // Migrate prior manual Saturn/Fabric installs by matching their declared mod IDs, not guessed filename prefixes.
  var desiredIds=pack.Mods.Select(m=>m.Id).ToHashSet(StringComparer.Ordinal);
  foreach(var path in Directory.EnumerateFiles(mods).Where(p=>Path.GetExtension(p).Equals(".jar",StringComparison.OrdinalIgnoreCase))){Rules.NoLink(path);var name=Path.GetFileName(path);if(existing.ContainsKey(name))continue;var id=ModId(path);if(id is not null&&desiredIds.Contains(id)){if(!Rules.Asset(name))throw new IOException("Cannot safely migrate filename: "+name);existing.Add(name,(id,Rules.Digest(path)));}}
  var txn=Path.Combine(root,"transactions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(txn,"downloads"));Directory.CreateDirectory(Path.Combine(txn,"backup"));
  var desired=new Dictionary<string,ModSpec>(StringComparer.OrdinalIgnoreCase);
  foreach(var mod in pack.Mods){token.ThrowIfCancellationRequested();desired.Add(mod.Asset,mod);var target=Path.Combine(mods,mod.Asset);Rules.NoLink(target);
   if(File.Exists(target)&&!existing.ContainsKey(mod.Asset))throw new IOException("Unmanaged file conflicts with the requested asset: "+mod.Asset);
   if(existing.TryGetValue(mod.Asset,out var found)&&found.Hash.Equals(mod.Sha256,StringComparison.OrdinalIgnoreCase))continue;
   var file=Path.Combine(txn,"downloads",mod.Asset);log("Downloading "+mod.Asset);await source.Download(mod,file,token);
   if(new FileInfo(file).Length!=mod.Size||!Rules.Digest(file).Equals(mod.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Checksum/size verification failed: "+mod.Asset);
   if(ModId(file)!=mod.Id)throw new InvalidDataException("Downloaded JAR has the wrong Fabric mod ID: "+mod.Asset);
  }
  var names=existing.Keys.Union(desired.Keys,StringComparer.OrdinalIgnoreCase);
  var journal=new Journal{HadState=File.Exists(State)};
  foreach(var name in names){string? before=existing.TryGetValue(name,out var b)?b.Hash:null;string? after=desired.TryGetValue(name,out var a)?a.Sha256.ToLowerInvariant():null;if(before!=after)journal.Changes.Add(new Change(name,before,after));}
  if(journal.HadState)File.Copy(State,Path.Combine(txn,"previous-state.json"));Rules.Write(Path.Combine(txn,"journal.json"),journal);
  try{
   int mutations=0;
   foreach(var change in journal.Changes){token.ThrowIfCancellationRequested();var target=Path.Combine(mods,change.Name);
    if(change.Before is not null){if(!File.Exists(target)||Rules.Digest(target)!=change.Before)throw new IOException("Profile changed during install: "+change.Name);File.Move(target,Path.Combine(txn,"backup",change.Name));afterMutation?.Invoke(++mutations);}
    if(change.After is not null){if(File.Exists(target))throw new IOException("Profile conflict during install: "+change.Name);File.Move(Path.Combine(txn,"downloads",change.Name),target);afterMutation?.Invoke(++mutations);}
   }
   Rules.Write(State,new Installed(pack.PackId,pack.Version,pack.Mods.Select(m=>new OwnedFile(m.Id,m.Asset,m.Sha256.ToLowerInvariant())).ToList()));
   journal.Status="Committed";Rules.Write(Path.Combine(txn,"journal.json"),journal);File.WriteAllText(Path.Combine(root,"last-transaction.txt"),Path.GetFileName(txn));log("Installed "+pack.Version+". Backups retained.");
  }catch{Undo(txn,journal);throw;}
 }
 public void Recover(){using var guard=Lock();RecoverUnsafe();}
 void RecoverUnsafe(){var folder=Path.Combine(root,"transactions");if(!Directory.Exists(folder))return;foreach(var txn in Directory.EnumerateDirectories(folder)){Rules.NoLink(txn);var file=Path.Combine(txn,"journal.json");if(!File.Exists(file))continue;var journal=Rules.Read<Journal>(file);if(journal.Status=="Prepared"){log("Recovering interrupted update");Undo(txn,journal);}}}
 public void Rollback(){using var guard=Lock();RecoverUnsafe();var pointer=Path.Combine(root,"last-transaction.txt");if(!File.Exists(pointer))throw new IOException("No completed update to roll back");var id=File.ReadAllText(pointer).Trim();if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid transaction pointer");var txn=Path.Combine(root,"transactions",id);var journal=Rules.Read<Journal>(Path.Combine(txn,"journal.json"));if(journal.Status!="Committed")throw new IOException("That update has already been rolled back");Undo(txn,journal);log("Previous mod set restored.");}
 void Undo(string txn,Journal journal){
  // Validate every path and hash before changing anything during recovery.
  foreach(var c in journal.Changes){if(!Rules.Asset(c.Name)||c.Before is not null&&!Rules.Hash(c.Before)||c.After is not null&&!Rules.Hash(c.After))throw new InvalidDataException("Invalid recovery journal");var target=Path.Combine(mods,c.Name);Rules.NoLink(target);var backup=Path.Combine(txn,"backup",c.Name);Rules.NoLink(backup);if(File.Exists(backup)&&Rules.Digest(backup)!=c.Before)throw new IOException("Backup changed; manual recovery required");if(File.Exists(target)){var hash=Rules.Digest(target);if(hash!=c.Before&&hash!=c.After)throw new IOException("File changed after update; refusing to overwrite: "+c.Name);}if(c.Before is not null&&!File.Exists(backup)&&(!File.Exists(target)||Rules.Digest(target)!=c.Before))throw new IOException("Original backup missing; manual recovery required");}
  var kept=Path.Combine(txn,"rolled-back-new");Directory.CreateDirectory(kept);
  foreach(var c in journal.Changes.AsEnumerable().Reverse()){var target=Path.Combine(mods,c.Name);var backup=Path.Combine(txn,"backup",c.Name);
   if(File.Exists(target)&&c.After is not null&&Rules.Digest(target)==c.After)File.Move(target,Path.Combine(kept,c.Name),true);
   if(File.Exists(backup))File.Move(backup,target);
  }
  if(journal.HadState)File.Copy(Path.Combine(txn,"previous-state.json"),State,true);else if(File.Exists(State))File.Move(State,Path.Combine(txn,"rolled-back-state.json"),true);
  journal.Status="RolledBack";Rules.Write(Path.Combine(txn,"journal.json"),journal);
 }
}
