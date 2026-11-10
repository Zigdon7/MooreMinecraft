using Saturn.Manager;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms.Automation;

ApplicationConfiguration.Initialize();
Application.Run(new ManagerForm(args));

// Native controls preserve Windows focus, UI Automation and contrast-theme behavior.
sealed class ManagerForm : Form
{
    readonly TextBox profile = new() { Dock = DockStyle.Fill, AccessibleName = "Minecraft game folder", AccessibleDescription = "The game directory used by your Fabric launcher profile." };
    readonly TextBox manifest = new() { Dock = DockStyle.Fill, AccessibleName = "Advanced pack manifest URL" };
    readonly Button browse = ActionButton("&Browse…", "Browse for Minecraft game folder");
    readonly Button check = ActionButton("&Check for updates", "Check for updates");
    readonly Button install = ActionButton("&Install update", "Install checked update");
    readonly Button rollback = ActionButton("&Restore previous mods…", "Restore previous managed mod set");
    readonly Button cancel = ActionButton("Ca&ncel operation", "Cancel current operation safely");
    readonly CheckBox detailsToggle = new() { Text = "Show &technical details", AutoSize = true, AccessibleName = "Show technical details" };
    readonly ListView mods = new() { Dock = DockStyle.Top, View = View.Details, FullRowSelect = true, Height = 144, AccessibleName = "Verified release files", AccessibleDescription = "Mod ID, release, filename and download size." };
    readonly TextBox output = new() { Dock = DockStyle.Top, Height = 130, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Operation details log" };
    readonly Label release = Copy("No update checked yet", 16, true);
    readonly Label requirements = Copy("Check the staging release to see its Minecraft and Fabric versions.");
    readonly Label contents = Copy("Your Atlas companion and required dependencies will appear here.");
    readonly Label installed = Copy("Select your Fabric profile’s game folder below.");
    readonly Label status = Copy("Ready — choose your game folder, then check for updates.");
    readonly Label nextStep = Copy("Next: check for updates. Nothing will be installed during this check.");
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = 8, Visible = false, Style = ProgressBarStyle.Marquee, AccessibleName = "Operation in progress" };
    readonly TableLayoutPanel details = Stack();
    readonly TableLayoutPanel folderEditor = Stack();
    readonly Label selectedFolder = Copy("");
    readonly CheckBox changeFolder = new() { Text = "Change &folder", AutoSize = true, Visible = false, AccessibleName = "Change selected game folder" };
    readonly TableLayoutPanel page = Stack();
    readonly TableLayoutPanel overview = Stack();
    TableLayoutPanel? folderSection, releaseSection;
    bool wideOverview;
    bool geometryQueued, applyingGeometry;
    int geometryPasses;
    readonly TableLayoutPanel shell = Stack();
    readonly Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true };
    readonly ToolTip hints = new() { AutoPopDelay = 20000 };
    readonly GitHubSource source = new();
    readonly bool preview;
    PackManifest? pack;
    CancellationTokenSource? cancellation;
    bool busy;
    bool updatingPreview;
    string? completedProfile;
    string? completedVersion;
    const string DefaultManifest = "https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/staging/pack.json";
    string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaturnModManager", "settings.json");

    public ManagerForm(string[] args)
    {
        preview = args.Contains("--ui-preview");
        Text = "Saturn Mod Manager — Staging";
        Font = new Font("Segoe UI", 10);
        install.Font = new Font(Font, FontStyle.Bold);
        install.EnabledChanged += (_, _) => StylePrimary();
        SystemColorsChanged += (_, _) => StylePrimary();
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(920, 820);
        MinimumSize = new Size(640, 520);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = SystemColors.Control;
        ForeColor = SystemColors.ControlText;
        page.Padding = new Padding(24, 18, 24, 18);
        page.Dock = DockStyle.Top;
        page.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        scroll.Controls.Add(page);
        shell.Dock = DockStyle.Fill; shell.AutoSize = false; shell.RowCount = 2;
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(scroll, 0, 0); Controls.Add(shell);
        scroll.ClientSizeChanged += (_, _) => ScheduleGeometry();

        var header = Stack();
        header.Margin = new Padding(0, 0, 0, 18);
        Add(header, Copy("Saturn Mod Manager", 20, true));
        Add(header, Copy("STAGING  /  Safe updates for your Minecraft profile", 10, true));
        Add(header, Copy("Your worlds and unrelated mods stay untouched."));
        Add(page, header);

        var folderCard = Card("1  Choose your Minecraft profile");
        var folderLabel = Copy("&Game folder"); folderLabel.UseMnemonic = true;
        Add(folderEditor, folderLabel); // Mnemonic transfers focus to the following text box.
        Add(folderEditor, profile);
        var folderActions = Actions(browse);
        Add(folderEditor, folderActions);
        Add(folderEditor, Copy("Use the game directory shown in your launcher’s Fabric profile."));
        Add(folderCard, selectedFolder); Add(folderCard, folderEditor);
        Add(folderCard, changeFolder);
        changeFolder.CheckedChanged += (_, _) => {
            // UIA Toggle must return before child HWNDs emit structure-change events.
            void ApplyExpansion() { ShowFolderEditor(); if (changeFolder.Checked) BeginInvoke(() => profile.Focus()); }
            if (IsHandleCreated) BeginInvoke(ApplyExpansion); else ShowFolderEditor();
        };
        Add(folderCard, installed);
        folderSection = folderCard;
        Add(page, overview);

        var releaseCard = Card("2  Review the staging update");
        Add(releaseCard, release);
        Add(releaseCard, requirements);
        Add(releaseCard, contents);
        Add(releaseCard, Actions(check));
        releaseSection = releaseCard; ArrangeOverview();

        var installCard = Card("3  Update, then launch Minecraft");

        Add(installCard, nextStep);
        Add(installCard, Actions(install, cancel, rollback));
        Add(installCard, progress);
        status.AccessibleName = "Update status";
        status.AccessibleRole = AccessibleRole.StaticText;
        status.Font = new Font(Font, FontStyle.Bold);
        Add(installCard, status);
        installCard.Margin = new Padding(24, 0, 24, 12);
        shell.Controls.Add(installCard, 0, 1);

        var setup = new LinkLabel { Text = "First time? Open the Fabric Loader installer website", AutoSize = true, AccessibleName = "Open official Fabric Loader installer website", Margin = new Padding(0, 8, 0, 8), LinkColor = SystemColors.HotTrack };
        setup.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://fabricmc.net/use/installer/") { UseShellExecute = true });
        Add(page, setup);
        Add(page, detailsToggle);
        details.Visible = false;
        details.Padding = new Padding(0, 12, 0, 0);
        Add(details, Copy("Advanced release source", 12, true));
        Add(details, Copy("Only change this URL if the Saturn server owner provides a different pack manifest."));
        var manifestLabel = Copy("Pack &manifest URL"); manifestLabel.UseMnemonic = true; Add(details, manifestLabel);
        Add(details, manifest);
        mods.Columns.Add("Mod ID"); mods.Columns.Add("Release"); mods.Columns.Add("File"); mods.Columns.Add("Size");
        Add(details, Copy("Verified release files", 12, true)); Add(details, mods);
        Add(details, Copy("Operation details", 12, true)); Add(details, output);
        Add(page, details);
        hints.SetToolTip(profile, "Choose the game directory of the Fabric profile you actually launch.");
        hints.SetToolTip(rollback, "Restores the previous managed mod set in the selected game folder. Worlds and unrelated mods are not changed.");
        profile.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        manifest.Text = DefaultManifest;
        if (!preview) RestoreSettings();
        profile.TextChanged += (_, _) => { if (!updatingPreview) { completedProfile = completedVersion = null; RefreshInstalled(); UpdateActions(); SetStatus("Game folder changed — review the selected folder before installing.", announce: false); } };
        profile.Validated += (_, _) => { if (!preview && !busy) SetStatus(installed.Text); };
        manifest.TextChanged += (_, _) => { if (!updatingPreview) { completedProfile = completedVersion = null; pack = null; ShowRelease(); UpdateActions(); SetStatus("Source changed — check for updates again.", announce: false); } };
        manifest.Validated += (_, _) => { if (!preview && !busy && pack is null) SetStatus("Source changed — check for updates again."); };
        browse.Click += (_, _) => {
            if (preview) return;
            using var dialog = new FolderBrowserDialog { Description = "Choose your Fabric profile’s game directory", ShowNewFolderButton = false, SelectedPath = profile.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK) profile.Text = dialog.SelectedPath;
        };
        detailsToggle.CheckedChanged += (_, _) => {
            void ApplyDetails() { details.Visible = detailsToggle.Checked; ScheduleGeometry(); if (details.Visible && IsHandleCreated) BeginInvoke(() => scroll.ScrollControlIntoView(details)); }
            if (IsHandleCreated) BeginInvoke(ApplyDetails); else ApplyDetails();
        };
        check.Click += async (_, _) => await Run(check, "Checking the staging release…", async token => {
            // Invalidate a previous check before attempting a new one; failed checks never leave a stale install action.
            pack = null; ShowRelease();
            var fetched = await source.Fetch(manifest.Text.Trim(), token);
            pack = fetched; ShowRelease(); Save();
            Log($"Checked {pack.Version}: {pack.Mods.Count} pinned, checksummed files.");
            SetStatus("Release checked — review the versions above, then install.");
        });
        install.Click += async (_, _) => {
            if (preview || pack is null || !Directory.Exists(profile.Text.Trim())) return;
            var selected = pack; string path = profile.Text.Trim();
            if (MessageBox.Show(this, $"Install Saturn {selected.Version} into:\n{path}\n\nClose Minecraft first. Only managed mods will change; backups will be kept.", "Install staging update?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await Run(install, "Updating mods — downloading, verifying and applying files…", async token => {
                await Task.Run(() => new Installer(path, source, Log).Install(selected, token), token);
                Save(); RefreshInstalled(); completedProfile = path; completedVersion = selected.Version;
                SetStatus("Update complete — launch your Fabric profile and join staging.");
            });
        };
        rollback.Click += async (_, _) => {
            if (preview) return;
            string path = profile.Text.Trim();
            var summary = RecoverySummary(path);
            if (MessageBox.Show(this, $"{summary}\n\nGame folder:\n{path}\n\nClose Minecraft first. This restores the previous managed mod set, not your world.", "Restore previous mods?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await Run(rollback, "Restoring the previous mod set…", async token => {
                await Task.Run(() => new Installer(path, source, Log).Rollback(), token);
                completedProfile = completedVersion = null; RefreshInstalled(); SetStatus("Previous mods restored — you can launch Minecraft.");
            });
        };
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; SetStatus("Cancellation requested — waiting for file recovery to finish."); };
        FormClosing += (_, e) => {
            if (busy) { e.Cancel = true; cancellation?.Cancel(); SetStatus("Please wait — finishing or recovering the current operation before closing."); }
            else { source.Dispose(); hints.Dispose(); }
        };
        RefreshInstalled(); UpdateActions();
        // Offline visual/accessibility fixture only: never fetches or mutates a user's game directory.
        if (preview) { ConfigurePreview(args); TraceFocus(shell); }
        SetTabOrder(shell);
        Shown += (_, _) => { Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height); Width = Math.Min(Width, Screen.FromControl(this).WorkingArea.Width); ApplyGeometry(); if (details.Visible) scroll.ScrollControlIntoView(details); else if (folderEditor.Visible) profile.Focus(); else check.Focus(); };
    }

    static Button ActionButton(string text, string name) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 7, 12, 7), Margin = new Padding(0, 4, 10, 4), UseVisualStyleBackColor = true, AccessibleName = name };
    static Label Copy(string text, float size = 10, bool bold = false) => new() { Text = text, AutoSize = true, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 3, 0, 6), UseMnemonic = false };
    static TableLayoutPanel Stack() {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return panel;
    }
    static TableLayoutPanel Card(string title) {
        var panel = Stack(); panel.Padding = new Padding(16, 12, 16, 12); panel.Margin = new Padding(0, 0, 0, 12);

        panel.BackColor = SystemColors.Window; panel.ForeColor = SystemColors.WindowText;
        Add(panel, Copy(title, 12, true)); return panel;
    }
    static FlowLayoutPanel Actions(params Control[] controls) {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        row.Controls.AddRange(controls); return row;
    }
    static void Add(TableLayoutPanel parent, Control child) { int row = parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(child, 0, row); }
    static void SetTabOrder(Control parent) { int index = 0; foreach (Control child in parent.Controls) { child.TabIndex = index++; if (child is Label) child.TabStop = false; SetTabOrder(child); } }
    static void WrapLabels(Control parent) {
        foreach (Control child in parent.Controls) {
            if (child is Label label) { var size = new Size(Math.Max(160, parent.ClientSize.Width - parent.Padding.Horizontal - label.Margin.Horizontal), 0); if (label.MaximumSize != size) label.MaximumSize = size; }
            if (child.HasChildren) WrapLabels(child);
        }
    }
    void ResizeColumns() {
        if (mods.Columns.Count != 4) return;
        int width = Math.Max(280, mods.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        mods.Columns[0].Width = width * 23 / 100; mods.Columns[1].Width = width * 21 / 100;
        mods.Columns[2].Width = width * 43 / 100; mods.Columns[3].Width = width - mods.Columns[0].Width - mods.Columns[1].Width - mods.Columns[2].Width;
    }
    void ShowRelease() {
        mods.Items.Clear(); ShowFolderEditor();
        if (pack is null) { release.Text = "No update checked yet"; requirements.Text = "Check the staging release to see its Minecraft and Fabric versions."; contents.Text = "Your Atlas companion and required dependencies will appear here."; return; }
        release.Text = $"Saturn · {pack.Version}";
        requirements.Text = $"Minecraft Java {pack.Minecraft}  •  Fabric Loader {pack.FabricLoader}";
        check.Text = "&Check again";
        contents.Text = string.Join(Environment.NewLine, pack.Mods.Select(m => $"• {FriendlyName(m.Id)}  —  {m.Size / 1048576.0:0.0} MB"));
        foreach (var m in pack.Mods) mods.Items.Add(new ListViewItem([m.Id, m.Tag, m.Asset, $"{m.Size / 1048576.0:0.0} MB"]));
        ScheduleGeometry(); ResizeColumns();
    }
    static string FriendlyName(string id) => id switch { "saturn_atlas" or "saturn_atlas_companion" => "Saturn Atlas companion", "fabric-api" or "fabric_api" => "Fabric API", _ => id.Replace('_', ' ').Replace('-', ' ') };
    void RefreshInstalled() {
        try {
            string file = Path.Combine(profile.Text.Trim(), ".saturn-manager", "installed.json");
            if (File.Exists(file)) {
                var record = Rules.Read<Installed>(file); completedProfile = profile.Text.Trim(); completedVersion = record.Version;
                installed.Text = $"Installed: {record.Version}. Your last managed update is recorded.";
            } else installed.Text = Directory.Exists(profile.Text.Trim()) ? "Folder found. No Saturn-managed installation recorded yet." : "Folder not found — browse to an existing Minecraft game directory.";
        } catch (Exception e) { installed.Text = "Installation record could not be read. Open technical details for the reason."; Log("Installation record: " + e.Message); }
        ScheduleGeometry();
    }
    static string RecoverySummary(string path) {
        try {
            string root = Path.Combine(path, ".saturn-manager");
            var id = File.ReadAllText(Path.Combine(root, "last-transaction.txt")).Trim();
            if (!Guid.TryParseExact(id, "N", out _)) return "No valid recovery record found.";
            string previous = Path.Combine(root, "transactions", id, "previous-state.json");
            return File.Exists(previous) ? $"Restore Saturn {Rules.Read<Installed>(previous).Version}." : "Restore the mod set from before the last Saturn installation.";
        } catch { return "The last completed managed update will be restored if a valid backup exists."; }
    }
    bool CanRestore() {
        try {
            string root = Path.Combine(profile.Text.Trim(), ".saturn-manager");
            string id = File.ReadAllText(Path.Combine(root, "last-transaction.txt")).Trim();
            return Guid.TryParseExact(id, "N", out _) && Rules.Read<Journal>(Path.Combine(root, "transactions", id, "journal.json")).Status == "Committed";
        } catch { return false; }
    }
    void UpdateActions() {
        browse.Enabled = check.Enabled = profile.Enabled = manifest.Enabled = changeFolder.Enabled = !busy;
        install.Text = completedProfile == profile.Text.Trim() && completedVersion == pack?.Version ? "Re&install…" : "&Install update";
        install.AccessibleName = install.Text.StartsWith("Re") ? "Reinstall checked update" : "Install checked update";
        install.AccessibleDescription = install.Text.StartsWith("Re") ? "Install the checked version again to repair its managed files." : "Install the reviewed release into the selected game folder.";
        install.Enabled = !busy && pack is not null && Directory.Exists(profile.Text.Trim());
        rollback.Enabled = !busy && CanRestore(); cancel.Visible = busy; cancel.Enabled = busy;
        nextStep.Text = busy ? "Please wait. Cancel safely if needed; recovery finishes before controls unlock." : completedProfile == profile.Text.Trim() && completedVersion == pack?.Version ? "Next: launch your Fabric profile. This update is installed." : pack is null ? "Next: check for updates. Nothing will be installed during this check." : !Directory.Exists(profile.Text.Trim()) ? "Next: choose an existing game folder to enable installation." : "Next: close Minecraft, then install. Downloads are verified and backups kept.";
        AcceptButton = install.Enabled ? install : check; StylePrimary();
    }
    async Task Run(Control initiator, string activity, Func<CancellationToken, Task> action) {
        if (busy || preview) return;
        busy = true; cancellation = new(); progress.Visible = true; UpdateActions(); SetStatus(activity);
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("Cancelled — no update completed. Check details before trying again."); Log("Operation cancelled; any started file transaction attempts recovery before returning."); }
        catch (Exception e) { SetStatus("Could not complete the operation — open technical details for the reason."); Log("Stopped: " + e.Message); MessageBox.Show(this, e.Message, "Saturn update needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { busy = false; cancellation.Dispose(); cancellation = null; progress.Visible = false; UpdateActions(); (initiator.Enabled ? initiator : check).Focus(); }
    }
    void SetStatus(string text, bool announce = true) {
        status.Text = text; status.AccessibleDescription = text;
        if (announce) status.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text);
        if (announce) Log(text); ScheduleGeometry();
    }
    void Log(string text) { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(() => Log(text)); return; } output.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine); }
    void RestoreSettings() {
        try { if (File.Exists(SettingsFile)) { var s = JsonSerializer.Deserialize<Saved>(File.ReadAllText(SettingsFile)); if (s is not null) { profile.Text = s.Profile; manifest.Text = s.Manifest; } } }
        catch (Exception e) { Log("Could not restore settings: " + e.Message); }
    }
    void Save() { if (preview) return; Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, JsonSerializer.Serialize(new Saved(profile.Text.Trim(), manifest.Text.Trim()))); }
    void ConfigurePreview(string[] args) {
        updatingPreview = true;
        profile.Text = @"C:\Users\Player\AppData\Roaming\.minecraft";
        installed.Text = "Installed: 0.1.2. Your last managed update is recorded.";
        string state = args.SkipWhile(a => a != "--ui-preview").Skip(1).FirstOrDefault() ?? "initial";
        if (state != "initial") {
            pack = new(1, "saturn", "0.1.3-preview", "26.3", "0.20.1", [new("saturn_atlas", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "saturn-atlas-companion.jar", new string('a',64), 163840), new("fabric-api", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "fabric-api.jar", new string('b',64), 4194304)]);
            ShowRelease(); nextStep.Text = "Next: close Minecraft, then install. Downloads are verified and backups kept.";
            status.Text = state switch { "busy" => "Updating mods — downloading and verifying files…", "success" => "Update complete — launch your Fabric profile and join staging.", "error" => "Could not download the update. Check your connection, then try again.", _ => "Release checked — review the versions above, then install." };
        }
        if (state == "busy") nextStep.Text = "Please wait. Cancel safely if needed; recovery finishes before controls unlock.";
        if (state == "success") { completedProfile = profile.Text; completedVersion = pack?.Version; install.Text = "Re&install…"; install.AccessibleName = "Reinstall checked update"; installed.Text = "Installed: 0.1.3-preview. Your last managed update is recorded."; nextStep.Text = "Next: launch your Fabric profile. This update is installed."; }
        if (state == "details") detailsToggle.Checked = true;
        busy = state == "busy"; progress.Visible = busy; cancel.Visible = busy; cancel.Enabled = busy;
        browse.Enabled = check.Enabled = profile.Enabled = manifest.Enabled = changeFolder.Enabled = !busy;
        install.Enabled = !busy && pack is not null; rollback.Enabled = !busy;
        // Preview guards in each handler prevent dialogs, fetches and installer effects.
        if (args.Contains("--large-text")) { EnlargeText(this); ArrangeOverview(); }
        if (args.Contains("--narrow")) Size = new Size(660, 720);
        updatingPreview = false; StylePrimary();
    }
    void ScheduleGeometry() {
        if (applyingGeometry || geometryQueued || !IsHandleCreated || IsDisposed) return;
        geometryQueued = true;
        BeginInvoke(() => { geometryQueued = false; if (!IsDisposed) ApplyGeometry(); });
    }
    void ApplyGeometry() {
        if (applyingGeometry) return;
        applyingGeometry = true;
        try {
            int width = page.ClientSize.Width;
            ArrangeOverview(); page.PerformLayout(); WrapLabels(shell); ResizeColumns();
            TraceUi("geometry " + (++geometryPasses) + " width=" + width + " wide=" + wideOverview);
        } finally { applyingGeometry = false; }
    }
    void TraceUi(string text) {
        if (!preview) return;
        var file = Environment.GetEnvironmentVariable("SATURN_UI_TRACE_FILE");
        if (string.IsNullOrWhiteSpace(file)) return;
        try { File.AppendAllText(file, DateTime.UtcNow.ToString("O") + " " + text + Environment.NewLine); } catch { }
    }
    void TraceFocus(Control parent) {
        foreach (Control child in parent.Controls) {
            child.Enter += (_, _) => TraceUi("enter " + child.GetType().Name + " " + child.AccessibleName);
            child.Leave += (_, _) => TraceUi("leave " + child.GetType().Name + " " + child.AccessibleName);
            TraceFocus(child);
        }
    }
    void ShowFolderEditor() {
        changeFolder.Visible = pack is not null;
        folderEditor.Visible = pack is null || changeFolder.Checked || (!preview && !Directory.Exists(profile.Text.Trim()));
        selectedFolder.Text = profile.Text.Trim(); selectedFolder.Visible = !folderEditor.Visible;
        ScheduleGeometry();
    }
    void StylePrimary() {
        if (SystemInformation.HighContrast || install.Text.Replace("&", "").Contains("Reinstall")) { install.BackColor = SystemColors.Control; install.ForeColor = SystemColors.ControlText; install.FlatStyle = FlatStyle.System; install.UseVisualStyleBackColor = true; return; }
        install.FlatStyle = FlatStyle.Flat; install.UseVisualStyleBackColor = false;
        install.BackColor = install.Enabled ? Color.FromArgb(22, 71, 109) : SystemColors.Control;
        install.ForeColor = install.Enabled ? Color.White : SystemColors.GrayText;
        install.FlatAppearance.BorderColor = install.Enabled ? Color.FromArgb(22, 71, 109) : SystemColors.ControlDark;
        install.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 88, 130);
        install.FlatAppearance.MouseDownBackColor = Color.FromArgb(15, 54, 83);
    }
    void ArrangeOverview() {
        if (folderSection is null || releaseSection is null) return;
        bool wide = page.Width - page.Padding.Horizontal >= 780 * (Font.Size / 10f);
        if (overview.Controls.Count != 0 && wide == wideOverview) return;
        overview.SuspendLayout(); overview.ColumnStyles.Clear(); overview.RowStyles.Clear();
        wideOverview = wide; overview.ColumnCount = wide ? 2 : 1; overview.RowCount = wide ? 1 : 2;
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, wide ? 50 : 100));
        if (wide) overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        overview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (!wide) overview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        folderSection.Margin = new Padding(0, 0, wide ? 12 : 0, 12);
        if (!overview.Controls.Contains(folderSection)) overview.Controls.Add(folderSection);
        if (!overview.Controls.Contains(releaseSection)) overview.Controls.Add(releaseSection);
        overview.SetCellPosition(folderSection, new TableLayoutPanelCellPosition(0, 0));
        overview.SetCellPosition(releaseSection, new TableLayoutPanelCellPosition(wide ? 1 : 0, wide ? 0 : 1));
        overview.ResumeLayout(true); SetTabOrder(shell);
    }
    static void EnlargeText(Control control) {
        // Snapshot before mutating parents: inherited fonts must not compound by nesting depth.
        var fonts = new Dictionary<Control, Font>();
        void Snapshot(Control c) { fonts[c] = c.Font; foreach (Control child in c.Controls) Snapshot(child); }
        Snapshot(control);
        foreach (var item in fonts) item.Key.Font = new Font(item.Value.FontFamily, item.Value.Size * 1.5f, item.Value.Style);
    }
    sealed record Saved(string Profile, string Manifest);
}
