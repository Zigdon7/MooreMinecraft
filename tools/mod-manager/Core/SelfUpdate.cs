using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Saturn.Manager;
/// <summary>The manager's own pinned release, as published in the production components feed.</summary>
public sealed record ManagerRelease(string Version,string Asset,long Size,string Sha256,string Download);
/// <summary>
/// Replaces the manager's own executable. This is the only executable the manager ever installs.
/// The feed address and repository are fixed here: a pack manifest or a setting cannot redirect them.
/// </summary>
public static class SelfUpdate{
 public const string Feed="https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/production/components.json";
 public const string Asset="Saturn-Mod-Manager.exe";
 const string Releases="/Zigdon7/MooreMinecraft/releases/download/";
 /// <summary>The manager entry of the feed, or null when the feed lists none.</summary>
 public static ManagerRelease? Parse(byte[] json){
  using var doc=JsonDocument.Parse(json);
  if(!doc.RootElement.TryGetProperty("components",out var list)||list.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Invalid components feed");
  var found=list.EnumerateArray().Where(c=>c.ValueKind==JsonValueKind.Object&&c.TryGetProperty("type",out var t)&&t.ValueKind==JsonValueKind.String&&t.GetString()=="windows-updater").ToList();
  if(found.Count==0)return null;
  if(found.Count>1)throw new InvalidDataException("Feed lists more than one manager release");
  string Text(string name)=>found[0].TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()!:throw new InvalidDataException("Manager release is missing "+name);
  if(!found[0].TryGetProperty("size",out var size)||!size.TryGetInt64(out long bytes))throw new InvalidDataException("Manager release is missing size");
  var release=new ManagerRelease(Text("version"),Text("asset"),bytes,Text("sha256"),Text("download"));Validate(release);return release;
 }
 public static void Validate(ManagerRelease r){
  if(Number(r.Version) is null||r.Asset!=Asset||!Rules.Hash(r.Sha256)||r.Size is <1048576 or >268435456)throw new InvalidDataException("Invalid manager release");
  if(!Uri.TryCreate(r.Download,UriKind.Absolute,out var u)||u.Scheme!="https"||u.Host!="github.com"||u.Port!=443||u.UserInfo.Length!=0||u.Query.Length!=0||u.Fragment.Length!=0)throw new InvalidDataException("Manager release is not a GitHub release download");
  // Exactly /Zigdon7/MooreMinecraft/releases/download/<tag>/Saturn-Mod-Manager.exe
  string path=u.AbsolutePath;
  if(!path.StartsWith(Releases,StringComparison.Ordinal)||!Regex.IsMatch(path[Releases.Length..],"^[A-Za-z0-9][A-Za-z0-9._-]{0,100}/"+Regex.Escape(Asset)+"$")||path.Contains(".."))throw new InvalidDataException("Manager release is not from the Saturn repository");
 }
 static Version? Number(string? text)=>text is not null&&Regex.IsMatch(text,@"^\d{1,4}\.\d{1,4}\.\d{1,4}$")&&Version.TryParse(text,out var v)?v:null;
 /// <summary>True only for a strictly higher version, so the feed can never downgrade the manager.</summary>
 public static bool Newer(string candidate,Version current){var c=Number(candidate);return c is not null&&c>new Version(current.Major,current.Minor,Math.Max(0,current.Build));}
 /// <summary>Checks a downloaded file against the pinned size and hash before it may replace anything.</summary>
 public static void Verify(string file,ManagerRelease r){
  Validate(r);
  if(new FileInfo(file).Length!=r.Size)throw new InvalidDataException("Manager download has the wrong size");
  using var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(f.ReadByte()!='M'||f.ReadByte()!='Z')throw new InvalidDataException("Manager download is not a Windows program");
  f.Position=0;
  if(!Convert.ToHexStringLower(SHA256.HashData(f)).Equals(r.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Manager download failed its checksum");
 }
 /// <summary>Puts the verified file in place of the running program. Windows allows a running
 /// program to be renamed, so the old one is kept as <c>.old</c> until the new one has started.</summary>
 public static void Swap(string current,string fresh){
  string old=current+".old";
  if(File.Exists(old))File.Delete(old);
  File.Move(current,old);
  try{File.Move(fresh,current);}catch{File.Move(old,current);throw;}
 }
 /// <summary>Removes leftovers of a finished or abandoned update. Never throws.</summary>
 public static void Cleanup(string current){foreach(string suffix in new[]{".old",".new"})try{File.Delete(current+suffix);}catch(IOException){}catch(UnauthorizedAccessException){}}
}
