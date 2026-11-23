using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

internal sealed partial class BuilderWindow
{
    private static readonly Color Edge = Color.FromArgb(110, 69, 29);
    private static readonly Color Gold = Color.FromArgb(255, 191, 43);
    private static readonly Color Ready = Color.FromArgb(101, 224, 139);
    private const TextFormatFlags PaintText = TextFormatFlags.PreserveGraphicsClipping |
        TextFormatFlags.PreserveGraphicsTranslateTransform;
    private static readonly Bitmap Skyline = LoadArtwork("CityBackdrop");
    private static readonly Bitmap Logo = LoadArtwork("ReSqueezedLogo");
    private static readonly Rectangle LogoBounds = VisiblePixels(Logo);
    private readonly Panel homePage = new Panel();
    private readonly Panel buildPage = new Panel();
    private readonly Panel settingsPage = new Panel();
    private readonly Panel aboutPage = new Panel();
    private readonly Panel pageHost = new Panel { Dock = DockStyle.Fill };
    private readonly Dictionary<Panel, Button> navigation = new Dictionary<Panel, Button>();
    private readonly Label footer = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly DashboardButton showFolder = new DashboardButton { Text = "Open game folder" };
    private Panel activePage;

    private static Bitmap LoadArtwork(string resource)
    {
        using (Stream stream = typeof(BuilderWindow).Assembly.GetManifestResourceStream(resource))
        using (var source = new Bitmap(stream))
            return new Bitmap(source);
    }

    private static bool IsLogoPixel(Color color)
    {
        return color.A > 30 && Math.Max(color.R, Math.Max(color.G, color.B)) > 55;
    }

