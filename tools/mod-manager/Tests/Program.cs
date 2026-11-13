using Saturn.Manager;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if(args.Length==4 && args[0]=="--manifest" && args[2]=="--profile"){
 using var remote=new GitHubSource();var manifest=await remote.Fetch(args[1],CancellationToken.None);
 Directory.CreateDirectory(args[3]);var installer=new Installer(args[3],remote,Console.WriteLine);
 await installer.Install(manifest);foreach(var mod in manifest.Mods){var file=Path.Combine(args[3],"mods",mod.Asset);if(Rules.Digest(file)!=mod.Sha256)throw new Exception("Post-install digest mismatch");}
 Console.WriteLine("PASS anonymous public-release download + verified install: "+manifest.Version);
 installer.Rollback();if(Directory.EnumerateFiles(Path.Combine(args[3],"mods"),"*.jar").Any())throw new Exception("Rollback did not restore empty fixture profile");
 Console.WriteLine("PASS public-release install rollback");return;
}

if(args.Length==2 && args[0]=="--launcher-smoke"){
 using var client = new HttpClient(); using var remote = new GitHubSource();
 var pack=await remote.Fetch("https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/production/pack.json",CancellationToken.None);
 var setup=new LauncherSetup(client);var prepared=await setup.Fetch(pack,CancellationToken.None);
 string root=Path.GetFullPath(args[1]),game=Path.Combine(root,"higdon-server");
 Directory.CreateDirectory(game);await new Installer(game,remote,Console.WriteLine).Install(pack);
 LauncherSetup.Apply(root,game,pack,prepared);
 if(!LauncherSetup.Ready(root,game,pack))throw new Exception("Launcher configuration not ready");
 Console.WriteLine("PASS live official Fabric metadata + public mod downloads → isolated launcher profile configuration (not an actual game launch)");return;
}

int passed=0,failed=0;var results=new List<string>();
async Task Test(string name,Func<Task> run){try{await run();passed++;results.Add("PASS "+name);Console.WriteLine("PASS "+name);}catch(Exception e){failed++;results.Add("FAIL "+name+": "+e);Console.WriteLine("FAIL "+name+": "+e.Message);}}
void Assert(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
async Task Throws(Func<Task> run){bool threw=false;try{await run();}catch{threw=true;}Assert(threw,"Expected failure");}
byte[] Jar(string id,string content){using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){using(var w=new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))w.Write(JsonSerializer.Serialize(new{id}));using(var w=new StreamWriter(zip.CreateEntry("test.txt").Open()))w.Write(content);}return output.ToArray();}
ModSpec Spec(string id,string filename,byte[] bytes)=>new(id,"saturn-test/mods","v1",filename,Convert.ToHexStringLower(SHA256.HashData(bytes)),bytes.Length);
PackManifest Pack(string version,params ModSpec[] mods)=>new(1,"saturn",version,"26.3","0.20.1",mods.ToList());
string Root(){string p=Path.Combine(Directory.GetCurrentDirectory(),"TestArtifacts",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(p,"mods"));return p;}
string Mod(string root,string name)=>Path.Combine(root,"mods",name);
Task Done()=>Task.CompletedTask;

