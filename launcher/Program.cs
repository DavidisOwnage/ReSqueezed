using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--prepare" || args[0] == "--play"))
            return RecompLauncherIntegration.Run(args);

        bool update = args.Length == 2 && args[0] == "--update";
        if (update || (args.Length == 3 && args[0] == "--build"))
        {
            string log = Builder.NewLog();
            try
            {
                if (update) Builder.Update(args[1], log, Console.WriteLine);
                else Builder.Build(args[1], args[2], log, Console.WriteLine);
                return 0;
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, ex + Environment.NewLine);
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Build log " + log);
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--run-game")
        {
            try { return Builder.RunGame(args[1], args[2] == "wide"); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "ReSqueezed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (Path.GetFileName(Builder.ApplicationPath()).Equals("Play ReSqueezed.exe", StringComparison.OrdinalIgnoreCase))
        {
            try { Builder.Play(Builder.Package, true); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "ReSqueezed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            return 0;
        }
        Application.Run(new BuilderWindow(args.Length == 1 ? args[0] : ""));
        return 0;
    }
}

internal sealed partial class BuilderWindow : Form
{
    private static readonly Color Background = Color.FromArgb(18, 14, 11);
    private static readonly Color Surface = Color.FromArgb(32, 24, 18);
    private static readonly Color Light = Color.FromArgb(255, 246, 229);
    private static readonly Color Muted = Color.FromArgb(203, 180, 150);
    private readonly TextBox input = new TextBox();
    private readonly TextBox destination = new TextBox();
    private readonly CheckBox widescreen = new DashboardToggle {
        Text = "Widescreen (16:9)", Checked = true
    };
    private readonly Label status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly Button build = new Button { Text = "Build", AutoSize = true };
    private readonly DashboardButton play = new DashboardButton();
    private readonly Button openLog = new Button { Text = "Build log", AutoSize = true, Enabled = false };
    private readonly TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Fill };
    private readonly Panel savesTab = new Panel();
    private readonly Panel modsTab = new Panel();
    private readonly ListBox drivers = new ListBox { Dock = DockStyle.Fill };
    private readonly ListBox snapshots = new ListBox { Dock = DockStyle.Fill };
    private readonly Button backUp = new Button { Text = "Back up", AutoSize = true };
    private readonly Button export = new Button { Text = "Export save…", AutoSize = true };
    private readonly Button restore = new Button { Text = "Restore backup…", AutoSize = true };
    private readonly Button import = new Button { Text = "Import ZIP…", AutoSize = true };
    private readonly Label saveStatus = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly CheckBox fasterStartup = new DashboardToggle { Text = "Faster startup" };
    private readonly Label modStatus = new Label { AutoSize = true };
    private bool updatingSaves;
    private bool updatingMods;
    private bool building;
    private string log;
    private string deferredUpdate;

    internal BuilderWindow(string initialInput)
    {
        Text = "ReSqueezed Launcher";
        using (var stream = typeof(BuilderWindow).Assembly.GetManifestResourceStream("GameIcon"))
            Icon = new Icon(stream);
        Font = new Font("Segoe UI", 10);
        BackColor = Background;
        ForeColor = Light;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1120, 695);
        MinimumSize = new Size(940, 620);
        AllowDrop = true;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14, 4, 14, 7), RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        Controls.Add(layout);
        layout.Controls.Add(CreateHeader());
        layout.Controls.Add(pageHost);
        footer.TextAlign = ContentAlignment.MiddleLeft;
        footer.Padding = new Padding(10, 0, 6, 0);
        footer.Paint += (sender, e) => {
            using (var line = new Pen(Edge)) e.Graphics.DrawLine(line, 0, 0, footer.Width, 0);
        };
        layout.Controls.Add(footer);
        var playLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(21), ColumnCount = 1, RowCount = 5
        };
        playLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        playLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        playLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        playLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        playLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buildPage.Controls.Add(playLayout);
        playLayout.Controls.Add(PageTitle("Build ReSqueezed"));
        playLayout.Controls.Add(new Label {
            Text = "Choose a Juiced USA / Europe retail ISO or extracted folder. First build downloads verified tools (244 MiB).", AutoSize = true,
            ForeColor = Muted, Margin = new Padding(0, 12, 0, 12)
        });
        playLayout.Controls.Add(fields);
        fields.ColumnCount = 1;
        fields.RowCount = 2;
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddField("Disc input", input, "ISO…", ChooseIso, "Folder…", () => ChooseFolder(input));
        AddField("Install folder", destination, "Browse…", () => ChooseFolder(destination));
        status.ForeColor = Muted;
        status.TextAlign = ContentAlignment.MiddleLeft;
        playLayout.Controls.Add(status);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        actions.Controls.Add(build);
        actions.Controls.Add(openLog);
        playLayout.Controls.Add(actions);
        StyleButton(build, true);
        StyleButton(openLog, false);
        CreateDashboard();
        CreateSavesPage();
        CreateModsPage();
        CreateSettingsPage();
        CreateAboutPage();

        input.Text = initialInput;
        destination.Text = Path.GetDirectoryName(Builder.ApplicationPath());
        build.Click += async (sender, e) => await BuildAsync();
        play.Click += async (sender, e) => {
            if (await OfferUpdateAsync()) return;
            try { Builder.Play(destination.Text, widescreen.Checked); }
            catch (Exception ex) { footer.Text = ex.Message; }
        };
        Shown += async (sender, e) => await OfferUpdateAsync();
        status.TextChanged += (sender, e) => { if (building) footer.Text = status.Text; };
        openLog.Click += (sender, e) => ShowLog();
        input.TextChanged += (sender, e) => RefreshButtons();
        destination.TextChanged += (sender, e) => RefreshButtons();
        Activated += (sender, e) => {
            if (activePage == savesTab) RefreshSaves();
            if (activePage == homePage) RefreshButtons();
        };
        DragEnter += (sender, e) => {
            if (!building && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        };
        DragDrop += (sender, e) => {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (!building && files != null && files.Length == 1)
            {
                input.Text = files[0];
                ShowPage(buildPage);
            }
        };
        FormClosing += (sender, e) => {
            if (building) {
                e.Cancel = true;
                status.Text = "The build is still running.";
            }
        };
        status.Text = Builder.Installed(destination.Text) ? "ReSqueezed is ready to play." : "Select your disc to build ReSqueezed.";
        ShowPage(String.IsNullOrEmpty(initialInput) ? homePage : buildPage);
        RefreshButtons();
    }

    private static Label PageTitle(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold),
            ForeColor = Light, Margin = new Padding(2, 0, 0, 10) };
    }

    private FlowLayoutPanel PageBody(Panel page, string title)
    {
        var body = new FlowLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(21), FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        page.Controls.Add(body);
        body.Controls.Add(PageTitle(title));
        return body;
    }

    private void CreateSettingsPage()
    {
        var body = PageBody(settingsPage, "Display");
        body.Controls.Add(widescreen);
        body.Controls.Add(new Label { Text = "Clear the checkbox to launch in 4:3.", AutoSize = true, ForeColor = Muted });
    }

    private void CreateAboutPage()
    {
        var body = PageBody(aboutPage, "ReSqueezed v" + Builder.Release);
        body.Controls.Add(new Label {
            Text = "A Windows x64 recompilation of the original Xbox release of Juiced.",
            AutoSize = true, ForeColor = Muted
        });
        string readme = Path.Combine(Builder.Package, "README.md");
        if (File.Exists(readme))
        {
            var guide = new Button { Text = "Read the setup guide", AutoSize = true };
            StyleButton(guide, false);
            guide.Click += (sender, e) => System.Diagnostics.Process.Start("notepad.exe", "\"" + readme + "\"");
            body.Controls.Add(guide);
        }
    }

    private CareerSaves.Profile SelectedDriver { get { return drivers.SelectedItem as CareerSaves.Profile; } }
    private CareerSaves.Backup SelectedBackup { get { return snapshots.SelectedItem as CareerSaves.Backup; } }

    private void CreateSavesPage()
    {
        var body = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(21), ColumnCount = 2, RowCount = 5
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        savesTab.Controls.Add(body);
        var title = PageTitle("Career saves");
        body.Controls.Add(title, 0, 0);
        body.SetColumnSpan(title, 2);
        body.Controls.Add(new Label { Text = "Career drivers", AutoSize = true }, 0, 1);
        body.Controls.Add(new Label { Text = "Backups", AutoSize = true }, 1, 1);
        foreach (var list in new[] { drivers, snapshots })
        {
            list.BackColor = Surface;
            list.ForeColor = Light;
            list.IntegralHeight = false;
        }
        drivers.AccessibleName = "Career drivers";
        snapshots.AccessibleName = "Backups for the selected driver";
        body.Controls.Add(drivers, 0, 2);
        body.Controls.Add(snapshots, 1, 2);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        foreach (var button in new[] { backUp, export, restore, import })
        {
            StyleButton(button, false);
            actions.Controls.Add(button);
        }
        body.Controls.Add(actions, 0, 3);
        body.SetColumnSpan(actions, 2);
        saveStatus.ForeColor = Muted;
        saveStatus.TextAlign = ContentAlignment.MiddleLeft;
        body.Controls.Add(saveStatus, 0, 4);
        body.SetColumnSpan(saveStatus, 2);
        drivers.SelectedIndexChanged += (sender, e) => { if (!updatingSaves) RefreshSnapshots(null); };
        snapshots.SelectedIndexChanged += (sender, e) => SetSaveButton(restore, !building && SelectedBackup != null);
        backUp.Click += (sender, e) => SaveAction(store => store.BackupNow(SelectedDriver.Id), "Backup created.");
        export.Click += (sender, e) => {
            string id = SelectedDriver.Id;
            using (var dialog = new SaveFileDialog {
                Filter = "Career ZIP (*.zip)|*.zip", DefaultExt = "zip", FileName = "ReSqueezed-" + id + ".zip"
            })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SaveAction(store => store.Export(id, dialog.FileName), "Career save exported.");
        };
        restore.Click += (sender, e) => {
            string id = SelectedDriver.Id;
            string archive = SelectedBackup.Path;
            if (ConfirmRestore("Restore this driver's backup?"))
                SaveAction(store => store.Restore(archive, id), "Career backup restored.");
        };
        import.Click += (sender, e) => {
            using (var dialog = new OpenFileDialog { Filter = "Career ZIP (*.zip)|*.zip" })
                if (dialog.ShowDialog(this) == DialogResult.OK && ConfirmRestore("Import this driver from the ZIP?"))
                    SaveAction(store => store.Restore(dialog.FileName, null), "Career save imported.");
        };
    }

    private bool ConfirmRestore(string question)
    {
        return MessageBox.Show(this, question + "\n\nIf that driver already exists, their current save will be backed up first.",
            "ReSqueezed Career Saves", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
    }

    private void SaveAction(Action<CareerSaves> action, string message)
    {
        try
        {
            action(new CareerSaves(destination.Text));
            RefreshSaves();
            saveStatus.Text = message;
        }
        catch (Exception ex) { saveStatus.Text = ex.Message; }
    }

    private void RefreshSaves()
    {
        string id = SelectedDriver == null ? null : SelectedDriver.Id;
        string backup = SelectedBackup == null ? null : SelectedBackup.Path;
        updatingSaves = true;
        try
        {
            drivers.Items.Clear();
            if (Builder.Installed(destination.Text))
                drivers.Items.AddRange(new CareerSaves(destination.Text).ListProfiles());
            var selected = drivers.Items.Cast<CareerSaves.Profile>()
                .FirstOrDefault(profile => profile.Id == id);
            if (selected != null) drivers.SelectedItem = selected;
            else if (drivers.Items.Count != 0) drivers.SelectedIndex = 0;
            RefreshSnapshots(backup);
        }
        catch (Exception ex) { saveStatus.Text = ex.Message; }
        finally { updatingSaves = false; }
    }

    private void RefreshSnapshots(string selectedPath)
    {
        snapshots.Items.Clear();
        bool installed = !building && Builder.Installed(destination.Text);
        bool current = false;
        try
        {
            if (installed && SelectedDriver != null)
            {
                var store = new CareerSaves(destination.Text);
                current = store.HasCareer(SelectedDriver.Id);
                snapshots.Items.AddRange(store.ListBackups(SelectedDriver.Id));
                snapshots.SelectedItem = snapshots.Items.Cast<CareerSaves.Backup>()
                    .FirstOrDefault(backup => backup.Path == selectedPath);
            }
            saveStatus.Text = !installed ? "Choose a completed ReSqueezed installation on the Build tab." :
                drivers.Items.Count == 0 ? "No career saves yet. Create a driver in the game or import a ZIP." :
                "Automatic backups are kept for each driver. Close ReSqueezed to manage saves.";
        }
        catch (Exception ex) { saveStatus.Text = ex.Message; }
        SetSaveButton(backUp, current);
        SetSaveButton(export, current);
        SetSaveButton(import, installed);
        SetSaveButton(restore, installed && SelectedBackup != null);
    }

    private static void SetSaveButton(Button button, bool enabled)
    {
        button.Enabled = enabled;
        button.BackColor = enabled ? Surface : Color.FromArgb(105, 88, 67);
    }

    private void CreateModsPage()
    {
        var body = PageBody(modsTab, "Mods");
        body.Controls.Add(fasterStartup);
        body.Controls.Add(new Label {
            Text = "Skip the opening logos and intro movie. Career movies and race previews stay on.",
            AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = Muted
        });
        modStatus.ForeColor = Muted;
        body.Controls.Add(modStatus);
        fasterStartup.CheckedChanged += (sender, e) => {
            if (updatingMods) return;
            try
            {
                Builder.SetFasterStartup(destination.Text, fasterStartup.Checked);
                modStatus.Text = "Applies the next time you launch ReSqueezed.";
            }
            catch (Exception ex) { RefreshMods(); modStatus.Text = ex.Message; }
        };
    }

    private void RefreshMods()
    {
        updatingMods = true;
        fasterStartup.Enabled = !building && Builder.Installed(destination.Text);
        fasterStartup.Checked = Builder.FasterStartup(destination.Text);
        modStatus.Text = fasterStartup.Enabled ? "" : "Choose a completed ReSqueezed installation on the Build tab.";
        updatingMods = false;
    }

    private static void StyleButton(Button button, bool primary)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Edge;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 215, 98) : Color.FromArgb(61, 43, 27);
        button.BackColor = primary ? Gold : Surface;
        button.ForeColor = primary ? Background : Light;
        button.Padding = new Padding(8, 2, 8, 2);
    }

    private void AddField(string label, TextBox box, string caption, Action choose,
        string secondCaption = null, Action secondChoose = null)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4,
            Margin = new Padding(0, 0, 0, 6) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
        box.Dock = DockStyle.Fill;
        box.BackColor = Surface;
        box.ForeColor = Light;
        row.Controls.Add(box);
        var browse = new Button { Text = caption, AutoSize = true };
        StyleButton(browse, false);
        browse.Click += (sender, e) => choose();
        row.Controls.Add(browse);
        if (secondChoose != null)
        {
            var alternate = new Button { Text = secondCaption, AutoSize = true };
            StyleButton(alternate, false);
            alternate.Click += (sender, e) => secondChoose();
            row.Controls.Add(alternate);
        }
        fields.Controls.Add(row);
    }

    private void ChooseIso()
    {
        using (var dialog = new OpenFileDialog { Filter = "Xbox disc images (*.iso)|*.iso" })
            if (dialog.ShowDialog(this) == DialogResult.OK) input.Text = dialog.FileName;
    }

    private void ChooseFolder(TextBox target)
    {
        using (var dialog = new FolderBrowserDialog { ShowNewFolderButton = target == destination })
            if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void RefreshButtons()
    {
        bool installed = Builder.Installed(destination.Text);
        bool update = installed && Builder.UpdateAvailable(destination.Text);
        build.Text = update ? "Update" : "Build";
        build.Enabled = !building && (update || (!installed && !String.IsNullOrWhiteSpace(input.Text)));
        play.Enabled = !building && installed;
        if (!building && log == null)
            status.Text = update ? "Update to v" + Builder.Release + " using the installed disc files." :
                installed ? "This installation is ready to play." : "Select your disc to build ReSqueezed.";
        fields.Enabled = !building;
        if (activePage == savesTab) RefreshSaves();
        if (activePage == modsTab) RefreshMods();
        openLog.Enabled = File.Exists(log) || File.Exists(ExistingBuildLog());
        dashboardLog.Enabled = openLog.Enabled;
        string directory;
        showFolder.Enabled = TryInstallation(out directory) && Directory.Exists(directory);
        Color inactive = Color.FromArgb(105, 88, 67);
        build.BackColor = build.Enabled ? Gold : inactive;
        openLog.BackColor = openLog.Enabled ? Surface : inactive;
        footer.ForeColor = play.Enabled ? Ready : Gold;
        footer.Text = building ? status.Text : update ? "●   Update available" : play.Enabled ? "●   All set   ·   Game ready to launch" :
            "●   Choose your disc on the Build page";
        homePage.Invalidate(true);
    }

    private async Task<bool> OfferUpdateAsync()
    {
        if (building || !Builder.UpdateAvailable(destination.Text)) return false;
        string installation = Path.GetFullPath(destination.Text);
        if (String.Equals(installation, deferredUpdate, StringComparison.OrdinalIgnoreCase)) return false;
        var answer = MessageBox.Show(this,
            "Rebuild the game locally to update to v" + Builder.Release +
            "? This uses your installed disc files and keeps your career saves, backups and mods. Choose No to update later.",
            "ReSqueezed update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
        {
            deferredUpdate = installation;
            return false;
        }
        ShowPage(buildPage);
        await BuildAsync();
        return true;
    }

    private async Task BuildAsync()
    {
        string disc = input.Text.Trim();
        string install = destination.Text.Trim();
        bool update = Builder.UpdateAvailable(install);
        building = true;
        RefreshButtons();
        status.Text = "Checking the input and build tools…";
        try
        {
            log = Builder.NewLog();
            File.WriteAllText(log, "");
            RefreshButtons();
            var messages = new Progress<string>(message => status.Text = message);
            Action<string> report = message => ((IProgress<string>)messages).Report(message);
            await Task.Run(() => {
                if (update) Builder.Update(install, log, report);
                else Builder.Build(disc, install, log, report);
            });
        }
        catch (Exception ex)
        {
            if (File.Exists(log)) File.AppendAllText(log, ex + Environment.NewLine);
            status.Text = ex.Message;
        }
        finally
        {
            building = false;
            RefreshButtons();
            if (Builder.Installed(install) && !Builder.UpdateAvailable(install)) ShowPage(homePage);
        }
    }
}
