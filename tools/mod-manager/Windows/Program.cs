using Saturn.Manager;
using System.Diagnostics;
using System.Text.Json;

ApplicationConfiguration.Initialize();
Application.Run(new ManagerForm());

sealed class ManagerForm:Form{
 readonly TextBox profile=new(){Dock=DockStyle.Fill},manifest=new(){Dock=DockStyle.Fill};
 readonly Button browse=new(){Text="Choose profile…",AutoSize=true},check=new(){Text="Check releases",AutoSize=true},install=new(){Text="Install / Update",AutoSize=true,Enabled=false},rollback=new(){Text="Roll back last update",AutoSize=true},cancel=new(){Text="Cancel",AutoSize=true,Enabled=false};
 readonly ListView mods=new(){Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
 readonly TextBox output=new(){Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical};
 readonly Label target=new(){AutoSize=true,Text="Choose your Minecraft game folder. Close Minecraft before installing updates."};
 readonly GitHubSource source=new();PackManifest? pack;CancellationTokenSource? cancellation;bool busy;
 const string DefaultManifest="https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/staging/pack.json";
 string SettingsFile=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SaturnModManager","settings.json");
 public ManagerForm(){
  Text="Saturn Mod Manager — Staging";Width=920;Height=680;MinimumSize=new Size(720,520);StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(23,32,44);ForeColor=Color.WhiteSmoke;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=8,Padding=new Padding(14)};
  layout.ColumnStyles.Add(new(SizeType.Absolute,120));layout.ColumnStyles.Add(new(SizeType.Percent,100));layout.ColumnStyles.Add(new(SizeType.Absolute,150));
  foreach(int height in new[]{32,38,38,48})layout.RowStyles.Add(new(SizeType.Absolute,height));layout.RowStyles.Add(new(SizeType.Percent,55));layout.RowStyles.Add(new(SizeType.Absolute,42));layout.RowStyles.Add(new(SizeType.Percent,45));layout.RowStyles.Add(new(SizeType.Absolute,32));
  var heading=new Label{Text="SATURN  /  MOD MANAGER",AutoSize=true,Font=new Font(Font.FontFamily,14,FontStyle.Bold)};layout.Controls.Add(heading,0,0);layout.SetColumnSpan(heading,3);
  layout.Controls.Add(new Label{Text="Game folder",AutoSize=true},0,1);layout.Controls.Add(profile,1,1);layout.Controls.Add(browse,2,1);
  layout.Controls.Add(new Label{Text="Pack manifest",AutoSize=true},0,2);layout.Controls.Add(manifest,1,2);layout.Controls.Add(check,2,2);
  layout.Controls.Add(target,0,3);layout.SetColumnSpan(target,3);
  mods.Columns.Add("Mod ID",170);mods.Columns.Add("Release tag",150);mods.Columns.Add("File",360);mods.Columns.Add("Size",80);layout.Controls.Add(mods,0,4);layout.SetColumnSpan(mods,3);
  var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.AddRange([install,rollback,cancel]);layout.Controls.Add(actions,0,5);layout.SetColumnSpan(actions,3);
  layout.Controls.Add(output,0,6);layout.SetColumnSpan(output,3);
  var loader=new LinkLabel{Text="First setup: install Fabric Loader for the Minecraft version shown above",AutoSize=true,LinkColor=Color.LightSkyBlue};loader.LinkClicked+=(_,_)=>Process.Start(new ProcessStartInfo("https://fabricmc.net/use/installer/"){UseShellExecute=true});layout.Controls.Add(loader,0,7);layout.SetColumnSpan(loader,3);Controls.Add(layout);
  profile.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),".minecraft");manifest.Text=DefaultManifest;
  try{if(File.Exists(SettingsFile)){var s=JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsFile));if(s is not null){profile.Text=s.Profile;manifest.Text=s.Manifest;}}}catch(Exception e){Log("Could not restore settings: "+e.Message);}
  manifest.TextChanged+=(_,_)=>{pack=null;install.Enabled=false;};
  browse.Click+=(_,_)=>{using var dialog=new FolderBrowserDialog{Description="Choose the Minecraft profile/game directory (contains mods)",ShowNewFolderButton=false,SelectedPath=profile.Text};if(dialog.ShowDialog(this)==DialogResult.OK)profile.Text=dialog.SelectedPath;};
  check.Click+=async(_,_)=>await Run(async token=>{var url=manifest.Text;pack=await source.Fetch(url,token);mods.Items.Clear();foreach(var m in pack.Mods)mods.Items.Add(new ListViewItem([m.Id,m.Tag,m.Asset,$"{m.Size/1024.0/1024:0.0} MB"]));target.Text=$"{pack.PackId} {pack.Version}  |  Minecraft {pack.Minecraft}  |  Fabric Loader {pack.FabricLoader}\nJAR updates only; your launcher, accounts and worlds are not changed.";Save();Log("Pack manifest checked. "+pack.Mods.Count+" pinned, checksummed mods.");});
  install.Click+=async(_,_)=>{if(pack is null)return;var selected=pack;string path=profile.Text;await Run(async token=>{Log("Updating only managed mods in: "+path);await Task.Run(()=>new Installer(path,source,Log).Install(selected,token),token);Save();});};
  rollback.Click+=async(_,_)=>{string path=profile.Text;await Run(token=>Task.Run(()=>new Installer(path,source,Log).Rollback(),token));};
  cancel.Click+=(_,_)=>cancellation?.Cancel();
  FormClosing+=(_,e)=>{if(busy){e.Cancel=true;cancellation?.Cancel();Log("Cancelling; wait for recovery to finish before closing.");}else source.Dispose();};
  Log("Staging channel. Choose the Fabric profile you actually launch; unrelated mods are preserved.");
 }
 async Task Run(Func<CancellationToken,Task> action){if(busy)return;busy=true;cancellation=new();browse.Enabled=check.Enabled=install.Enabled=rollback.Enabled=profile.Enabled=manifest.Enabled=false;cancel.Enabled=true;try{await action(cancellation.Token);Log("Ready.");}catch(OperationCanceledException){Log("Cancelled. Any applied changes were recovered before returning.");}catch(Exception e){Log("Stopped: "+e.Message);MessageBox.Show(this,e.Message,"Saturn updater stopped",MessageBoxButtons.OK,MessageBoxIcon.Information);}finally{busy=false;cancellation.Dispose();cancellation=null;browse.Enabled=check.Enabled=rollback.Enabled=profile.Enabled=manifest.Enabled=true;install.Enabled=pack is not null;cancel.Enabled=false;}}
 void Log(string text){if(IsDisposed)return;if(InvokeRequired){BeginInvoke(()=>Log(text));return;}output.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
 void Save(){Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);File.WriteAllText(SettingsFile,JsonSerializer.Serialize(new Saved(profile.Text,manifest.Text)));}
 sealed record Saved(string Profile,string Manifest);
}