await Test("verified first install preserves unrelated files",async()=>{string p=Root();File.WriteAllText(Mod(p,"unrelated.jar"),"untouched");var bytes=Jar("saturn_atlas","one");var spec=Spec("saturn_atlas","atlas-1.jar",bytes);await new Installer(p,new Fake(spec.Asset,bytes)).Install(Pack("1",spec));Assert(File.ReadAllBytes(Mod(p,spec.Asset)).SequenceEqual(bytes));Assert(File.ReadAllText(Mod(p,"unrelated.jar"))=="untouched");Assert(Rules.Read<Installed>(Path.Combine(p,".saturn-manager","installed.json")).Version=="1");});
await Test("manual old versions are backed up by mod ID",async()=>{string p=Root();var old=Jar("saturn_atlas","old");File.WriteAllBytes(Mod(p,"manual-old.jar"),old);var bytes=Jar("saturn_atlas","new");var spec=Spec("saturn_atlas","atlas-2.jar",bytes);var i=new Installer(p,new Fake(spec.Asset,bytes));await i.Install(Pack("2",spec));Assert(!File.Exists(Mod(p,"manual-old.jar")));i.Rollback();Assert(File.ReadAllBytes(Mod(p,"manual-old.jar")).SequenceEqual(old));Assert(!File.Exists(Mod(p,spec.Asset)));});
await Test("checksum mismatch makes no mods changes",async()=>{string p=Root();var bytes=Jar("saturn_atlas","one");var spec=Spec("saturn_atlas","atlas.jar",bytes);var corrupt=(byte[])bytes.Clone();corrupt[^1]^=1;await Throws(()=>new Installer(p,new Fake(spec.Asset,corrupt)).Install(Pack("1",spec)));Assert(!Directory.EnumerateFiles(Path.Combine(p,"mods")).Any());});
await Test("wrong Fabric mod ID is rejected",async()=>{string p=Root();var bytes=Jar("different_mod","one");var spec=Spec("saturn_atlas","atlas.jar",bytes);await Throws(()=>new Installer(p,new Fake(spec.Asset,bytes)).Install(Pack("1",spec)));Assert(!File.Exists(Mod(p,spec.Asset)));});
await Test("path traversal and executable assets rejected",async()=>{var bytes=Jar("saturn_atlas","one");foreach(string name in new[]{"../evil.jar","C:\\evil.jar","bad/evil.jar","manager.exe","CON.jar","x..jar"})await Throws(()=>{Pack("1",Spec("saturn_atlas",name,bytes)).Validate();return Done();});});
await Test("duplicate mod IDs and bad hashes rejected",async()=>{var bytes=Jar("saturn_atlas","one");var a=Spec("saturn_atlas","a.jar",bytes);await Throws(()=>{Pack("1",a,a with{Asset="b.jar"}).Validate();return Done();});await Throws(()=>{Pack("1",a with{Sha256="bad"}).Validate();return Done();});});
await Test("unmanaged filename collision is preserved",async()=>{string p=Root();var old=Jar("unrelated_mod","old");File.WriteAllBytes(Mod(p,"atlas.jar"),old);var bytes=Jar("saturn_atlas","new");var spec=Spec("saturn_atlas","atlas.jar",bytes);await Throws(()=>new Installer(p,new Fake(spec.Asset,bytes)).Install(Pack("1",spec)));Assert(File.ReadAllBytes(Mod(p,"atlas.jar")).SequenceEqual(old));});
await Test("failed upgrade restores files and installed state",async()=>{string p=Root();var a=Jar("saturn_atlas","one");var sa=Spec("saturn_atlas","atlas-1.jar",a);await new Installer(p,new Fake(sa.Asset,a)).Install(Pack("1",sa));var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","atlas-2.jar",b);await Throws(()=>new Installer(p,new Fake(sb.Asset,b)).Install(Pack("2",sb),afterMutation:_=>throw new IOException("Simulated failure")));Assert(File.ReadAllBytes(Mod(p,sa.Asset)).SequenceEqual(a));Assert(!File.Exists(Mod(p,sb.Asset)));Assert(Rules.Read<Installed>(Path.Combine(p,".saturn-manager","installed.json")).Version=="1");});
await Test("changed managed JAR refuses update",async()=>{string p=Root();var a=Jar("saturn_atlas","one");var sa=Spec("saturn_atlas","atlas.jar",a);await new Installer(p,new Fake(sa.Asset,a)).Install(Pack("1",sa));File.AppendAllText(Mod(p,sa.Asset),"local edit");var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","atlas.jar",b);await Throws(()=>new Installer(p,new Fake(sb.Asset,b)).Install(Pack("2",sb)));Assert(File.ReadAllText(Mod(p,sa.Asset)).EndsWith("local edit"));});
await Test("interrupted transaction recovery restores prior pack",async()=>{string p=Root();var a=Jar("saturn_atlas","one");var sa=Spec("saturn_atlas","atlas-1.jar",a);var i=new Installer(p,new Fake(sa.Asset,a));await i.Install(Pack("1",sa));var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","atlas-2.jar",b);string root=Path.Combine(p,".saturn-manager"),txn=Path.Combine(root,"transactions",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(txn,"backup"));File.Copy(Path.Combine(root,"installed.json"),Path.Combine(txn,"previous-state.json"));Rules.Write(Path.Combine(txn,"journal.json"),new Journal{HadState=true,Changes=[new(sa.Asset,sa.Sha256,null),new(sb.Asset,null,sb.Sha256)]});File.Move(Mod(p,sa.Asset),Path.Combine(txn,"backup",sa.Asset));File.WriteAllBytes(Mod(p,sb.Asset),b);i.Recover();Assert(File.ReadAllBytes(Mod(p,sa.Asset)).SequenceEqual(a));Assert(!File.Exists(Mod(p,sb.Asset)));});
await Test("rollback refuses locally modified new files",async()=>{string p=Root();var a=Jar("saturn_atlas","one");File.WriteAllBytes(Mod(p,"old.jar"),a);var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","new.jar",b);var i=new Installer(p,new Fake(sb.Asset,b));await i.Install(Pack("2",sb));File.AppendAllText(Mod(p,sb.Asset),"edit");await Throws(()=>{i.Rollback();return Done();});Assert(!File.Exists(Mod(p,"old.jar")));Assert(File.Exists(Mod(p,"new.jar")));});
await Test("non HTTPS or non GitHub manifest sources rejected",async()=>{foreach(string url in new[]{"http://github.com/a/b","https://example.com/pack.json","file:///etc/passwd","https://name:password@github.com/a/b"})await Throws(()=>{GitHubSource.ManifestUri(url);return Done();});Assert(GitHubSource.ManifestUri("https://raw.githubusercontent.com/test/repo/main/pack.json").Scheme=="https");});
await Test("linked mods directory is refused",async()=>{string p=Root();Directory.Delete(Path.Combine(p,"mods"));string other=Root();Directory.CreateSymbolicLink(Path.Combine(p,"mods"),other);await Throws(()=>{new Installer(p,new Fake("x",[]));return Done();});});

await Test("comparison reports not installed, current and update without changing files",async()=>{string p=Root();var a=Jar("saturn_atlas","one");var sa=Spec("saturn_atlas","atlas-1.jar",a);var api=Jar("fabric-api","api");var sapi=Spec("fabric-api","api.jar",api);var one=Pack("1",sa,sapi);Assert(Compare.Mods(p,one,null).All(x=>x.State==Compare.New),"empty folder is not installed");await new Installer(p,new Multi(new(){[sa.Asset]=a,[sapi.Asset]=api})).Install(one);string state=Path.Combine(p,".saturn-manager","installed.json");var record=Rules.Read<Installed>(state);Assert(Compare.Mods(p,one,record).All(x=>x.State==Compare.Current),"installed pack is current");Assert(Compare.Missing(p,record).Count==0);var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","atlas-2.jar",b);var two=Compare.Mods(p,Pack("2",sb,sapi),record);Assert(two.Single(x=>x.Mod.Id=="saturn_atlas").State==Compare.Update,"renamed newer file is an update");Assert(two.Single(x=>x.Mod.Id=="fabric-api").State==Compare.Current,"unchanged file stays current");var same=Spec("saturn_atlas","atlas-1.jar",b);Assert(Compare.Mods(p,Pack("3",same,sapi),record)[0].State==Compare.Update,"same name with another hash is an update");using(new FileStream(Mod(p,sa.Asset),FileMode.Open,FileAccess.Read,FileShare.Read))Assert(Compare.Mods(p,one,record)[0].State==Compare.Current,"a JAR held open by the game is still compared");File.Delete(Mod(p,sa.Asset));Assert(Compare.Missing(p,record).SequenceEqual(new[]{sa.Asset}),"deleted file is reported missing");Assert(Compare.Mods(p,one,record)[0].State==Compare.New,"deleted file is not installed");Assert(Rules.Read<Installed>(state).Version=="1"&&File.Exists(Mod(p,sapi.Asset)),"comparison changed the folder");});


string Feed(string entry)=>"{\"schema\":1,\"components\":[{\"type\":\"fabric-client\",\"version\":\"1\"},"+entry+"]}";
string Entry(string version="0.2.1",string asset="Saturn-Mod-Manager.exe",string download="https://github.com/Zigdon7/MooreMinecraft/releases/download/manager-v0.2.1/Saturn-Mod-Manager.exe",long size=2000000,string? hash=null)=>JsonSerializer.Serialize(new{type="windows-updater",version,asset,size,sha256=hash??new string('a',64),download});
ManagerRelease? Read(string feed)=>SelfUpdate.Parse(Encoding.UTF8.GetBytes(feed));
await Test("manager feed entry is read and only a higher version counts as newer",async()=>{var r=Read(Feed(Entry()))!;Assert(r.Version=="0.2.1"&&r.Size==2000000);Assert(SelfUpdate.Newer("0.2.1",new Version(0,2,0,0)));Assert(!SelfUpdate.Newer("0.2.0",new Version(0,2,0,0)),"same version is not an update");Assert(!SelfUpdate.Newer("0.1.9",new Version(0,2,0,0)),"never downgrade");Assert(SelfUpdate.Newer("0.10.0",new Version(0,9,3)),"numeric, not text, comparison");Assert(!SelfUpdate.Newer("latest",new Version(0,1,0)));Assert(Read("{\"components\":[]}") is null,"a feed without a manager entry offers nothing");await Done();});
await Test("manager release from another host, repository, file or channel is rejected",async()=>{foreach(string bad in new[]{Entry(download:"https://example.com/Zigdon7/MooreMinecraft/releases/download/t/Saturn-Mod-Manager.exe"),Entry(download:"http://github.com/Zigdon7/MooreMinecraft/releases/download/t/Saturn-Mod-Manager.exe"),Entry(download:"https://github.com/Other/MooreMinecraft/releases/download/t/Saturn-Mod-Manager.exe"),Entry(download:"https://github.com/Zigdon7/MooreMinecraft/releases/download/t/evil.exe"),Entry(download:"https://github.com/Zigdon7/MooreMinecraft/releases/download/t/../../x/Saturn-Mod-Manager.exe"),Entry(download:"https://github.com/Zigdon7/MooreMinecraft/releases/download/t/Saturn-Mod-Manager.exe?x=1"),Entry(download:"https://user@github.com/Zigdon7/MooreMinecraft/releases/download/t/Saturn-Mod-Manager.exe"),Entry(asset:"setup.exe"),Entry(hash:"bad"),Entry(size:10),Entry(version:"1.0"),Entry(version:"0.2.1-beta")})await Throws(()=>{Read(Feed(bad));return Done();});await Throws(()=>{Read(Feed(Entry()+","+Entry()));return Done();});});
await Test("manager download must match the pinned size and hash before replacing anything",async()=>{string dir=Root();byte[] good=new byte[1500000];good[0]=(byte)'M';good[1]=(byte)'Z';good[77]=9;string hash=Convert.ToHexStringLower(SHA256.HashData(good));var r=Read(Feed(Entry(size:good.Length,hash:hash)))!;string file=Path.Combine(dir,"new.exe");File.WriteAllBytes(file,good);SelfUpdate.Verify(file,r);var flipped=(byte[])good.Clone();flipped[^1]^=1;File.WriteAllBytes(file,flipped);await Throws(()=>{SelfUpdate.Verify(file,r);return Done();});File.WriteAllBytes(file,good.Take(good.Length-1).ToArray());await Throws(()=>{SelfUpdate.Verify(file,r);return Done();});byte[] text=new byte[1500000];File.WriteAllBytes(file,text);await Throws(()=>{SelfUpdate.Verify(file,r with{Sha256=Convert.ToHexStringLower(SHA256.HashData(text))});return Done();});});
await Test("manager swap replaces the program, keeps the old one, and undoes a failed swap",async()=>{string dir=Root();string exe=Path.Combine(dir,"Saturn-Mod-Manager.exe");File.WriteAllText(exe,"old");File.WriteAllText(exe+".new","new");File.WriteAllText(exe+".old","stale");SelfUpdate.Swap(exe,exe+".new");Assert(File.ReadAllText(exe)=="new"&&File.ReadAllText(exe+".old")=="old"&&!File.Exists(exe+".new"));SelfUpdate.Cleanup(exe);Assert(File.Exists(exe)&&!File.Exists(exe+".old"));await Throws(()=>{SelfUpdate.Swap(exe,exe+".new");return Done();});Assert(File.ReadAllText(exe)=="new","a failed swap leaves the working program in place");});


System.Text.Json.Nodes.JsonObject Fabric(PackManifest p)=>System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new {id=$"higdon-fabric-loader-{p.FabricLoader}-{p.Minecraft}",inheritsFrom=p.Minecraft,mainClass="net.fabricmc.loader.impl.launch.knot.KnotClient",libraries=new[]{new{name="net.fabricmc:fabric-loader:"+p.FabricLoader,url="https://maven.fabricmc.net/"}}}))!.AsObject();
await Test("launcher setup preserves unrelated profiles/accounts, isolates mods and is idempotent",async()=>{
 string root=Root(),game=Path.Combine(root,"higdon-server");var pack=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));
 string file=Path.Combine(root,"launcher_profiles.json");
 File.WriteAllText(file,"{\"profiles\":{\"vanilla\":{\"name\":\"Keep me\",\"lastVersionId\":\"26.3\"}},\"settings\":{\"keep\":true},\"authenticationDatabase\":{\"fixture\":{\"keep\":true}}}");
 var before=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
 Assert(!LauncherSetup.Ready(root,game,pack));LauncherSetup.Apply(root,game,pack,Fabric(pack));LauncherSetup.Apply(root,game,pack,Fabric(pack));
 var after=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
 foreach(string key in new[]{"settings","authenticationDatabase"})Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(before[key],after[key]),key+" changed");
 Assert(System.Text.Json.Nodes.JsonNode.DeepEquals(before["profiles"]!["vanilla"],after["profiles"]!["vanilla"]));
 Assert(after["profiles"]!.AsObject().Count==2&&LauncherSetup.Ready(root,game,pack));
 Assert(after["profiles"]![LauncherSetup.ProfileKey]!["gameDir"]!.GetValue<string>()==game);
 Assert(Directory.GetFiles(root,"*.higdon-backup-*").Length==2);await Done();
});
await Test("launcher handles Microsoft Store profile file and Loader upgrades independently of mods",async()=>{
 string root=Root(),game=Path.Combine(root,"isolated");var p=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));
 string store=Path.Combine(root,"launcher_profiles_microsoft_store.json");File.WriteAllText(store,"{\"profiles\":{}}");
 LauncherSetup.Apply(root,game,p,Fabric(p));Assert(LauncherSetup.Ready(root,game,p));
 var next=p with{FabricLoader="0.20.2"};Assert(!LauncherSetup.Ready(root,game,next));LauncherSetup.Apply(root,game,next,Fabric(next));Assert(LauncherSetup.Ready(root,game,next));
 Assert(!File.Exists(Path.Combine(root,"launcher_profiles.json")),"created wrong launcher config");await Done();
});
await Test("malformed or locked launcher profiles are not overwritten",async()=>{
 var p=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));string root=Root(),game=Path.Combine(root,"game");string f=Path.Combine(root,"launcher_profiles.json");File.WriteAllText(f,"broken");
 await Throws(()=>{LauncherSetup.Apply(root,game,p,Fabric(p));return Done();});Assert(File.ReadAllText(f)=="broken");
 File.WriteAllText(f,"{\"profiles\":{}}");using(var held=new FileStream(f,FileMode.Open,FileAccess.ReadWrite,FileShare.None))await Throws(()=>{LauncherSetup.Apply(root,game,p,Fabric(p));return Done();});Assert(File.ReadAllText(f)=="{\"profiles\":{}}");
});
await Test("launcher setup refuses symlinked game folders and invalid version paths",async()=>{
 var p=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));string root=Root();string linked=Path.Combine(root,"linked");
 if(!OperatingSystem.IsWindows()){Directory.CreateSymbolicLink(linked,Root());await Throws(()=>{LauncherSetup.Apply(root,linked,p,Fabric(p));return Done();});}
 await Throws(()=>{LauncherSetup.Apply(root,root,p with{Minecraft="../elsewhere"},Fabric(p));return Done();});
});
await Test("Fabric metadata must match exact requested Minecraft and Loader versions",async()=>{
 var p=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));
 var official=Fabric(p);official["id"]=$"fabric-loader-{p.FabricLoader}-{p.Minecraft}";
 using var client=new HttpClient(new MetadataHandler(official.ToJsonString()));var prepared=await new LauncherSetup(client).Fetch(p,CancellationToken.None);
 Assert(prepared["id"]!.GetValue<string>().StartsWith("higdon-fabric-loader-"));
 using var bad=new HttpClient(new MetadataHandler(official.ToJsonString().Replace("26.3","26.2")));await Throws(async()=>{await new LauncherSetup(bad).Fetch(p,CancellationToken.None);});
});
await Test("per-plugin versions persist, update independently and retain legacy compatibility",async()=>{
 string root=Root();var a=Jar("saturn_atlas","one");var api=Jar("fabric-api","api");var sa=Spec("saturn_atlas","atlas-1.jar",a) with{Version="0.2.0"};var sf=Spec("fabric-api","api.jar",api) with{Version="0.165.2+26.3"};
 var pack=Pack("0.2.0",sa,sf) with{ServerPluginVersion="0.2.0"};var installer=new Installer(root,new Multi(new(){[sa.Asset]=a,[sf.Asset]=api}));await installer.Install(pack);
 var record=Rules.Read<Installed>(Path.Combine(root,".saturn-manager","installed.json"));Assert(record.Files.Single(f=>f.Id==sa.Id).Version=="0.2.0");
 var b=Jar("saturn_atlas","two");var sb=Spec("saturn_atlas","atlas-2.jar",b) with{Version="0.2.1"};var next=Pack("0.2.1",sb,sf);
 var states=Compare.Mods(root,next,record);Assert(states[0].State==Compare.Update&&states[1].State==Compare.Current);
 await new Installer(root,new Multi(new(){[sb.Asset]=b,[sf.Asset]=api})).Install(next);Assert(Rules.Read<Installed>(Path.Combine(root,".saturn-manager","installed.json")).Files[0].Version=="0.2.1");
 var legacy=JsonSerializer.Deserialize<OwnedFile>("{\"id\":\"saturn_atlas\",\"name\":\"atlas.jar\",\"hash\":\"abc\"}",Rules.Json)!;Assert(legacy.Version is null);
 await Throws(()=>{(pack with{Mods=[sa with{Version="staging"}]}).Validate();return Done();});
 Assert(SelfUpdate.Feed.Contains("/production/"));
});