    private static Rectangle VisiblePixels(Bitmap bitmap)
    {
        int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (IsLogoPixel(bitmap.GetPixel(x, y)))
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x + 1);
                    bottom = Math.Max(bottom, y + 1);
                }
        Rectangle bounds = Rectangle.FromLTRB(left, top, right, bottom);
        bounds.Inflate(2, 2);
        return Rectangle.Intersect(bounds, new Rectangle(Point.Empty, bitmap.Size));
    }

    private Control CreateHeader()
    {
        var header = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0),
            BackColor = Background
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 137));
        var brand = new ArtPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        brand.Paint += (sender, e) => {
            DrawLogo(e.Graphics, new Rectangle(5, 7, 104, 53));
            TextAt(e.Graphics, "Launcher", 119, 16, 11, FontStyle.Bold, Light, 145);
            TextAt(e.Graphics, "v0.1a", 120, 37, 9, FontStyle.Regular, Gold, 75);
        };
        header.Controls.Add(brand, 0, 0);
        brand.MouseDown += DragWindow;
        header.MouseDown += DragWindow;
        var links = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 13, 0, 0)
        };
        header.Controls.Add(links, 1, 0);
        links.MouseDown += DragWindow;
        AddNavigation(links, "Play", homePage);
        AddNavigation(links, "Build", buildPage);
        AddNavigation(links, "Mods", modsTab);
        AddNavigation(links, "Saves", savesTab);
        AddNavigation(links, "Settings", settingsPage);
        AddNavigation(links, "About", aboutPage);
        header.Controls.Add(CreateWindowControls(), 2, 0);
        return header;
    }

    private void AddNavigation(FlowLayoutPanel links, string text, Panel page)
    {
        var button = new Button {
            Text = text, Size = new Size(text == "Settings" ? 90 : 72, 36),
            Margin = new Padding(0, 0, 6, 0), FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand
        };
        button.FlatAppearance.MouseOverBackColor = Surface;
        button.FlatAppearance.MouseDownBackColor = Surface;
        button.Click += (sender, e) => ShowPage(page);
        links.Controls.Add(button);
        navigation.Add(page, button);
        page.Dock = DockStyle.Fill;
        page.BackColor = Background;
        if (page != homePage)
            page.Paint += (sender, e) => {
                if (page.Width < 2 || page.Height < 2) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var outline = new Pen(Edge))
                using (var shape = Rounded(new Rectangle(0, 0, page.Width - 1, page.Height - 1), 12))
                    e.Graphics.DrawPath(outline, shape);
            };
        page.Visible = false;
        pageHost.Controls.Add(page);
    }

    private void ShowPage(Panel page)
    {
        pageHost.SuspendLayout();
        if (activePage != null) activePage.Visible = false;
        activePage = page;
        page.Visible = true;
        page.BringToFront();
        pageHost.ResumeLayout();
        foreach (var link in navigation)
        {
            bool selected = link.Key == page;
            link.Value.BackColor = selected ? Color.FromArgb(65, 43, 16) : Background;
            link.Value.ForeColor = selected ? Gold : Light;
            link.Value.FlatAppearance.BorderColor = Gold;
            link.Value.FlatAppearance.BorderSize = selected ? 1 : 0;
        }
        if (page == savesTab) RefreshSaves();
        if (page == modsTab) RefreshMods();
        if (page == homePage) homePage.Invalidate(true);
    }

    private bool TryInstallation(out string directory)
    {
        directory = null;
        if (String.IsNullOrWhiteSpace(destination.Text)) return false;
        try { directory = Path.GetFullPath(destination.Text.Trim()); }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (PathTooLongException) { return false; }
        return true;
    }

    private string ExistingBuildLog()
    {
        string folder;
        return TryInstallation(out folder) ? Builder.InstalledBuildLog(folder) : null;
    }

    private void ShowLog()
    {
        string path = File.Exists(log) ? log : ExistingBuildLog();
        if (File.Exists(path)) Process.Start("notepad.exe", "\"" + path + "\"");
    }

    private void CreateDashboard()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        homePage.Controls.Add(layout);
        var hero = new ArtPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        layout.Controls.Add(hero, 0, 0);
        hero.Paint += (sender, e) => PaintHero(hero, e.Graphics);

        play.Text = "▶   PLAY";
        play.Size = new Size(152, 46);
        play.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        play.Primary = true;
        play.AccessibleName = "Play ReSqueezed";
        var toBuild = new DashboardButton { Text = "Build", Size = new Size(98, 42) };
        toBuild.Click += (sender, e) => ShowPage(buildPage);
        showFolder.Size = new Size(155, 42);
        showFolder.Click += (sender, e) => {
            string folder;
            try
            {
                if (TryInstallation(out folder) && Directory.Exists(folder))
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception ex) { footer.Text = ex.Message; }
        };
        foreach (var action in new[] { play, toBuild, showFolder })
            hero.Controls.Add(action);
        hero.Resize += (sender, e) => {
            int y = hero.ClientSize.Height - 61;
            play.Location = new Point(28, y);
            toBuild.Location = new Point(192, y + 2);
            showFolder.Location = new Point(302, y + 2);
        };

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        layout.Controls.Add(cards, 0, 1);
        AddCard(cards, 0, "Build status", PaintBuildStatus, "Build log", ShowLog);
        AddCard(cards, 1, "Game files", PaintGameFiles, "Build setup", () => ShowPage(buildPage));
        AddCard(cards, 2, "Mods", PaintModStatus, "Manage mods  →", () => ShowPage(modsTab));
    }

    private void AddCard(TableLayoutPanel table, int column, string title,
        Action<Graphics, int> paintDetails, string action, Action click)
    {
        var card = new ArtPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, column == 2 ? 0 : 12, 0) };
        var button = new DashboardButton { Text = action, Size = new Size(133, 32) };
        if (column == 0) dashboardLog = button;
        button.Click += (sender, e) => click();
        card.Controls.Add(button);
        card.Resize += (sender, e) => button.Location = new Point(17, card.ClientSize.Height - 43);
        card.Paint += (sender, e) => {
            Rectangle box = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
            if (box.Width <= 0 || box.Height <= 0) return;
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var shape = Rounded(box, 12))
            using (var fill = new LinearGradientBrush(box, Color.FromArgb(46, 34, 23), Surface, 82f))
            using (var outline = new Pen(Edge))
            {
                graphics.FillPath(fill, shape);
                graphics.DrawPath(outline, shape);
            }
            DrawCardIcon(graphics, column);
            TextAt(graphics, title, 47, 13, 13, FontStyle.Bold, Light, box.Width - 59);
            Rule(graphics, 48, box.Width);
            paintDetails(graphics, box.Width);
        };
        table.Controls.Add(card, column, 0);
    }

    private DashboardButton dashboardLog;

    private void PaintHero(ArtPanel hero, Graphics g)
    {
        Rectangle box = new Rectangle(0, 0, hero.Width - 1, hero.Height - 1);
        if (box.Width <= 0 || box.Height <= 0) return;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var shape = Rounded(box, 14))
        {
            GraphicsState state = g.Save();
            g.SetClip(shape);
            float scale = Math.Max((float)box.Width / Skyline.Width, (float)box.Height / Skyline.Height);
            float width = Skyline.Width * scale, height = Skyline.Height * scale;
            g.DrawImage(Skyline, new RectangleF(box.Width - width, (box.Height - height) * 0.46f, width, height));
            var tint = new Rectangle(0, 0, box.Width * 64 / 100, box.Height);
            using (var shade = new LinearGradientBrush(tint, Color.FromArgb(124, 12, 9, 7), Color.Transparent, 0f))
                g.FillRectangle(shade, tint);
            using (var bottom = new LinearGradientBrush(new Rectangle(0, box.Height - 70, box.Width, 70),
                Color.Transparent, Color.FromArgb(130, 13, 9, 5), 90f))
                g.FillRectangle(bottom, 0, box.Height - 70, box.Width, 70);
            g.Restore(state);
            using (var outline = new Pen(Color.FromArgb(159, 93, 25))) g.DrawPath(outline, shape);
        }
        bool installed = Builder.Installed(destination.Text);
        string title = installed ? "Ready to Play" : building ? "Building ReSqueezed" : "Build ReSqueezed";
        string subtitle = installed ? "Launch ReSqueezed or manage your career and mods." :
            "Choose your Xbox ISO or extracted disc to get started.";
        TextAt(g, title, 25, 43, 29, FontStyle.Bold, Light, box.Width * 60 / 100);
        TextAt(g, subtitle, 29, 100, 11, FontStyle.Regular, Light, box.Width * 62 / 100);
        int pillY = Math.Max(129, box.Height - 105);
        DrawPill(g, installed ? "●  Build ready" : "●  Build required", 28, pillY, 131, installed ? Ready : Gold);
        bool fast = Builder.FasterStartup(destination.Text);
        DrawPill(g, fast ? "1 mod active" : "0 mods active", 168, pillY, 116, Gold);
        int logoSize = Math.Min(320, box.Width * 32 / 100);
        DrawLogo(g, new Rectangle(box.Width - logoSize - 47, 16, logoSize, Math.Min(136, box.Height - 78)));
    }

    private void PaintBuildStatus(Graphics g, int width)
    {
        bool installed = Builder.Installed(destination.Text);
        string folder;
        string exe = TryInstallation(out folder) ? Builder.InstalledExecutable(folder) : null;
        TextAt(g, installed ? "●  Ready to launch" : "●  Build required", 18, 58, 10, FontStyle.Bold,
            installed ? Ready : Gold, width - 30);
        CardRow(g, width, 84, "Platform", "Windows x64");
        CardRow(g, width, 109, "Last built",
            File.Exists(exe) ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm") : "—");
        CardRow(g, width, 134, "Renderer", "Direct3D 11");
    }

    private void PaintGameFiles(Graphics g, int width)
    {
        string folder;
        bool selected = TryInstallation(out folder);
        CardRow(g, width, 58, "Xbox disc data",
            selected && File.Exists(Path.Combine(folder, "game", "default.xbe")) ? "Present" : "Pending");
        CardRow(g, width, 84, "Windows executable",
            selected && File.Exists(Builder.InstalledExecutable(folder)) ? "Present" : "Pending");
        CardRow(g, width, 109, "Build sources", File.Exists(Path.Combine(Builder.Package,
            "source", "ReSqueezed", "CMakeLists.txt")) ? "Bundled" : "—");
    }

    private void PaintModStatus(Graphics g, int width)
    {
        bool fast = Builder.FasterStartup(destination.Text);
        CardRow(g, width, 58, "Faster startup", fast ? "Enabled" : "Off");
        TextAt(g, "Skip the opening movies", 17, 86, 10, FontStyle.Regular, Muted, width - 24);
    }

    private static void CardRow(Graphics g, int width, int y, string label, string value)
    {
        int column = width * 55 / 100;
        TextAt(g, label, 17, y, 9, FontStyle.Regular, Muted, column - 19);
        using (var font = new Font("Segoe UI", 9))
            TextRenderer.DrawText(g, value, font, new Rectangle(column, y, width - column - 17, 20),
                value == "Present" || value == "Enabled" ? Ready : Light,
                PaintText | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        Rule(g, y + 21, width);
    }

    private static void Rule(Graphics g, int y, int width)
    {
        using (var pen = new Pen(Color.FromArgb(77, 76, 53, 32))) g.DrawLine(pen, 17, y, width - 17, y);
    }

    private static void DrawPill(Graphics g, string text, int x, int y, int width, Color color)
    {
        using (var shape = Rounded(new Rectangle(x, y, width, 28), 13))
        using (var fill = new SolidBrush(Color.FromArgb(225, 41, 29, 14)))
        using (var border = new Pen(Color.FromArgb(153, 97, 31)))
        {
            g.FillPath(fill, shape);
            g.DrawPath(border, shape);
        }
        TextAt(g, text, x + 9, y + 5, 9, FontStyle.Bold, color, width - 16);
    }

    private static void DrawCardIcon(Graphics g, int icon)
    {
        using (var pen = new Pen(Gold, 1.6f))
        {
            if (icon == 1)
                g.DrawPolygon(pen, new[] { new Point(17, 19), new Point(25, 19), new Point(28, 22),
                    new Point(37, 22), new Point(37, 34), new Point(17, 34) });
            else
            {
                g.DrawPolygon(pen, new[] { new Point(16, 23), new Point(27, 18), new Point(38, 23), new Point(27, 29) });
                g.DrawLines(pen, new[] { new Point(16, 28), new Point(27, 34), new Point(38, 28) });
            }
        }
    }

    private static void DrawLogo(Graphics g, Rectangle frame)
    {
        float scale = Math.Min((float)frame.Width / LogoBounds.Width, (float)frame.Height / LogoBounds.Height);
        var target = new RectangleF(frame.X + (frame.Width - LogoBounds.Width * scale) / 2,
            frame.Y + (frame.Height - LogoBounds.Height * scale) / 2, LogoBounds.Width * scale, LogoBounds.Height * scale);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var attributes = new ImageAttributes())
        {
            attributes.SetColorKey(Color.Black, Color.FromArgb(14, 14, 14));
            g.DrawImage(Logo, Rectangle.Round(target), LogoBounds.X, LogoBounds.Y,
                LogoBounds.Width, LogoBounds.Height, GraphicsUnit.Pixel, attributes);
        }
    }

    private static void TextAt(Graphics g, string value, int x, int y, int size, FontStyle style, Color color, int width)
    {
        using (var font = new Font("Segoe UI", size, style))
            TextRenderer.DrawText(g, value, font, new Rectangle(x, y, Math.Max(0, width), font.Height + 5), color,
                PaintText | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath Rounded(Rectangle box, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        path.AddArc(box.Left, box.Top, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class ArtPanel : Panel
    {
        internal ArtPanel() { DoubleBuffered = true; ResizeRedraw = true; }
    }

    private sealed class DashboardButton : Button
    {
        internal bool Primary;
        private bool hovered;

        internal DashboardButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle box = new Rectangle(0, 0, Width - 1, Height - 1);
            if (box.Width <= 0 || box.Height <= 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool gold = Primary && Enabled;
            Color top = gold ? Color.FromArgb(255, 218, 91) : Color.FromArgb(55, 40, 25);
            Color bottom = gold ? Color.FromArgb(245, 164, 5) : Color.FromArgb(30, 23, 17);
            using (var path = Rounded(box, 8))
            using (var brush = new LinearGradientBrush(box, top, bottom, 90f))
            using (var edge = new Pen(gold || hovered ? Gold : Edge))
            {
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(edge, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, box,
                !Enabled ? Color.FromArgb(168, 150, 126) : gold ? Background : Light,
                PaintText | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(box, -4, -4));
        }
    }

    private sealed class DashboardToggle : CheckBox
    {
        internal DashboardToggle()
        {
            Size = new Size(480, 48);
            Margin = new Padding(2, 1, 2, 8);
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Background);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var box = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var shape = Rounded(box, 9))
            using (var fill = new SolidBrush(Surface))
            using (var border = new Pen(Edge))
            {
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(border, shape);
            }
            var switchBox = new Rectangle(Width - 63, (Height - 23) / 2, 45, 23);
            using (var track = Rounded(switchBox, 11))
            using (var fill = new SolidBrush(Checked && Enabled ? Color.FromArgb(129, 100, 31) : Color.FromArgb(72, 58, 46)))
            using (var knob = new SolidBrush(Checked && Enabled ? Gold : Muted))
            {
                e.Graphics.FillPath(fill, track);
                e.Graphics.FillEllipse(knob, Checked ? switchBox.Right - 20 : switchBox.Left + 3,
                    switchBox.Top + 3, 17, 17);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(16, 0, Width - 90, Height),
                Enabled ? Light : Muted, PaintText | TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(box, -4, -4));
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    }
}
