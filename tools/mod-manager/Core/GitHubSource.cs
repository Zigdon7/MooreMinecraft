using System.Net.Http.Headers;
using System.Text.Json;
namespace Saturn.Manager;
public interface IArtifacts{Task Download(ModSpec mod,string destination,CancellationToken token);}
public sealed class GitHubSource:IArtifacts,IDisposable{
 readonly HttpClient client=new(){Timeout=TimeSpan.FromSeconds(90)};
 public GitHubSource(){client.DefaultRequestHeaders.UserAgent.ParseAdd("Saturn-Mod-Manager/0.1.0");}
 public static Uri ManifestUri(string text){var u=new Uri(text);if(u.Scheme!="https"||u.UserInfo.Length!=0||u.Port!=443||!new[]{"raw.githubusercontent.com","github.com"}.Contains(u.Host,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("Manifest must be a public HTTPS GitHub/raw GitHub URL");return u;}
 public async Task<PackManifest> Fetch(string url,CancellationToken token){using var response=await client.GetAsync(ManifestUri(url),HttpCompletionOption.ResponseHeadersRead,token);var bytes=await Limited(response,1048576,token);var pack=JsonSerializer.Deserialize<PackManifest>(bytes,Rules.Json)??throw new InvalidDataException("No manifest");pack.Validate();return pack;}
 public async Task Download(ModSpec mod,string destination,CancellationToken token){
  var api=$"https://api.github.com/repos/{mod.Repo}/releases/tags/{Uri.EscapeDataString(mod.Tag)}";
  using var response=await client.GetAsync(api,HttpCompletionOption.ResponseHeadersRead,token);using var doc=JsonDocument.Parse(await Limited(response,1048576,token));
  var release=doc.RootElement;if(release.GetProperty("draft").GetBoolean())throw new InvalidDataException("Release is a draft");
  var matches=release.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==mod.Asset).ToList();if(matches.Count!=1)throw new InvalidDataException("Release asset missing or ambiguous: "+mod.Asset);
  var asset=matches[0];if(asset.GetProperty("size").GetInt64()!=mod.Size)throw new InvalidDataException("Release size differs from pack manifest");
  var u=new Uri(asset.GetProperty("browser_download_url").GetString()!);if(u.Scheme!="https"||u.Host!="github.com"||!u.AbsolutePath.StartsWith("/"+mod.Repo+"/releases/download/",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Asset is not from its configured GitHub release");
  using var download=await client.GetAsync(u,HttpCompletionOption.ResponseHeadersRead,token);download.EnsureSuccessStatusCode();
  if(download.Content.Headers.ContentLength is long n&&n!=mod.Size)throw new InvalidDataException("Download length mismatch");
  await using var input=await download.Content.ReadAsStreamAsync(token);await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
  byte[] buffer=new byte[65536];long count=0;int read;
  while((read=await input.ReadAsync(buffer,token))!=0){count+=read;if(count>mod.Size)throw new InvalidDataException("Download exceeds expected size");await output.WriteAsync(buffer.AsMemory(0,read),token);}
  if(count!=mod.Size)throw new InvalidDataException("Incomplete download");await output.FlushAsync(token);
 }
 static async Task<byte[]> Limited(HttpResponseMessage response,int max,CancellationToken token){response.EnsureSuccessStatusCode();await using var input=await response.Content.ReadAsStreamAsync(token);using var output=new MemoryStream();byte[] buffer=new byte[8192];int n;while((n=await input.ReadAsync(buffer,token))>0){if(output.Length+n>max)throw new InvalidDataException("Response too large");output.Write(buffer,0,n);}return output.ToArray();}
 public void Dispose()=>client.Dispose();
}