await Test("production and staging launcher profiles coexist with isolated game folders",async()=>{
 string root=Root(),prod=Path.Combine(root,"prod"),stage=Path.Combine(root,"stage");var p=Pack("0.2.0",Spec("saturn_atlas","atlas.jar",Jar("saturn_atlas","one")));
 LauncherSetup.Apply(root,prod,p,Fabric(p));LauncherSetup.Apply(root,stage,p,Fabric(p),true);
 Assert(LauncherSetup.Ready(root,prod,p)&&LauncherSetup.Ready(root,stage,p,true));Assert(!LauncherSetup.Ready(root,prod,p,true));
 var profiles=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(root,"launcher_profiles.json")))!["profiles"]!.AsObject();Assert(profiles.Count==2);await Done();
});
await Test("mod installs refuse crossing production/staging channels, including legacy records",async()=>{
 string root=Root();var bytes=Jar("saturn_atlas","one");var m=Spec("saturn_atlas","atlas.jar",bytes);var i=new Installer(root,new Fake(m.Asset,bytes));
 await i.Install(Pack("0.2.0",m) with{Channel="production"});
 await Throws(()=>i.Install(Pack("0.1.3-dev",m) with{Channel="staging"}));
 Assert(Rules.Read<Installed>(Path.Combine(root,".saturn-manager","installed.json")).Channel=="production");
 string legacy=Root();var j=new Installer(legacy,new Fake(m.Asset,bytes));await j.Install(Pack("staging-atlas-123",m));
 await Throws(()=>j.Install(Pack("0.2.0",m) with{Channel="production"}));
});

Console.WriteLine($"RESULT: {passed} passed; {failed} failed");File.WriteAllLines("test-results.txt",results.Append($"RESULT: {passed} passed; {failed} failed"));Environment.ExitCode=failed==0?0:1;
sealed class Fake(string name,byte[] bytes):IArtifacts{public Task Download(ModSpec mod,string destination,CancellationToken token){if(mod.Asset!=name)throw new Exception("Unexpected asset");return File.WriteAllBytesAsync(destination,bytes,token);}}
sealed class Multi(Dictionary<string,byte[]> files):IArtifacts{public Task Download(ModSpec mod,string destination,CancellationToken token)=>File.WriteAllBytesAsync(destination,files[mod.Asset],token);}

sealed class MetadataHandler(string json):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) { if(request.RequestUri!.Host!="meta.fabricmc.net")throw new Exception("Wrong metadata source");return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(json)}); } }
