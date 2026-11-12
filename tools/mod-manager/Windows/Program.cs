using Saturn.Manager;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Forms.Automation;

ApplicationConfiguration.Initialize();
// Let WinForms theme native controls/title bars using the Windows app preference.
Application.SetColorMode(SystemColorMode.System);
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
    readonly ListView mods = new() { Dock = DockStyle.Top, View = View.Details, FullRowSelect = true, Height = 144, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Verified release files", AccessibleDescription = "Mod ID, release, filename and download size." };
    readonly TextBox output = new() { Dock = DockStyle.Top, Height = 130, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, AccessibleName = "Operation details log" };
    readonly Label release = Copy("Not checked yet", 13, true);
    readonly Label requirements = Copy("Check for updates to see what the latest release contains.");
    readonly Label contents = Copy("");
    readonly Label installed = Copy("Select your Fabric profile’s game folder.");
    readonly Label status = Copy("No changes have been made.");
    readonly Label nextStep = Copy("Next: check for updates. Nothing will be installed during this check.");
    readonly ProgressBar progress = new() { Dock = DockStyle.Top, Height = Px(4), Margin = new Padding(0, Px(6), 0, Px(2)), Visible = false, Style = ProgressBarStyle.Marquee, AccessibleName = "Operation in progress" };
    readonly TableLayoutPanel details = Stack();
    readonly TableLayoutPanel folderEditor = Stack();
    readonly Label selectedFolder = Copy("");
    readonly CheckBox changeFolder = new() { Text = "Change &folder", AutoSize = true, Visible = false, AccessibleName = "Change selected game folder" };
    readonly TableLayoutPanel page = Stack();
    TableLayoutPanel? folderSection, folderRow;
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
    bool failedInstallation;
    Label? actionHeading;
    readonly Label logHint = Copy("No additional operation details recorded.");
    bool updatingPreview;
    string? completedProfile;
    string? completedVersion;
    const string DefaultManifest = "https://raw.githubusercontent.com/Zigdon7/MooreMinecraft/main/releases/staging/pack.json";
    string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaturnModManager", "settings.json");

    public ManagerForm(string[] args)
    {
        preview = args.Contains("--ui-preview");
        Text = "Saturn Mod Manager";
        Font = new Font("Segoe UI", 10);
        install.EnabledChanged += (_, _) => StylePrimary();
        SystemColorsChanged += (_, _) => ApplySystemPalette(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        // Sizes are logical pixels scaled to the system DPI, so the window is not cramped at 150%.
        ClientSize = new Size(Px(600), Px(660));
        MinimumSize = new Size(Px(440), Px(420));
        StartPosition = FormStartPosition.CenterScreen;
        page.Padding = new Padding(Px(28), Px(22), Px(28), Px(12));
        page.Dock = DockStyle.Top;
        page.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        scroll.Controls.Add(page);
        shell.Dock = DockStyle.Fill; shell.AutoSize = false; shell.RowCount = 2;
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(scroll, 0, 0); Controls.Add(shell);
        scroll.ClientSizeChanged += (_, _) => ScheduleGeometry();

        var title = Copy("Saturn Mod Manager", 18, true); title.Margin = new Padding(0, 0, 0, Px(2));
        Add(page, title);
        var subtitle = Copy("Staging channel. Your worlds and other mods are never touched."); subtitle.Margin = new Padding(0, 0, 0, Px(10));
        Add(page, subtitle);

        // One column, no boxes: sections are separated by a heading and a hairline.
        var folder = Stack();
        Add(folder, SectionLabel("Game folder"));
        folderRow = Row(selectedFolder, changeFolder);
        Add(folder, folderRow);
        Add(folderEditor, Row(profile, browse));
        Add(folderEditor, Copy("Use the game directory shown in your launcher’s Fabric profile."));
        Add(folder, folderEditor);
        changeFolder.CheckedChanged += (_, _) => {
            // UIA Toggle must return before child HWNDs emit structure-change events.
            void ApplyExpansion() { ShowFolderEditor(); if (changeFolder.Checked) BeginInvoke(() => profile.Focus()); }
            if (IsHandleCreated) BeginInvoke(ApplyExpansion); else ShowFolderEditor();
        };
        Add(folder, installed);
        folderSection = folder;
        Add(page, folder);
        Add(page, Rule());

        var latest = Stack();
        Add(latest, SectionLabel("Latest release"));
        Add(latest, Row(release, check));
        Add(latest, requirements);
        Add(latest, contents);
        Add(page, latest);
        Add(page, Rule());

        // Install, recovery and status stay visible in a footer while the page above scrolls.
        var installCard = Stack(); installCard.Tag = "bar"; installCard.Margin = Padding.Empty;
        var footerRule = Rule(); footerRule.Margin = Padding.Empty; Add(installCard, footerRule);
        var footer = Stack(); footer.Padding = new Padding(Px(28), Px(14), Px(28), Px(16)); Add(installCard, footer);
        actionHeading = Copy("Install when ready", 12, true); Add(footer, actionHeading);
        Add(footer, nextStep);
        var actions = Actions(install, cancel, rollback); actions.Margin = new Padding(Px(2), Px(6), 0, Px(4)); Add(footer, actions);
        Add(footer, progress);
        status.AccessibleName = "Update status";
        status.AccessibleRole = AccessibleRole.StaticText;
        Add(footer, status);
        shell.Controls.Add(installCard, 0, 1);

        detailsToggle.Margin = new Padding(Px(3), Px(8), 0, Px(6)); contents.Visible = false;
        Add(page, detailsToggle);
        var setup = new LinkLabel { Text = "New to Fabric? Open the Fabric Loader installer website", AutoSize = true, AccessibleName = "Open official Fabric Loader installer website", Margin = new Padding(0, 0, 0, Px(8)), LinkColor = SystemColors.WindowText };
        setup.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://fabricmc.net/use/installer/") { UseShellExecute = true });
        Add(page, setup);
        details.Visible = false;
        details.Padding = new Padding(0, Px(8), 0, 0);
        Add(details, SectionLabel("Release source"));
        Add(details, Copy("Only change this URL if the Saturn server owner provides a different pack manifest."));
        var manifestLabel = Copy("Pack &manifest URL"); manifestLabel.UseMnemonic = true; Add(details, manifestLabel);
        Add(details, manifest);
        mods.Columns.Add("Mod ID"); mods.Columns.Add("Release"); mods.Columns.Add("File"); mods.Columns.Add("Size");
        Add(details, SectionLabel("Verified release files")); Add(details, mods);
        Add(details, SectionLabel("Operation details")); Add(details, logHint); Add(details, output);
        output.TextChanged += (_, _) => { logHint.Visible = output.TextLength == 0; ScheduleGeometry(); };
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
            SetStatus($"Release lists {pack.Mods.Count} pinned, checksummed files. Nothing installed yet.");
        });
        install.Click += async (_, _) => {
            if (preview || pack is null || !Directory.Exists(profile.Text.Trim())) return;
            var selected = pack; string path = profile.Text.Trim();
            if (MessageBox.Show(this, $"Install Saturn {selected.Version} into:\n{path}\n\nClose Minecraft first. Only managed mods will change; backups will be kept.", "Install staging update?", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            await Run(install, "Updating mods — downloading, verifying and applying files…", async token => {
                await Task.Run(() => new Installer(path, source, Log).Install(selected, token), token);
                Save(); RefreshInstalled(); completedProfile = path; completedVersion = selected.Version;
                SetStatus("Managed mods updated. Unrelated mods preserved.");
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
        ShowFolderEditor(); RefreshInstalled(); UpdateActions();
        // Offline visual/accessibility fixture only: never fetches or mutates a user's game directory.
        if (preview) { ConfigurePreview(args); TraceFocus(shell); }
        ApplySystemPalette(this);
        SetTabOrder(shell);
        Shown += (_, _) => { Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height); Width = Math.Min(Width, Screen.FromControl(this).WorkingArea.Width); ApplyGeometry(); if (details.Visible) scroll.ScrollControlIntoView(details); else if (folderEditor.Visible) profile.Focus(); else check.Focus(); };
    }

    static readonly float Dpi = SystemDpi();
    static float SystemDpi() { using var g = Graphics.FromHwnd(IntPtr.Zero); return g.DpiX / 96f; }
    /// <summary>Logical pixels to device pixels.</summary>
    static int Px(int value) => (int)Math.Round(value * Dpi);
    static Button ActionButton(string text, string name) => new() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Px(12), Px(4), Px(12), Px(4)), Margin = new Padding(0, Px(2), Px(8), Px(2)), UseVisualStyleBackColor = true, AccessibleName = name };
    static Label Copy(string text, float size = 10, bool bold = false) => new() { Text = text, AutoSize = true, Font = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", size), Margin = new Padding(0, Px(2), 0, Px(4)), UseMnemonic = false };
    static Label SectionLabel(string text) { var label = Copy(text.ToUpperInvariant(), 8.5f, true); label.AccessibleName = text; label.Margin = new Padding(0, Px(10), 0, Px(6)); return label; }
    static Panel Rule() => new() { Height = 1, Dock = DockStyle.Top, Margin = new Padding(0, Px(12), 0, Px(4)), Tag = "rule", BackColor = SystemColors.ControlDark, TabStop = false };
    static TableLayoutPanel Stack() {
        var panel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); return panel;
    }
    /// <summary>Content on the left, its action right-aligned on the same line.</summary>
    static TableLayoutPanel Row(Control left, Control right) {
        var row = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, Margin = Padding.Empty, Tag = "row" };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Anchor = left is Label ? AnchorStyles.Left : AnchorStyles.Left | AnchorStyles.Right; left.Dock = DockStyle.None;
        right.Anchor = AnchorStyles.Right; right.Margin = new Padding(Px(12), Px(2), 0, Px(2));
        row.Controls.Add(left, 0, 0); row.Controls.Add(right, 1, 0); return row;
    }
    static FlowLayoutPanel Actions(params Control[] controls) {
        var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, WrapContents = true, Margin = Padding.Empty };
        row.Controls.AddRange(controls); return row;
    }
    static void Add(TableLayoutPanel parent, Control child) { int row = parent.RowCount++; parent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); parent.Controls.Add(child, 0, row); }
    static void SetTabOrder(Control parent) { int index = 0; foreach (Control child in parent.Controls) { child.TabIndex = index++; if (child is Label) child.TabStop = false; SetTabOrder(child); } }
    static void WrapLabels(Control parent) {
        foreach (Control child in parent.Controls) {
            if (child is Label label) {
                // In a row, the label wraps in the space its right-aligned action leaves.
                int reserved = 0;
                if (Equals(parent.Tag, "row")) foreach (Control sibling in parent.Controls) if (sibling != label && sibling.Visible) reserved += sibling.Width + sibling.Margin.Horizontal;
                var size = new Size(Math.Max(Px(120), parent.ClientSize.Width - parent.Padding.Horizontal - label.Margin.Horizontal - reserved), 0);
                if (label.MaximumSize != size) label.MaximumSize = size;
            }
            if (child.HasChildren) WrapLabels(child);
        }
    }
    void ResizeColumns() {
        if (mods.Columns.Count != 4) return;
        if (pack is not null) mods.Height = Math.Clamp(pack.Mods.Count, 1, 5) * Math.Max(22, mods.Font.Height + 6) + mods.Font.Height + 14;
        int width = Math.Max(280, mods.ClientSize.Width - SystemInformation.VerticalScrollBarWidth);
        mods.Columns[0].Width = width * 23 / 100; mods.Columns[1].Width = width * 21 / 100;
        mods.Columns[2].Width = width * 43 / 100; mods.Columns[3].Width = width - mods.Columns[0].Width - mods.Columns[1].Width - mods.Columns[2].Width;
    }
    void ShowRelease() {
        mods.Items.Clear(); ShowFolderEditor();
        contents.Visible = pack is not null;
        if (pack is null) { release.Text = "Not checked yet"; requirements.Text = "Check for updates to see what the latest release contains."; contents.Text = ""; return; }
        release.Text = $"Saturn {pack.Version}";
        requirements.Text = $"Minecraft Java {pack.Minecraft}  ·  Fabric Loader {pack.FabricLoader}";
        check.Text = "&Check again";
        contents.Text = string.Join(Environment.NewLine, pack.Mods.Select(m => $"{FriendlyName(m.Id)}, {m.Size / 1048576.0:0.0} MB"));
        foreach (var m in pack.Mods) mods.Items.Add(new ListViewItem([m.Id, m.Tag, m.Asset, $"{m.Size / 1048576.0:0.0} MB"]));
        ScheduleGeometry(); ResizeColumns();
    }
    static string FriendlyName(string id) => id switch { "saturn_atlas" or "saturn_atlas_companion" => "Saturn Atlas companion", "fabric-api" or "fabric_api" => "Fabric API", _ => id.Replace('_', ' ').Replace('-', ' ') };
    void RefreshInstalled() {
        try {
            string file = Path.Combine(profile.Text.Trim(), ".saturn-manager", "installed.json");
            if (File.Exists(file)) {
                var record = Rules.Read<Installed>(file); completedProfile = profile.Text.Trim(); completedVersion = record.Version;
                installed.Text = $"Installed: Saturn {record.Version}";
            } else installed.Text = Directory.Exists(profile.Text.Trim()) ? "Nothing installed by this manager yet." : "Folder not found. Browse to an existing Minecraft game directory.";
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
        bool upToDate = completedProfile == profile.Text.Trim() && completedVersion == pack?.Version;
        install.Text = upToDate ? "Re&install…" : failedInstallation ? "&Try installation again" : "&Install update";
        if (actionHeading is not null) actionHeading.Text = busy ? "Updating your mods" : failedInstallation ? "Update unsuccessful" : upToDate ? "You’re up to date" : pack is null ? "Install when ready" : "Ready to install";
        install.AccessibleName = upToDate ? "Reinstall checked update" : failedInstallation ? "Retry checked installation" : "Install checked update";
        install.AccessibleDescription = install.Text.StartsWith("Re") ? "Install the checked version again to repair its managed files." : "Install the reviewed release into the selected game folder.";
        install.Enabled = !busy && pack is not null && Directory.Exists(profile.Text.Trim());
        rollback.Enabled = !busy && CanRestore(); cancel.Visible = busy; cancel.Enabled = busy;
        nextStep.Text = busy ? "Please wait. Cancel safely if needed; recovery finishes before controls unlock." : completedProfile == profile.Text.Trim() && completedVersion == pack?.Version ? "Next: launch your Fabric profile and join staging." : pack is null ? "Next: check for updates. Nothing will be installed during this check." : !Directory.Exists(profile.Text.Trim()) ? "Next: choose an existing game folder to enable installation." : "Next: close Minecraft, then install. Backups are kept.";
        AcceptButton = install.Enabled && !upToDate ? install : check; StylePrimary();
    }
    async Task Run(Control initiator, string activity, Func<CancellationToken, Task> action) {
        if (busy || preview) return;
        failedInstallation = false; busy = true; cancellation = new(); progress.Visible = true; UpdateActions(); SetStatus(activity);
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { SetStatus("Cancelled — no update completed. Check details before trying again."); Log("Operation cancelled; any started file transaction attempts recovery before returning."); }
        catch (Exception e) { failedInstallation = ReferenceEquals(initiator, install); SetStatus("Could not complete the operation — open technical details for the reason."); Log("Stopped: " + e.Message); MessageBox.Show(this, e.Message, "Saturn update needs attention", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
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
        installed.Text = "Installed: Saturn 0.1.2";
        string state = args.SkipWhile(a => a != "--ui-preview").Skip(1).FirstOrDefault() ?? "initial";
        if (state != "initial") {
            pack = new(1, "saturn", "0.1.3-preview", "26.3", "0.20.1", [new("saturn_atlas", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "saturn-atlas-companion.jar", new string('a',64), 163840), new("fabric-api", "Zigdon7/MooreMinecraft", "staging-v0.1.3", "fabric-api.jar", new string('b',64), 4194304)]);
            ShowRelease(); nextStep.Text = "Next: close Minecraft, then install. Backups are kept.";
            status.Text = state switch { "busy" => "Updating mods — downloading and verifying files…", "success" => "Managed mods updated. Unrelated mods preserved.", "error" => "Could not download the update. Check your connection, then try again.", _ => "Release lists 2 pinned, checksummed files. Nothing installed yet." };
        }
        if (state == "error") { if (actionHeading is not null) actionHeading.Text = "Update unsuccessful"; failedInstallation = true; install.Text = "&Try installation again"; install.AccessibleName = "Retry checked installation"; }
        if (state != "initial" && state != "error" && state != "busy" && state != "success" && actionHeading is not null) actionHeading.Text = "Ready to install";
        if (state == "busy" && actionHeading is not null) actionHeading.Text = "Updating your mods";
        if (state == "busy") nextStep.Text = "Please wait. Cancel safely if needed; recovery finishes before controls unlock.";
        if (state == "success") { completedProfile = profile.Text; completedVersion = pack?.Version; if (actionHeading is not null) actionHeading.Text = "You’re up to date"; install.Text = "Re&install…"; install.AccessibleName = "Reinstall checked update"; installed.Text = "Installed: Saturn 0.1.3-preview"; nextStep.Text = "Next: launch your Fabric profile and join staging."; }
        if (args.Contains("--long-values")) {
            profile.Text = @"C:\Users\Player\Documents\Minecraft profiles\Saturn staging accessibility review\Instances\Long named Fabric profile for shared Atlas testing";
            if (pack is not null) { pack = pack with { Version = "0.1.3-preview-accessibility-long-label" }; ShowRelease(); }
        }
        if (state == "details") detailsToggle.Checked = true;
        busy = state == "busy"; progress.Visible = busy; cancel.Visible = busy; cancel.Enabled = busy;
        browse.Enabled = check.Enabled = profile.Enabled = manifest.Enabled = changeFolder.Enabled = !busy;
        install.Enabled = !busy && pack is not null; rollback.Enabled = !busy;
        // Preview guards in each handler prevent dialogs, fetches and installer effects.
        if (args.Contains("--large-text")) EnlargeText(this);
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
            page.PerformLayout(); WrapLabels(shell); ResizeColumns();
            TraceUi("geometry " + (++geometryPasses) + " width=" + width);
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
        bool showEditor = pack is null || changeFolder.Checked || (!preview && !Directory.Exists(profile.Text.Trim()));
        bool canChange = pack is not null;
        changeFolder.Visible = canChange; folderEditor.Visible = showEditor;
        selectedFolder.Text = profile.Text.Trim(); selectedFolder.Visible = !showEditor;
        bool showRow = !showEditor || canChange;
        if (folderRow is not null) folderRow.Visible = showRow;
        // Visible includes ancestor visibility; use intended flags during construction.
        if (folderSection is not null && folderRow is not null) foreach (var item in new[] {(Control: (Control)folderRow, Show: showRow), (Control: (Control)folderEditor, Show: showEditor)}) {
            var row = folderSection.RowStyles[folderSection.GetRow(item.Control)]; row.SizeType = item.Show ? SizeType.AutoSize : SizeType.Absolute; row.Height = 0;
        }
        ScheduleGeometry();
    }
    void ApplySystemPalette(Control root) {
        // Explicit card/text colors are snapshots: refresh them when Windows changes
        // light/dark or accessibility colors, not just when the process starts.
        // The page is the window surface; only the footer bar uses the control surface.
        if (root == this) { BackColor = SystemColors.Window; ForeColor = SystemColors.WindowText; }
        else if (root is TextBoxBase or ListView) { root.BackColor = SystemColors.Window; root.ForeColor = SystemColors.WindowText; }
        else if (Equals(root.Tag, "bar")) { root.BackColor = SystemColors.Control; root.ForeColor = SystemColors.ControlText; }
        else if (Equals(root.Tag, "rule")) root.BackColor = SystemColors.ControlDark;
        if (root is LinkLabel link) {
            link.LinkColor = SystemColors.WindowText;
            link.ActiveLinkColor = SystemColors.WindowText;
            link.VisitedLinkColor = SystemColors.WindowText;
            link.DisabledLinkColor = SystemColors.GrayText;
        }
        foreach (Control child in root.Controls) ApplySystemPalette(child);
        if (root == this) { StylePrimary(); Invalidate(true); }
    }
    void StylePrimary() {
        // The action the user is expected to take next is semibold; a repair reinstall is not.
        string family = install.Text.StartsWith("Re") ? "Segoe UI" : "Segoe UI Semibold";
        if (install.Font.Size != status.Font.Size || install.Font.Name != family) install.Font = new Font(family, status.Font.Size);
        // Native themed rendering supplies hover, pressed, disabled and contrast colors.
        // Never override the user's palette with a fixed brand-blue fill.
        install.FlatStyle = FlatStyle.Standard;
        install.UseVisualStyleBackColor = true;
        install.ResetBackColor(); install.ResetForeColor();
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
