using Saturn.Manager;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms.Automation;
using static Theme;

static class Program
{
    // The shell folder picker and other COM dialogs require a single-threaded apartment.
    // Top-level statements start the UI thread in the MTA, where the picker hangs the app.
    [STAThread]
    static int Main(string[] args)
    {
        // Exit code only, no window: lets the smoke test prove the UI thread apartment.
        if (args.Contains("--check-apartment")) return Thread.CurrentThread.GetApartmentState() == ApartmentState.STA ? 0 : 1;
        ApplicationConfiguration.Initialize();
        // Native parts (title bar, text boxes, scroll bars, dialogs) follow the Windows app preference.
        Application.SetColorMode(SystemColorMode.System);
        Theme.Load();
        Application.Run(new ManagerForm(args));
        return 0;
    }
}

/// <summary>
/// Launcher-style layout: navigation on the left, the pack with its installed and latest
/// versions and one main button at the top, the pack's mods listed below with a status each.
/// Every interactive element is a real Windows control, so keyboard and screen readers work.
/// </summary>
sealed class ManagerForm : Form
{
    // Mods page
    readonly PillButton install = new("&Update", "Install checked update", Kind.Primary) { Hero = true };
    readonly PillButton check = new("&Check for updates", "Check for updates", Kind.Secondary);
    readonly PillButton changeFolder = new("Change…", "Change game folder", Kind.Link);
    readonly Badge state = new(9);
    readonly Line meta = new("Live testing channel"), installedValue = new("", 13, true), latestValue = new("", 13, true), folderLine = new("", 9), hint = new("", 10, lines: 2), listHeading = new("", 8.5f, true);
    readonly TableLayoutPanel list = Stack();
    // Settings page
    readonly TextBox profile = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Minecraft game folder", AccessibleDescription = "The game directory used by your Fabric launcher profile." };
    readonly TextBox manifest = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Advanced pack manifest URL" };
    readonly PillButton browse = new("&Browse…", "Browse for Minecraft game folder", Kind.Secondary);
    readonly PillButton resetSource = new("Use &default source", "Use default pack manifest URL", Kind.Secondary);
    readonly PillButton rollback = new("&Restore previous mods…", "Restore previous managed mod set", Kind.Secondary);
    readonly PillButton fabric = new("New to Fabric? Open the Fabric Loader installer website", "Open official Fabric Loader installer website", Kind.Link);
    readonly Line recovery = new(""), managerLine = new("");
    // Activity page
    readonly TextBox output = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Activity log" };
    // Shell
    readonly PillButton navMods = new("&Mods", "Mods page", Kind.Nav), navSettings = new("&Settings", "Settings page", Kind.Nav), navActivity = new("&Activity", "Activity page", Kind.Nav);
    readonly PillButton selfUpdate = new("Install &now", "Install manager update", Kind.Primary);
    readonly PillButton cancel = new("Ca&ncel", "Cancel current operation safely", Kind.Secondary);
    readonly Card banner = new(2) { Highlight = true };
    readonly Line bannerText = new("", 10, true, lines: 2), status = new("Ready."), version = new("", 9);
    readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Visible = false, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, AccessibleName = "Operation in progress" };
    readonly Panel side = new() { Dock = DockStyle.Left }, main = new() { Dock = DockStyle.Fill }, pages = new() { Dock = DockStyle.Fill };
    readonly Control modsPage, settingsPage, activityPage;
    readonly ToolTip hints = new() { AutoPopDelay = 20000 };
    readonly List<Action> styles = [];
    readonly GitHubSource source = new();
    readonly bool preview;
    static readonly Version Current = typeof(Program).Assembly.GetName().Version ?? new Version(0, 0, 0);
    static string CurrentText => Current.ToString(3);
    // State. Render() turns it into what is shown.
    PackManifest? pack;
    Installed? record;
    List<(ModSpec Mod, string State)> states = [];
    ManagerRelease? managerRelease;
    CancellationTokenSource? cancellation;
    bool busy, failedInstallation, checkFailed, recordError;
    string rowsShown = "";
    const string DefaultManifest = "https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/production/pack.json";
    string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaturnModManager", "settings.json");

    public ManagerForm(string[] args)
    {
        preview = args.Contains("--ui-preview");
        Text = "Saturn Mod Manager"; Font = Theme.Font(10);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(Px(900), Px(600)); MinimumSize = new Size(Px(720), Px(500)); StartPosition = FormStartPosition.CenterScreen;

        modsPage = BuildModsPage(); settingsPage = BuildSettingsPage(); activityPage = BuildActivityPage();
        pages.Controls.AddRange([modsPage, settingsPage, activityPage]);
        // Fill first: later controls claim their edges before it.
        main.Controls.Add(pages); main.Controls.Add(BuildBanner()); main.Controls.Add(BuildStatusBar());
        Controls.Add(main); Controls.Add(BuildSidebar());

        profile.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        manifest.Text = DefaultManifest;
        if (!preview) RestoreSettings();
        profile.TextChanged += (_, _) => { if (preview) return; RefreshInstalled(); Render(); };
        manifest.TextChanged += (_, _) => { if (preview) return; pack = null; checkFailed = false; states = []; SetStatus("Release source changed. Check for updates again.", announce: false); Render(); };
        browse.Click += (_, _) => Browse(); changeFolder.Click += (_, _) => Browse();
        resetSource.Click += (_, _) => manifest.Text = DefaultManifest;
        fabric.Click += (_, _) => { if (!preview) Process.Start(new ProcessStartInfo("https://fabricmc.net/use/installer/") { UseShellExecute = true }); };
        check.Click += async (_, _) => await CheckNow(quiet: false);
        install.Click += async (_, _) => await InstallPack();
        rollback.Click += async (_, _) => await Restore();
        selfUpdate.Click += async (_, _) => await UpdateManager();
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; SetStatus("Cancelling. Waiting for file recovery to finish."); };
        foreach (var (nav, page) in new[] { (navMods, modsPage), (navSettings, settingsPage), (navActivity, activityPage) })
            // A page switch changes which windows exist, so it runs after the click (and any UI Automation Invoke) has returned.
            nav.Click += (_, _) => { if (IsHandleCreated) BeginInvoke(() => Go(page)); else Go(page); };
        hints.SetToolTip(rollback, "Puts back the managed mods from before the last install. Worlds and your other mods are not changed.");
        hints.SetToolTip(changeFolder, "Choose the game directory of the Fabric profile you launch.");
        SystemColorsChanged += (_, _) => ApplyTheme();
        FormClosing += (_, e) => {
            if (busy) { e.Cancel = true; cancellation?.Cancel(); SetStatus("Please wait. Finishing or recovering the current operation before closing."); }
            else { source.Dispose(); hints.Dispose(); }
        };

        Go(modsPage);
        if (preview) ConfigurePreview(args); else RefreshInstalled();
        SetTabOrder(this);
        // Main action first, then the folder link, then navigation.
        install.Parent!.TabIndex = 0; folderLine.Parent!.TabIndex = 1;
        ApplyTheme();
        if (preview && args.Contains("--large-text")) EnlargeText(this);
        if (preview && args.Contains("--narrow")) Size = MinimumSize;
        if (preview) TraceFocus(this);
        Shown += (_, _) => {
            var area = Screen.FromControl(this).WorkingArea; Height = Math.Min(Height, area.Height); Width = Math.Min(Width, area.Width);
            (install.Enabled ? install : check).Focus();
            if (preview) return;
            string updatedFrom = args.SkipWhile(a => a != "--updated-from").Skip(1).FirstOrDefault() ?? "";
            if (updatedFrom.Length is > 0 and < 20) SetStatus($"Mod Manager updated from {updatedFrom} to {CurrentText}.");
            _ = Task.Run(RemoveOldProgram);
            // Look up the latest releases on launch. This only reads; nothing is installed.
            BeginInvoke(async () => await CheckNow(quiet: true));
        };
    }

    // ---- Layout -------------------------------------------------------------------------------

    Control BuildSidebar()
    {
        side.Width = Px(188); side.Padding = new Padding(Px(12), Px(18), Px(12), Px(12));
        var top = Stack();
        var brand = new Label { Text = "Saturn", AutoSize = true, Font = Theme.Font(17, true), Margin = new Padding(Px(8), 0, 0, 0), UseMnemonic = false };
        var sub = Soft(new Line("Mod Manager", 9.5f)); sub.Margin = new Padding(Px(10), 0, 0, Px(18));
        Add(top, brand); Add(top, sub); Add(top, navMods); Add(top, navSettings); Add(top, navActivity);
        version.Dock = DockStyle.Bottom; version.Padding = new Padding(Px(8), 0, 0, 0); Soft(version);
        version.Text = "Manager " + CurrentText;
        side.Controls.Add(top); side.Controls.Add(version);
        styles.Add(() => side.BackColor = Side);
        return side;
    }

    Control BuildBanner()
    {
        var holder = Stack(); holder.Padding = new Padding(Px(22), Px(16), Px(22), 0);
        Columns(banner); banner.Margin = Padding.Empty;
        bannerText.Anchor = AnchorStyles.Left | AnchorStyles.Right; bannerText.Dock = DockStyle.None;
        selfUpdate.Anchor = AnchorStyles.Right; selfUpdate.Margin = new Padding(Px(12), 0, 0, 0);
        banner.Controls.Add(bannerText, 0, 0); banner.Controls.Add(selfUpdate, 1, 0);
        banner.Visible = false; styles.Add(banner.Restyle);
        Add(holder, banner); return holder;
    }

    Control BuildStatusBar()
    {
        var bar = Stack(); bar.Dock = DockStyle.Bottom; bar.Padding = new Padding(Px(22), Px(6), Px(22), Px(10));
        var track = new Panel { Dock = DockStyle.Top, Height = Px(4), Margin = new Padding(0, 0, 0, Px(6)) }; track.Controls.Add(progress);
        status.AccessibleName = "Update status"; status.AccessibleRole = AccessibleRole.StaticText; Soft(status);
        status.Anchor = AnchorStyles.Left | AnchorStyles.Right; status.Dock = DockStyle.None;
        var row = Row(status, cancel);
        Add(bar, track); Add(bar, row); return bar;
    }

    Control BuildModsPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Px(22), Px(14), Px(22), 0) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Card(2) { Padding = new Padding(Px(20), Px(16), Px(20), Px(14)) }; Columns(header); styles.Add(header.Restyle);
        var info = Stack();
        var titleRow = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Top };
        titleRow.Controls.Add(new Label { Text = "Saturn", AutoSize = true, Font = Theme.Font(18, true), Margin = Padding.Empty, UseMnemonic = false, Anchor = AnchorStyles.Left });
        state.AccessibleName = "Pack status"; titleRow.Controls.Add(state);
        Add(info, titleRow); meta.Margin = new Padding(Px(2), 0, 0, 0); Add(info, Soft(meta));

        // The two versions side by side: what this folder has, and what the server is on.
        var versions = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = new Padding(Px(2), Px(14), 0, Px(10)) };
        versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); versions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); versions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        versions.RowStyles.Add(new RowStyle(SizeType.AutoSize)); versions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var arrow = new Label { Text = "→", AutoSize = true, Font = Theme.Font(13), Margin = new Padding(Px(10), 0, Px(14), 0), Anchor = AnchorStyles.None, AccessibleName = "then" }; styles.Add(() => arrow.ForeColor = Muted);
        installedValue.AccessibleDescription = "Version installed in the selected game folder"; latestValue.AccessibleDescription = "Latest released version";
        versions.Controls.Add(Soft(new Line("INSTALLED", 8, true) { AccessibleName = "Installed" }), 0, 0); versions.Controls.Add(Soft(new Line("LATEST", 8, true) { AccessibleName = "Latest" }), 2, 0);
        versions.Controls.Add(installedValue, 0, 1); versions.Controls.Add(arrow, 1, 1); versions.Controls.Add(latestValue, 2, 1);

        folderLine.Anchor = AnchorStyles.Left | AnchorStyles.Right; folderLine.Dock = DockStyle.None; folderLine.AccessibleDescription = "Selected game folder"; Soft(folderLine);
        var folderRow = Row(folderLine, changeFolder); folderRow.Margin = new Padding(Px(2), 0, 0, 0);

        var actions = Stack(); actions.Dock = DockStyle.None; actions.Anchor = AnchorStyles.Top | AnchorStyles.Right; actions.Margin = new Padding(Px(18), Px(2), 0, 0);
        install.Font = Theme.Font(12, true); install.Dock = check.Dock = DockStyle.Top; install.Margin = new Padding(0, 0, 0, Px(8)); check.Margin = Padding.Empty;
        Add(actions, install); Add(actions, check);
        header.Controls.Add(info, 0, 0); header.Controls.Add(actions, 1, 0);
        // The versions, folder and next step use the full width of the card, under the title and buttons.
        hint.Margin = new Padding(Px(2), Px(10), 0, 0);
        int line = 1; foreach (Control wide in new Control[] { versions, folderRow, hint }) { header.Controls.Add(wide, 0, line++); header.SetColumnSpan(wide, 2); }

        listHeading.Margin = new Padding(Px(2), Px(6), 0, Px(6)); Soft(listHeading);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty }; scroll.Controls.Add(list);
        page.Controls.Add(header, 0, 0); page.Controls.Add(listHeading, 0, 1); page.Controls.Add(scroll, 0, 2);
        return page;
    }

    Control BuildSettingsPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(Px(22), Px(14), Px(22), 0) };
        var stack = Stack();
        Card Section(string title, string about, int lines = 2)
        {
            var card = new Card(); card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); styles.Add(card.Restyle);
            Add(card, new Line(title, 11, true)); var text = Soft(new Line(about, 9.5f, lines: lines)); text.Margin = new Padding(0, 0, 0, Px(8)); Add(card, text);
            Add(stack, card); return card;
        }
        var folder = Section("Game folder", "The game directory of the Fabric profile you launch. Mods are installed into its “mods” folder; worlds are never touched.");
        Add(folder, Row(profile, browse)); fabric.Margin = new Padding(0, Px(6), 0, 0); fabric.Anchor = AnchorStyles.Left; Add(folder, fabric);
        var release = Section("Release source", "Where the pack’s pinned mod list comes from. Only change this if the Saturn server owner gives you another address.");
        Add(release, manifest); resetSource.Anchor = AnchorStyles.Left; resetSource.Margin = new Padding(0, Px(8), 0, 0); Add(release, resetSource);
        var restore = Section("Recovery", "Every install keeps a backup of the mods it replaced.", 1);
        Add(restore, recovery); rollback.Anchor = AnchorStyles.Left; rollback.Margin = new Padding(0, Px(6), 0, 0); Add(restore, rollback);
        var about = Section("Mod Manager", "Updates to this program are offered at the top of the window and install with one click.", 1);
        Add(about, managerLine);
        page.Controls.Add(stack); return page;
    }

    Control BuildActivityPage()
    {
        var page = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Px(22), Px(14), Px(22), Px(4)) };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.RowStyles.Add(new RowStyle(SizeType.AutoSize)); page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = new Line("Activity", 11, true); title.Margin = new Padding(Px(2), 0, 0, Px(8));
        page.Controls.Add(title, 0, 0); page.Controls.Add(output, 0, 1); return page;
    }

    static TableLayoutPanel Stack()
    {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return panel;
    }
    static void Columns(TableLayoutPanel panel) { panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); }
    /// <summary>Content on the left, its action right-aligned on the same line.</summary>
    static TableLayoutPanel Row(Control left, Control right)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        Columns(row); row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Anchor = AnchorStyles.Left | AnchorStyles.Right; left.Dock = DockStyle.None;
        right.Anchor = AnchorStyles.Right; right.Margin = new Padding(Px(10), Px(2), 0, Px(2));
        row.Controls.Add(left, 0, 0); row.Controls.Add(right, 1, 0); return row;
    }
    static void Add(TableLayoutPanel parent, Control child) { int row = parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(child, 0, row); }
    /// <summary>Secondary text colour, kept in step with the theme.</summary>
    T Soft<T>(T control) where T : Control { styles.Add(() => control.ForeColor = Muted); return control; }
    static void SetTabOrder(Control parent) { int index = 0; foreach (Control child in parent.Controls) { child.TabIndex = index++; if (child is Label) child.TabStop = false; SetTabOrder(child); } }

    void ApplyTheme()
    {
        Theme.Load(); BackColor = Window; ForeColor = Ink;
        foreach (var style in styles) style();
        foreach (var box in new[] { profile, manifest, output }) { box.BackColor = Contrast ? SystemColors.Window : Mix(Surface, Window, 0.5f); box.ForeColor = Ink; }
        rowsShown = ""; Render(); Invalidate(true);
    }

    void Go(Control page)
    {
        foreach (var (nav, target) in new[] { (navMods, modsPage), (navSettings, settingsPage), (navActivity, activityPage) }) { target.Visible = target == page; nav.Selected = target == page; }
    }

    // ---- State to screen ----------------------------------------------------------------------

    bool FolderOk() => preview || Directory.Exists(profile.Text.Trim());
    bool UpToDate() => pack is not null && states.Count > 0 && states.All(s => s.State == Compare.Current);
    static string SizeText(long bytes) => bytes >= 1048576 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
    static string FriendlyName(string id) => id switch { "saturn_atlas" or "saturn_atlas_companion" => "Saturn Atlas", "fabric-api" or "fabric_api" => "Fabric API", _ => id.Replace('_', ' ').Replace('-', ' ') };

    void Render()
    {
        bool folder = FolderOk(), upToDate = UpToDate(), installedHere = folder && record is not null;
        int missing = 0;
        if (installedHere && !preview) try { missing = Compare.Missing(profile.Text.Trim(), record!).Count; } catch (Exception) { }

        installedValue.Text = !folder ? "Folder not found" : recordError ? "Unknown" : record is null ? "Not installed" : record.Version;
        latestValue.Text = pack?.Version ?? (checkFailed ? "Unavailable" : busy ? "Checking…" : "Not checked");
        meta.Text = pack is null ? "Live testing channel" : $"Live testing channel  ·  Minecraft {pack.Minecraft}  ·  Fabric Loader {pack.FabricLoader}";
        folderLine.Text = profile.Text.Trim();

        if (busy) state.Set("Working…", Muted);
        else if (failedInstallation) state.Set("Install failed", Bad);
        else if (!folder) state.Set("Choose game folder", Warn);
        else if (pack is null) state.Set(checkFailed ? "Can’t reach releases" : "Not checked", checkFailed ? Bad : Muted);
        else if (upToDate) state.Set("Up to date", Good);
        else if (installedHere) state.Set("Update available", Warn);
        else state.Set("Not installed", Warn);

        install.Text = upToDate ? "Re&install" : failedInstallation ? "&Try again" : installedHere ? "&Update" : "&Install";
        install.AccessibleName = upToDate ? "Reinstall checked update" : failedInstallation ? "Retry checked installation" : "Install checked update";
        install.AccessibleDescription = upToDate ? "Install the checked version again to repair its managed files." : "Install the latest release into the selected game folder.";
        install.Enabled = !busy && pack is not null && folder; install.Quiet = upToDate;
        check.Text = pack is null ? "&Check for updates" : "&Check again";
        check.Enabled = browse.Enabled = changeFolder.Enabled = profile.Enabled = manifest.Enabled = resetSource.Enabled = !busy;
        rollback.Enabled = !busy && CanRestore(); recovery.Text = RecoverySummary();
        cancel.Visible = cancel.Enabled = busy;
        if (!preview || !busy) progress.Visible = busy;
        string action = install.Text.Replace("&", "");
        hint.Text = busy ? "Please wait. You can cancel safely; anything half-done is put back first."
            : !folder ? "Choose the game folder of your Fabric profile to continue."
            : pack is null ? (checkFailed ? "The release list could not be read. Check your connection, then check again." : "Check for updates to see the latest release. Checking never installs anything.")
            : upToDate ? "You’re ready. Launch your Fabric profile and join the live server on port 25565."
            : missing > 0 ? $"{missing} installed {(missing == 1 ? "file is" : "files are")} missing from the mods folder. Close Minecraft, then click {action} to repair."
            : $"Close Minecraft, then click {action}. The mods it replaces are backed up, and your other mods are not touched.";
        AcceptButton = install.Enabled && !upToDate ? install : check;

        banner.Visible = managerRelease is not null;
        if (managerRelease is not null) bannerText.Text = $"Mod Manager {managerRelease.Version} is available. You have {CurrentText}.";
        selfUpdate.Enabled = !busy;
        managerLine.Text = managerRelease is not null ? $"Version {CurrentText}. Version {managerRelease.Version} is available." : $"Version {CurrentText}.";
        RenderRows(folder);
    }

    void RenderRows(bool folder)
    {
        var rows = new List<(string Name, string Detail, string Badge, Color Tone)>();
        if (pack is not null)
        {
            foreach (var m in pack.Mods)
            {
                string s = states.FirstOrDefault(x => x.Mod.Id == m.Id).State;
                rows.Add((FriendlyName(m.Id), $"{m.Asset}  ·  {SizeText(m.Size)}", s == Compare.Current ? "Up to date" : s == Compare.Update ? "Update available" : s == Compare.New ? "Not installed" : "In this release",
                    s == Compare.Current ? Good : s is Compare.Update or Compare.New ? Warn : Muted));
            }
            listHeading.Text = $"IN THIS RELEASE  ·  {pack.Mods.Count} {(pack.Mods.Count == 1 ? "MOD" : "MODS")}";
        }
        else if (folder && record is not null)
        {
            // Offline or not yet checked: still show what this folder has.
            foreach (var f in record.Files ?? [])
            {
                bool present = preview || File.Exists(Path.Combine(profile.Text.Trim(), "mods", f.Name));
                rows.Add((FriendlyName(f.Id), f.Name, present ? "Installed" : "File missing", present ? Muted : Bad));
            }
            listHeading.Text = "INSTALLED IN THIS FOLDER";
        }
        else listHeading.Text = "MODS";
        if (rows.Count == 0) rows.Add(("Nothing to show yet", folder ? "Check for updates to list the mods in the latest release." : "Choose your game folder first.", "", Muted));

        string signature = string.Join("\n", rows.Select(r => $"{r.Name}|{r.Detail}|{r.Badge}|{r.Tone.ToArgb()}")) + Dark + Contrast;
        if (signature == rowsShown) return;
        rowsShown = signature;
        list.SuspendLayout();
        foreach (Control old in list.Controls.Cast<Control>().ToArray()) { list.Controls.Remove(old); old.Dispose(); }
        list.RowCount = 0; list.RowStyles.Clear();
        foreach (var r in rows)
        {
            var card = new Card(2) { Padding = new Padding(Px(16), Px(9), Px(14), Px(9)), Margin = new Padding(0, 0, 0, Px(6)) }; Columns(card); card.Restyle();
            var name = new Line(r.Name, 10.5f, true); var detail = new Line(r.Detail, 9) { ForeColor = Muted };
            if (Font.Size > 10) foreach (var text in new[] { name, detail }) text.Font = new Font(text.Font.FontFamily, text.Font.Size * Font.Size / 10f, text.Font.Style);
            card.Controls.Add(name, 0, 0); card.Controls.Add(detail, 0, 1);
            if (r.Badge.Length > 0) { var badge = new Badge { Anchor = AnchorStyles.Right }; badge.Set(r.Badge, r.Tone); card.Controls.Add(badge, 1, 0); card.SetRowSpan(badge, 2); }
            Add(list, card);
        }
        list.ResumeLayout(true);
    }

    /// <summary>Reads the install record and compares the folder with the checked release. Reads only.</summary>
    void RefreshInstalled()
    {
        record = null; recordError = false; states = [];
        string path = profile.Text.Trim();
        try
        {
            string file = Path.Combine(path, ".saturn-manager", "installed.json");
            if (File.Exists(file)) record = Rules.Read<Installed>(file);
        }
        catch (Exception e) { record = null; recordError = true; Log("Installation record could not be read: " + e.Message); }
        try { if (pack is not null && Directory.Exists(path)) states = Compare.Mods(path, pack, record); }
        catch (Exception e) { states = []; Log("Could not compare the game folder: " + e.Message); }
    }

    string RecoverySummary()
    {
        if (preview) return "The last install can be undone: Saturn 0.1.2 would be restored.";
        try
        {
            string root = Path.Combine(profile.Text.Trim(), ".saturn-manager");
            string pointer = Path.Combine(root, "last-transaction.txt");
            if (!File.Exists(pointer) || !CanRestore()) return "Nothing to restore in this folder yet.";
            string previous = Path.Combine(root, "transactions", File.ReadAllText(pointer).Trim(), "previous-state.json");
            return File.Exists(previous) ? $"The last install can be undone: {Rules.Read<Installed>(previous).Version} would be restored." : "The last install can be undone: the mods from before it would be restored.";
        }
        catch (Exception) { return "Nothing to restore in this folder yet."; }
    }
    bool CanRestore()
    {
        if (preview) return true;
        try
        {
            string root = Path.Combine(profile.Text.Trim(), ".saturn-manager");
            string id = File.ReadAllText(Path.Combine(root, "last-transaction.txt")).Trim();
            return Guid.TryParseExact(id, "N", out _) && Rules.Read<Journal>(Path.Combine(root, "transactions", id, "journal.json")).Status == "Committed";
        }
        catch (Exception) { return false; }
    }

    // ---- Actions ------------------------------------------------------------------------------

    void Browse()
    {
        if (preview || busy) return;
        using var dialog = new FolderBrowserDialog { Description = "Choose your Fabric profile’s game directory", UseDescriptionForTitle = true, ShowNewFolderButton = false, SelectedPath = profile.Text };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        profile.Text = dialog.SelectedPath; Save();
        SetStatus(record is null ? "Game folder selected. Saturn is not installed there yet." : $"Game folder selected. It has {record.Version}.");
    }

    async Task CheckNow(bool quiet) => await Run(check, "Checking for updates…", async token => {
        // The manager's own release comes from a fixed feed, so a broken pack source cannot hide it.
        try { var found = await source.FetchManager(token); managerRelease = found is not null && SelfUpdate.Newer(found.Version, Current) ? found : null; }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Log("Could not check for a Mod Manager update: " + e.Message); }
        // Invalidate the previous check first: a failed check never leaves a stale install action.
        pack = null; states = []; checkFailed = true;
        pack = await source.Fetch(manifest.Text.Trim(), token); checkFailed = false;
        RefreshInstalled(); Save();
        Log($"Checked {pack.Version}: {pack.Mods.Count} pinned, checksummed files.");
        int pending = states.Count(s => s.State != Compare.Current);
        SetStatus(!FolderOk() ? $"Latest release is {pack.Version}. Choose your game folder to compare it."
            : pending == 0 ? $"You have the latest release, {pack.Version}."
            : record is not null ? $"Update available: {record.Version} → {pack.Version}."
            : $"{pack.Version} is ready to install.");
    }, quiet);

    async Task InstallPack()
    {
        if (preview || pack is null || !FolderOk()) return;
        var selected = pack; string path = profile.Text.Trim();
        await Run(install, $"Installing {selected.Version}: downloading and verifying files…", async token => {
            await Task.Run(() => new Installer(path, source, Log).Install(selected, token), token);
            Save(); RefreshInstalled(); SetStatus($"Installed {selected.Version}. Your other mods were not touched.");
        });
    }

    async Task Restore()
    {
        if (preview) return;
        string path = profile.Text.Trim();
        if (MessageBox.Show(this, $"{RecoverySummary()}\n\nGame folder:\n{path}\n\nClose Minecraft first. This changes managed mods only, not your world.", "Restore previous mods?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        await Run(rollback, "Restoring the previous mods…", async token => {
            await Task.Run(() => new Installer(path, source, Log).Rollback(), token);
            RefreshInstalled(); SetStatus("Previous mods restored.");
        });
    }

    /// <summary>One click: download the pinned release, verify it, swap it in and restart.</summary>
    async Task UpdateManager()
    {
        if (preview || managerRelease is null) return;
        var release = managerRelease; string? exe = Environment.ProcessPath; bool swapped = false;
        await Run(selfUpdate, $"Downloading Mod Manager {release.Version}…", async token => {
            if (exe is null || !Path.GetFileName(exe).Equals(SelfUpdate.Asset, StringComparison.OrdinalIgnoreCase)) throw new IOException("This copy cannot update itself. Download Saturn-Mod-Manager.exe again instead.");
            string fresh = exe + ".new";
            var report = new Progress<int>(percent => { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Clamp(percent, 0, 100); });
            await source.DownloadManager(release, fresh, report, token);
            SetStatus("Verifying the download…");
            await Task.Run(() => SelfUpdate.Verify(fresh, release), token);
            token.ThrowIfCancellationRequested();
            SelfUpdate.Swap(exe, fresh); swapped = true;
            Log($"Mod Manager {release.Version} verified (SHA-256 {release.Sha256}) and put in place.");
        });
        progress.Style = ProgressBarStyle.Marquee;
        if (!swapped) { if (exe is not null) try { File.Delete(exe + ".new"); } catch (Exception) { } return; }
        managerRelease = null; Render();
        try { Process.Start(new ProcessStartInfo(exe!) { UseShellExecute = false, ArgumentList = { "--updated-from", CurrentText } }); Close(); }
        catch (Exception e) { SetStatus($"Mod Manager {release.Version} is installed. Close this window and open it again."); Log("Could not restart: " + e.Message); }
    }

    /// <summary>The previous program file is kept until this one has started, then removed.</summary>
    static async Task RemoveOldProgram()
    {
        string? exe = Environment.ProcessPath; if (exe is null) return;
        for (int attempt = 0; attempt < 6 && (File.Exists(exe + ".old") || File.Exists(exe + ".new")); attempt++) { await Task.Delay(1500); SelfUpdate.Cleanup(exe); }
    }

    async Task Run(Control initiator, string activity, Func<CancellationToken, Task> action, bool quiet = false)
    {
        if (busy || preview) return;
        // Disabling controls moves keyboard focus; remember where it was for a background check.
        var focused = ActiveControl;
        failedInstallation = false; busy = true; cancellation = new(); SetStatus(activity); Render();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("Cancelled. Nothing was changed."); }
        catch (Exception e)
        {
            failedInstallation = ReferenceEquals(initiator, install);
            Log("Stopped: " + e.Message);
            SetStatus(ReferenceEquals(initiator, check) ? "Could not check for updates. Check your connection, then check again." : "That did not complete. Nothing was left half-done; see Activity for the reason.");
            if (!quiet) MessageBox.Show(this, e.Message, "Saturn Mod Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false; cancellation.Dispose(); cancellation = null; Render();
            var target = quiet ? focused : initiator.Enabled ? initiator : check;
            if (target is { Visible: true, Enabled: true }) target.Focus();
        }
    }

    void SetStatus(string text, bool announce = true)
    {
        status.Text = text; status.AccessibleDescription = text;
        if (!announce) return;
        if (IsHandleCreated) status.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text);
        Log(text);
    }
    void Log(string text) { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(() => Log(text)); return; } output.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
    void RestoreSettings()
    {
        try { if (File.Exists(SettingsFile)) { var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsFile)); if (s is not null) { profile.Text = s.Profile; manifest.Text = s.Manifest; } } }
        catch (Exception e) { Log("Could not restore settings: " + e.Message); }
    }
    void Save()
    {
        if (preview) return;
        try { Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Saved(profile.Text.Trim(), manifest.Text.Trim()))); }
        catch (Exception e) { Log("Could not save settings: " + e.Message); }
    }

    // ---- Offline fixtures ---------------------------------------------------------------------

    /// <summary>Offline visual/accessibility fixture only: never fetches or touches a game directory.</summary>
    void ConfigurePreview(string[] args)
    {
        string name = args.SkipWhile(a => a != "--ui-preview").Skip(1).FirstOrDefault() ?? "initial";
        profile.Text = args.Contains("--long-values") ? @"C:\Users\Player\Documents\Minecraft profiles\Saturn staging accessibility review\Instances\Long named Fabric profile for shared Atlas testing" : @"C:\Users\Player\AppData\Roaming\.minecraft";
        var atlas = new ModSpec("saturn_atlas", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "saturn-atlas-companion-0.1.3.jar", new string('a', 64), 163840);
        var api = new ModSpec("fabric-api", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "fabric-api-0.165.2+26.3.jar", new string('b', 64), 1779306);
        string latest = args.Contains("--long-values") ? "0.1.3-preview-accessibility-long-label" : "0.1.3-preview";
        record = new("saturn", "0.1.2", [new("saturn_atlas", "saturn-atlas-companion-0.1.2.jar", new string('c', 64)), new("fabric-api", api.Asset, api.Sha256)]);
        if (name == "offline") checkFailed = true;
        else if (name != "initial")
        {
            pack = new(1, "saturn", latest, "26.3", "0.20.1", [atlas, api]);
            states = [(atlas, Compare.Update), (api, Compare.Current)];
        }
        string text = "Ready.";
        switch (name)
        {
            case "checked": case "settings": case "activity": text = $"Update available: 0.1.2 → {latest}."; break;
            case "busy": busy = true; progress.Visible = true; text = $"Installing {latest}: downloading and verifying files…"; break;
            case "success": record = record with { Version = latest }; states = [(atlas, Compare.Current), (api, Compare.Current)]; text = $"Installed {latest}. Your other mods were not touched."; break;
            case "error": failedInstallation = true; text = "That did not complete. Nothing was left half-done; see Activity for the reason."; break;
            case "offline": text = "Could not check for updates. Check your connection, then check again."; break;
            case "manager": managerRelease = new("9.9.9", SelfUpdate.Asset, 52000000, new string('d', 64), "https://github.com/Zigdon7/MooreMinecraft/releases/download/preview/Saturn-Mod-Manager.exe"); text = $"Update available: 0.1.2 → {latest}."; break;
        }
        status.Text = text; status.AccessibleDescription = text;
        output.Text = "12:00:01  Checking for updates…\r\n12:00:02  Checked 0.1.3-preview: 2 pinned, checksummed files.\r\n12:00:02  " + text + "\r\n";
        if (name == "settings") Go(settingsPage);
        if (name == "activity") Go(activityPage);
    }
    void TraceUi(string text)
    {
        var file = Environment.GetEnvironmentVariable("SATURN_UI_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(file)) return;
        try { File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); } catch (Exception) { }
    }
    void TraceFocus(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.Enter += (_, _) => TraceUi("enter " + child.GetType().Name + " " + child.AccessibleName);
            child.Leave += (_, _) => TraceUi("leave " + child.GetType().Name + " " + child.AccessibleName);
            TraceFocus(child);
        }
    }
    void EnlargeText(Control control)
    {
        // Snapshot before mutating parents: inherited fonts must not compound by nesting depth.
        var fonts = new Dictionary<Control, Font>();
        void Snapshot(Control c) { fonts[c] = c.Font; foreach (Control child in c.Controls) Snapshot(child); }
        Snapshot(control);
        foreach (var item in fonts) item.Key.Font = new Font(item.Value.FontFamily, item.Value.Size * 1.5f, item.Value.Style);
        side.Width = Px(230);
    }
    sealed record Saved(string Profile, string Manifest);
}
