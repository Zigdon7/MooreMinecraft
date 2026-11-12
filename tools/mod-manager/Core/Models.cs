using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Saturn.Manager;
public sealed record ModSpec(string Id,string Repo,string Tag,string Asset,string Sha256,long Size);
public sealed record PackManifest(int Schema,string PackId,string Version,string Minecraft,string FabricLoader,List<ModSpec> Mods){
 public void Validate(){
  if(Schema!=1||!Rules.Id(PackId)||string.IsNullOrWhiteSpace(Version)||Version.Length>64||string.IsNullOrWhiteSpace(Minecraft)||string.IsNullOrWhiteSpace(FabricLoader)||Mods is null||Mods.Count is <1 or >32)throw new InvalidDataException("Invalid pack manifest");
  var ids=new HashSet<string>(StringComparer.Ordinal);var assets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var m in Mods){if(m is null||!Rules.Id(m.Id)||!ids.Add(m.Id)||!Rules.Repo(m.Repo)||string.IsNullOrWhiteSpace(m.Tag)||m.Tag.Length>128||m.Tag.Any(char.IsControl)||!Rules.Asset(m.Asset)||!assets.Add(m.Asset)||!Rules.Hash(m.Sha256)||m.Size is <1 or >134217728)throw new InvalidDataException("Invalid or duplicate mod entry");}
 }
}
public sealed record OwnedFile(string Id,string Name,string Hash);
public sealed record Installed(string PackId,string Version,List<OwnedFile> Files);
public sealed record Change(string Name,string? Before,string? After);
public sealed class Journal{
 public string Status{get;set;}="Prepared";public bool HadState{get;set;}public List<Change> Changes{get;set;}=[];
}
/// <summary>What a game folder holds compared with a pack. Read-only: never changes the folder.</summary>
public static class Compare{
 public const string Current="current",Update="update",New="new";
 /// <summary>Per mod: Current when the pinned file is present with the pinned hash, Update when another
 /// version of that mod is recorded or the file differs, New when the folder has no version of it.</summary>
 public static List<(ModSpec Mod,string State)> Mods(string profile,PackManifest pack,Installed? record){
  var result=new List<(ModSpec,string)>();string mods=Path.Combine(profile,"mods");
  foreach(var m in pack.Mods){
   string target=Path.Combine(mods,m.Asset);bool recorded=record?.Files?.Any(f=>f.Id==m.Id&&File.Exists(Path.Combine(mods,f.Name)))==true;
   string state=File.Exists(target)?(Matches(target,m.Sha256)?Current:Update):recorded?Update:New;
   result.Add((m,state));
  }
  return result;
 }
 /// <summary>Recorded files that are no longer in the mods folder.</summary>
 public static List<string> Missing(string profile,Installed record)=>(record.Files??[]).Where(f=>!File.Exists(Path.Combine(profile,"mods",f.Name))).Select(f=>f.Name).ToList();
 // Minecraft keeps its JARs open while running, so read without demanding exclusive access.
 static bool Matches(string path,string hash){try{using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return Convert.ToHexStringLower(SHA256.HashData(f)).Equals(hash,StringComparison.OrdinalIgnoreCase);}catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}}
}
public static class Rules{
 public static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,PropertyNameCaseInsensitive=false};
 public static bool Id(string? x)=>x is not null&&Regex.IsMatch(x,"^[a-z][a-z0-9_-]{1,63}$");
 public static bool Repo(string? x)=>x is not null&&Regex.IsMatch(x,"^[A-Za-z0-9_-]+/[A-Za-z0-9_.-]+$")&&!x.Split('/').Any(p=>p is "." or "..");
 public static bool Hash(string? x)=>x is not null&&Regex.IsMatch(x,"^[a-fA-F0-9]{64}$");
 public static bool Asset(string? x)=>x is not null&&Regex.IsMatch(x,"^[A-Za-z0-9][A-Za-z0-9._+\\-]{0,180}\\.jar$")&&!x.Contains("..")&&!Regex.IsMatch(x,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])\\.",RegexOptions.IgnoreCase);
 public static string Digest(string p){using var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.None);return Convert.ToHexStringLower(SHA256.HashData(f));}
 public static T Read<T>(string p)=>JsonSerializer.Deserialize<T>(File.ReadAllText(p),Json)??throw new InvalidDataException("Empty state file");
 public static void Write<T>(string p,T value){var bytes=JsonSerializer.SerializeToUtf8Bytes(value,Json);var temp=p+".new";using(var f=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}File.Move(temp,p,true);}
 public static void NoLink(string p){if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked updater paths are not supported: "+p);}
}
