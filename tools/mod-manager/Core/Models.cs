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
