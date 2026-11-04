using System;
using Keystream.Core.Localization;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Keystream.App.Host;
using Keystream.Core.Input;

namespace Keystream.App.Views;

/// <summary>
/// Native Windows ContextMenuStrip with custom dark cyberpunk styling.
/// Layout:
///   ● Deck: Terhubung         (header with LED dot, disabled)
///   ─────────────────
///   ❚❚ Disable App         ●  (toggle + status dot right-aligned)
///   🌐 Global ON              (toggle)
///   ─────────────────
///   👤 Profile              ▸  (submenu with ☑/☐)
///   📑 Mode                 ▸  (submenu with ☑/☐)
///   ─────────────────
///   ☑  Mulai tersembunyi      (checkbox setting)
///   ─────────────────
///   [→ Exit                    (red)
/// </summary>
public sealed class ModernTrayMenu : ContextMenuStrip
{
    private readonly AppHost _host;
    private readonly Action _onExitApp;

    // §152: faktor DPI terakhir yang dihitung UpdateState (sekali per
    // Opening) - dipakai untuk padding/MinimumSize. Renderer menghitung
    // faktornya sendiri per render lewat ResolveDpiFactor (sumber dan
    // urutan fallback yang sama); nilai awal 1.0 = tampilan §151.
    private double _dpiFactor = 1.0;

    /// <summary>§152: faktor DPI terakhir dari UpdateState (1.0 bila
    /// Opening belum pernah terjadi).</summary>
    internal double DpiFactor => _dpiFactor;

    /// <summary>§152: faktor skala DPI, TIDAK PERNAH throw dan tidak
    /// pernah membiarkan menu gagal render. Urutan: (1) DPI efektif
    /// monitor di bawah kursor lewat MonitorDpi (koordinat piksel
    /// perangkat, ruang yang sama dengan yang didokumentasikan
    /// MonitorDpi) / 96; (2) Graphics.DpiX / 96 bila pembacaan Win32
    /// gagal; (3) 1.0 bila keduanya gagal (= tampilan §151 persis).
    /// Tidak ada perubahan DPI-awareness di mana pun.</summary>
    internal static double ResolveDpiFactor(System.Drawing.Graphics? graphics)
    {
        try
        {
            System.Drawing.Point cursor = Cursor.Position;
            if (MonitorDpi.TryGetDpiForPixelPoint(cursor.X, cursor.Y, out uint dpiX, out _))
                return TrayDpiScale.FactorForDpi(dpiX);
        }
        catch
        {
            // Lanjut ke fallback Graphics - kegagalan di sini tidak
            // boleh menggagalkan render menu.
        }
        try
        {
            if (graphics != null)
                return TrayDpiScale.FactorForDpi(graphics.DpiX);
        }
        catch
        {
            // Jatuh ke 1.0 di bawah.
        }
        return 1.0;
    }

    /// <summary>§152: padding item submenu dari nilai DASAR × faktor
    /// saat ini, sebagai nilai ABSOLUT (bukan dari padding yang sudah
    /// diskala). Item submenu dibuat ulang tiap Populate*, jadi nilai
    /// ini selalu dihitung dari dasar - pemanggilan ulang identik.</summary>
    private Padding ScaledSubItemPadding()
    {
        var (l, t, r, b) = TrayDpiScale.ScalePadding(
            TrayDpiScale.BaseSubItemPadLeft, TrayDpiScale.BaseSubItemPadTop,
            TrayDpiScale.BaseSubItemPadRight, TrayDpiScale.BaseSubItemPadBottom, _dpiFactor);
        return new Padding(l, t, r, b);
    }

    /// <summary>§152: terapkan padding + MinimumSize submenu dari
    /// nilai DASAR × faktor saat ini, sebagai nilai ABSOLUT.</summary>
    private void ApplyDpiToSubmenu(ToolStripDropDown dropDown)
    {
        dropDown.Padding = new Padding(TrayDpiScale.Scale(TrayDpiScale.BaseDropdownPadding, _dpiFactor));
        dropDown.MinimumSize = new Size(TrayDpiScale.Scale(TrayDpiScale.BaseSubmenuMinWidth, _dpiFactor), 0);
    }

    // Items are identified by reference, not index - much safer.
    internal readonly ToolStripMenuItem ItemHeader;
    internal readonly ToolStripMenuItem ItemToggleApp;
    internal readonly ToolStripMenuItem ItemToggleGlobal;
    internal readonly ToolStripMenuItem ItemProfile;
    internal readonly ToolStripMenuItem ItemMode;
    internal readonly ToolStripMenuItem ItemStartMinimized;
    internal readonly ToolStripMenuItem ItemExit;

    public ModernTrayMenu(AppHost host, Action onExitApp)
    {
        _host = host;
        _onExitApp = onExitApp;

        ShowImageMargin = false;
        ShowCheckMargin = false;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(18, 19, 22);   // #121316
        ForeColor = Color.FromArgb(232, 234, 240); // #E8EAF0
        // §152: nilai dasar dari TrayDpiScale (faktor 1.0); UpdateState
        // di akhir ctor menghitung ulang dari dasar × faktor DPI saat ini.
        Padding = new Padding(TrayDpiScale.BaseMenuPadding);
        MinimumSize = new Size(TrayDpiScale.BaseRootMinWidth, 0);
        Renderer = new ModernDarkMenuRenderer(this);

        // 1. Header - "Deck: Terhubung" / "Deck: Terputus" with LED dot
        ItemHeader = new ToolStripMenuItem(Localizer.Get("S0352"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(126, 139, 159),
            Enabled = false,
            Padding = new Padding(TrayDpiScale.BaseHeaderPadLeft, TrayDpiScale.BaseHeaderPadTop, TrayDpiScale.BaseHeaderPadRight, TrayDpiScale.BaseHeaderPadBottom),
        };
        Items.Add(ItemHeader);
        Items.Add(new ToolStripSeparator());

        // 2. Toggle App (▷ Enable App / ❚❚ Disable App) with status dot
        ItemToggleApp = new ToolStripMenuItem(Localizer.Get("S0970"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        ItemToggleApp.Click += (_, _) => Safe(() => _host.SetPassthrough(!_host.IsPassthrough), "Toggle App");
        Items.Add(ItemToggleApp);

        // 3. Global ON / OFF
        ItemToggleGlobal = new ToolStripMenuItem(Localizer.Get("S0971"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        ItemToggleGlobal.Click += (_, _) => Safe(() => _host.SetGlobalEnabled(!_host.Config.Settings.IsGlobalEnabled), Localizer.Get("S0964"));
        Items.Add(ItemToggleGlobal);

        Items.Add(new ToolStripSeparator());

        // 4. Submenu: Profile >
        ItemProfile = new ToolStripMenuItem(Localizer.Get("S0973"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        SetupSubmenuStyling(ItemProfile.DropDown);
        ItemProfile.DropDownOpening += (_, _) => PopulateProfileSubmenu();
        Items.Add(ItemProfile);

        // 5. Submenu: Mode >
        ItemMode = new ToolStripMenuItem(Localizer.Get("S0975"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        SetupSubmenuStyling(ItemMode.DropDown);
        ItemMode.DropDownOpening += (_, _) => PopulateModeSubmenu();
        Items.Add(ItemMode);

        Items.Add(new ToolStripSeparator());

        // 6. Checkbox: Mulai tersembunyi di tray
        ItemStartMinimized = new ToolStripMenuItem(Localizer.Get("S0977"))
        {
            Tag = _host.Config.Settings.StartMinimized ? "☑" : "☐",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        ItemStartMinimized.Click += (_, _) => Safe(() =>
        {
            // §89: bila Save gagal, nilai in-memory DIKEMBALIKAN agar
            // tampilan tidak pernah berbohong (sebelumnya nilai sudah
            // berubah sebelum Save, jadi UpdateState berikutnya
            // menampilkan ☑ padahal file masih ☐ - persis laporan
            // "dicentang tapi tidak tersimpan saat restart").
            bool oldVal = _host.Config.Settings.StartMinimized;
            bool newVal = !oldVal;
            _host.Config.Settings.StartMinimized = newVal;
            try { _host.Save(); }
            catch
            {
                _host.Config.Settings.StartMinimized = oldVal;
                throw;
            }
            ItemStartMinimized.Tag = newVal ? "☑" : "☐";
            Invalidate(true);
        }, Localizer.Get("S0977"));
        Items.Add(ItemStartMinimized);

        Items.Add(new ToolStripSeparator());

        // 7. Exit
        ItemExit = new ToolStripMenuItem(Localizer.Get("S0980"))
        {
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(232, 234, 240), // #E8EAF0
            Padding = new Padding(TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop, TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom),
        };
        ItemExit.Click += (_, _) => Safe(_onExitApp, Localizer.Get("S1549"));
        Items.Add(ItemExit);

        // Subscribe to state changes
        _host.HealthChanged += UpdateState;
        _host.PassthroughChanged += _ => UpdateState();
        _host.GlobalEnabledChanged += _ => UpdateState();
        _host.ActiveProfileChangedByAutoSwitch += UpdateState;
        _host.PageSwitchedByAction += UpdateState;
        // Language toggle refreshes every tray text: UpdateState re-reads
        // all of them from Localizer (header, toggles, checkbox, submenus).
        Localizer.LanguageChanged += UpdateState;
        Opening += (_, _) => UpdateState();
        Resize += (_, _) => ApplyRoundedRegion(this, _dpiFactor);

        UpdateState();
    }

    private void SetupSubmenuStyling(ToolStripDropDown dropDown)
    {
        if (dropDown is ToolStripDropDownMenu menu)
        {
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
        }
        dropDown.BackColor = Color.FromArgb(18, 19, 22);
        dropDown.ForeColor = Color.FromArgb(232, 234, 240);
        dropDown.Renderer = Renderer;
        dropDown.Resize += (_, _) => ApplyRoundedRegion(dropDown, _dpiFactor);
        dropDown.Padding = new Padding(TrayDpiScale.BaseDropdownPadding);
        dropDown.MinimumSize = new Size(TrayDpiScale.BaseSubmenuMinWidth, 0);
    }

    private void PopulateProfileSubmenu()
    {
        ItemProfile.DropDownItems.Clear();
        string activeId = _host.Config.ActiveProfileId;

        foreach (var profile in _host.Config.Profiles)
        {
            bool isActive = profile.Id == activeId;
            var item = new ToolStripMenuItem(profile.Name)
            {
                Tag = isActive ? "☑" : "☐",
                Font = new Font("Segoe UI", 9.5f, isActive ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = isActive ? Color.FromArgb(56, 189, 248) : Color.FromArgb(232, 234, 240),
                Padding = ScaledSubItemPadding(),
            };

            string pid = profile.Id;
            item.Click += (_, _) => Safe(() =>
            {
                _host.SwitchToProfile(pid);
                UpdateState();
            }, "Pindah profil");
            ItemProfile.DropDownItems.Add(item);
        }

        if (ItemProfile.DropDownItems.Count == 0)
            ItemProfile.DropDownItems.Add(new ToolStripMenuItem(Localizer.Get("S0982")) { Enabled = false });
    }

    private void PopulateModeSubmenu()
    {
        ItemMode.DropDownItems.Clear();
        var profile = _host.Config.ActiveProfile;
        string activePageId = _host.Config.Settings.ActivePageId;

        if (profile != null && profile.Pages.Count > 0)
        {
            foreach (var page in profile.Pages)
            {
                bool isActive = page.Id == activePageId;
                var item = new ToolStripMenuItem(page.Name)
                {
                    Tag = isActive ? "☑" : "☐",
                    Font = new Font("Segoe UI", 9.5f, isActive ? FontStyle.Bold : FontStyle.Regular),
                    ForeColor = isActive ? Color.FromArgb(56, 189, 248) : Color.FromArgb(232, 234, 240),
                    Padding = ScaledSubItemPadding(),
                };

                string pageId = page.Id;
                item.Click += (_, _) => Safe(() =>
                {
                    _host.SwitchToPage(pageId);
                    UpdateState();
                }, "Pindah mode");
                ItemMode.DropDownItems.Add(item);
            }
        }
        else
        {
            ItemMode.DropDownItems.Add(new ToolStripMenuItem(Localizer.Get("S0982")) { Enabled = false });
        }
    }

    /// <summary>Pelindung klik tray: handler WinForms TIDAK ditangkap
    /// DispatcherUnhandledException WPF, jadi exception di sini dulu
    /// mematikan proses tanpa dialog (§45). Kini dicatat ke crash.log;
    /// menu tetap responsif untuk klik berikutnya.</summary>
    private static void Safe(Action action, string what)
    {
        try { action(); }
        catch (Exception ex) { CrashLog.Write($"Menu tray '{what}' gagal", ex); }
    }

    public void UpdateState()
    {
        if (IsDisposed) return;

        // §152: hitung faktor DPI sekali per pemanggilan (Opening
        // memanggil UpdateState), lalu hitung ulang SEMUA padding +
        // MinimumSize dari nilai DASAR × faktor, sebagai nilai ABSOLUT
        // (tidak pernah dari nilai yang sudah diskala - pemanggilan
        // ulang menghasilkan nilai identik, tanpa compounding).
        // Populate* di bawah memakai _dpiFactor yang baru ini.
        _dpiFactor = ResolveDpiFactor(null);
        double f = _dpiFactor;
        Padding = new Padding(TrayDpiScale.Scale(TrayDpiScale.BaseMenuPadding, f));
        MinimumSize = new Size(TrayDpiScale.Scale(TrayDpiScale.BaseRootMinWidth, f), 0);
        var (hl, ht, hr, hb) = TrayDpiScale.ScalePadding(
            TrayDpiScale.BaseHeaderPadLeft, TrayDpiScale.BaseHeaderPadTop,
            TrayDpiScale.BaseHeaderPadRight, TrayDpiScale.BaseHeaderPadBottom, f);
        ItemHeader.Padding = new Padding(hl, ht, hr, hb);
        var (il, it, ir, ib) = TrayDpiScale.ScalePadding(
            TrayDpiScale.BaseItemPadLeft, TrayDpiScale.BaseItemPadTop,
            TrayDpiScale.BaseItemPadRight, TrayDpiScale.BaseItemPadBottom, f);
        var itemPadding = new Padding(il, it, ir, ib);
        ItemToggleApp.Padding = itemPadding;
        ItemToggleGlobal.Padding = itemPadding;
        ItemProfile.Padding = itemPadding;
        ItemMode.Padding = itemPadding;
        ItemStartMinimized.Padding = itemPadding;
        ItemExit.Padding = itemPadding;
        ApplyDpiToSubmenu(ItemProfile.DropDown);
        ApplyDpiToSubmenu(ItemMode.DropDown);

        bool isPassthrough = _host.IsPassthrough;
        bool isGlobal = _host.Config.Settings.IsGlobalEnabled;
        var health = _host.Health;

        // Header: Deck status
        if (health.State == InterceptionState.Running && !health.DeckPresent)
        {
            ItemHeader.Text = Localizer.Get("S0352");
            ItemHeader.ForeColor = Color.FromArgb(232, 234, 240);
        }
        else if (health.State == InterceptionState.Running)
        {
            ItemHeader.Text = Localizer.Get("S0351");
            ItemHeader.ForeColor = Color.FromArgb(232, 234, 240);
        }
        else if (health.State == InterceptionState.Faulted)
        {
            ItemHeader.Text = Localizer.Get("S0983");
            ItemHeader.ForeColor = Color.FromArgb(232, 234, 240);
        }
        else
        {
            ItemHeader.Text = Localizer.Get("S0352");
            ItemHeader.ForeColor = Color.FromArgb(232, 234, 240);
        }

        // Toggle App
        if (isPassthrough)
        {
            ItemToggleApp.Text = Localizer.Get("S0984");
            ItemToggleApp.ForeColor = Color.FromArgb(34, 197, 94);
        }
        else
        {
            ItemToggleApp.Text = Localizer.Get("S0970");
            ItemToggleApp.ForeColor = Color.FromArgb(232, 234, 240);
        }

        // Global
        if (isGlobal)
        {
            ItemToggleGlobal.Text = Localizer.Get("S0985");
            ItemToggleGlobal.ForeColor = Color.FromArgb(56, 189, 248);
        }
        else
        {
            ItemToggleGlobal.Text = Localizer.Get("S0971");
            ItemToggleGlobal.ForeColor = Color.FromArgb(126, 139, 159);
        }

        // Checkbox: sync from config (Text re-set here, not only in the
        // ctor, so a language toggle refreshes it via UpdateState below).
        ItemProfile.Text = Localizer.Get("S0973");
        ItemMode.Text = Localizer.Get("S0975");
        ItemExit.Text = Localizer.Get("S0980");
        ItemStartMinimized.Text = Localizer.Get("S0977");
        ItemStartMinimized.Tag = _host.Config.Settings.StartMinimized ? "☑" : "☐";

        PopulateProfileSubmenu();
        PopulateModeSubmenu();
        Invalidate(true);
        ApplyRoundedRegion(this, f);
    }

    /// <summary>Sudut luar membulat seperti menu contoh: Region
    /// dropdown dipotong mengikuti path membulat, agar jendela menu
    /// benar-benar bersudut bulat dan klik di sudut yang terpotong
    /// dianggap klik di luar menu. Tidak pernah throw dan tidak
    /// pernah menggagalkan menu: kegagalan apa pun = menu tetap
    /// tampil persegi.</summary>
    internal static void ApplyRoundedRegion(ToolStripDropDown dropDown, double factor)
    {
        try
        {
            if (dropDown == null || dropDown.Width <= 0 || dropDown.Height <= 0) return;
            int radius = TrayDpiScale.Scale(TrayDpiScale.BaseOuterRadius, factor);
            if (radius * 2 >= dropDown.Width || radius * 2 >= dropDown.Height) return;
            var rect = new Rectangle(0, 0, dropDown.Width, dropDown.Height);
            using var path = ModernDarkMenuRenderer.CreateRoundedRectangle(rect, radius);
            var old = dropDown.Region;
            dropDown.Region = new Region(path);
            old?.Dispose();
        }
        catch
        {
            // Menu tetap tampil persegi - jangan pernah gagalkan tray.
        }
    }

    /// <summary>Returns the deck connection status dot color.</summary>
    public Color GetDeckDotColor()
    {
        var health = _host.Health;
        if (health.State == InterceptionState.Faulted)
            return Color.FromArgb(239, 68, 68);
        if (health.State == InterceptionState.Running)
            return health.DeckPresent
                ? Color.FromArgb(34, 197, 94)
                : Color.FromArgb(255, 198, 92);
        return Color.FromArgb(126, 139, 159);
    }

}

/// <summary>
/// Custom renderer for the dark cyberpunk tray menu.
/// Uses reference-based item identity (not fragile index) for special rendering.
/// All text and icons use DPI-scaled columns for perfect alignment
/// (§152: nilai dasar 100% tinggal di TrayDpiScale; renderer memakai
/// geometri yang sudah diskala lewat GeometryFor):
///   Column 1 (Icon/Dot):  X = ColIcon, width = ColIconW
///   Column 2 (Text):      X = ColText
/// </summary>
internal sealed class ModernDarkMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly ModernTrayMenu _menu;
    private static readonly Color BgColor = Color.FromArgb(18, 19, 22);
    private static readonly Color BorderColor = Color.FromArgb(37, 40, 51);
    private static readonly Color HoverColor = Color.FromArgb(28, 31, 40);
    private static readonly Color ExitHoverColor = Color.FromArgb(41, 22, 25);
    private static readonly Color SeparatorColor = Color.FromArgb(30, 33, 42);

    // §152: semua konstanta geometri §151 (12/22/38/8/...) kini
    // tinggal di TrayDpiScale sebagai nilai dasar; renderer membaca
    // geometri yang sudah diskala lewat GeometryFor di bawah.
    // DotRightMargin MATI (declared, tidak pernah dipakai di cabang
    // render mana pun - dibuktikan grep) dan DIBIARKAN apa adanya;
    // JANGAN di-wire-in (§152-5).
    private const int DotRightMargin = 20;

    public ModernDarkMenuRenderer(ModernTrayMenu menu) : base(new DarkColorTable())
    {
        _menu = menu;
    }

    /// <summary>§152: geometri untuk satu pemanggilan render, dari
    /// faktor DPI saat ini (MonitorDpi di bawah kursor, fallback
    /// Graphics.DpiX, fallback 1.0 - lihat
    /// ModernTrayMenu.ResolveDpiFactor). Tidak pernah throw dan tidak
    /// pernah membiarkan menu tak ter-render: kegagalan apa pun
    /// menghasilkan geometri faktor 1.0 (= tampilan §151 persis).
    /// Satu rumus untuk semua skala; tidak ada cabang 125%/150%.</summary>
    private TrayMenuGeometry GeometryFor(System.Drawing.Graphics? graphics)
    {
        try
        {
            return TrayDpiScale.ForFactor(ModernTrayMenu.ResolveDpiFactor(graphics));
        }
        catch
        {
            return TrayDpiScale.ForFactor(1.0);
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        // Sudut luar membulat seperti menu contoh: isi seluruh strip
        // dengan path membulat, bukan persegi.
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int radius = TrayDpiScale.Scale(TrayDpiScale.BaseOuterRadius,
            ModernTrayMenu.ResolveDpiFactor(e.Graphics));
        var rect = new Rectangle(0, 0, e.ToolStrip.Width, e.ToolStrip.Height);
        using var brush = new SolidBrush(BgColor);
        using var path = CreateRoundedRectangle(rect, radius);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var g = GeometryFor(e.Graphics);
        using var pen = new Pen(BorderColor, g.BorderWidth);
        float inset = g.BorderWidth / 2f;
        int radius = TrayDpiScale.Scale(TrayDpiScale.BaseOuterRadius,
            ModernTrayMenu.ResolveDpiFactor(e.Graphics));
        var r = new RectangleF(inset, inset,
            e.ToolStrip.Width - g.BorderWidth, e.ToolStrip.Height - g.BorderWidth);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedRectangleF(r, radius);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(SeparatorColor, 1);
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Color.FromArgb(126, 139, 159);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Selected && e.Item.Enabled)
        {
            var g = GeometryFor(e.Graphics);
            var r = new Rectangle(g.HoverX, g.HoverY, e.Item.Width - g.HoverWidthReduction, e.Item.Height - g.HoverHeightReduction);
            Color hover = ReferenceEquals(e.Item, _menu.ItemExit) ? ExitHoverColor : HoverColor;
            using var brush = new SolidBrush(hover);
            using var path = CreateRoundedRectangle(r, g.Radius);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var g = GeometryFor(e.Graphics);

        Color textColor = e.Item.ForeColor;

        // ── Header: LED dot (left column) + text ──
        if (ReferenceEquals(e.Item, _menu.ItemHeader))
        {
            // LED dot centered in icon column, vertically centered using TextRectangle
            int dotX = g.ColIcon + (g.ColIconW - g.DotSize) / 2;
            int dotY = e.TextRectangle.Top + (e.TextRectangle.Height - g.DotSize) / 2;
            using var dotBrush = new SolidBrush(_menu.GetDeckDotColor());
            e.Graphics.FillEllipse(dotBrush, dotX, dotY, g.DotSize, g.DotSize);

            // Text in text column
            var r = new Rectangle(g.ColText, e.TextRectangle.Top, e.Item.Width - g.ColText - g.RightInset, e.TextRectangle.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        // ── Toggle App: glyph (left) + text (no status dot) ──
        if (ReferenceEquals(e.Item, _menu.ItemToggleApp))
        {
            DrawGlyph(e);
            var r = new Rectangle(g.ColText, e.TextRectangle.Top, e.Item.Width - g.ColText - g.RightInset, e.TextRectangle.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        // ── Submenu items (Profile/Mode choices with ☑/☐) ──
        if (e.Item.Owner is ToolStripDropDownMenu dd && dd.OwnerItem != null)
        {
            string check = e.Item.Tag as string ?? "";
            if (!string.IsNullOrEmpty(check))
            {
                var cr = new Rectangle(g.ColIcon, e.TextRectangle.Top, g.ColIconW, e.TextRectangle.Height);
                using var cf = new Font("Segoe UI Symbol", 10f, FontStyle.Regular);
                TextRenderer.DrawText(e.Graphics, check, cf, cr, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            var r = new Rectangle(g.ColText, e.TextRectangle.Top, e.Item.Width - g.ColText - g.RightInset, e.TextRectangle.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        // ── Checkbox item (Mulai tersembunyi): ☑/☐ glyph + text ──
        if (ReferenceEquals(e.Item, _menu.ItemStartMinimized))
        {
            string check = e.Item.Tag as string ?? "☐";
            var cr = new Rectangle(g.ColIcon, e.TextRectangle.Top, g.ColIconW, e.TextRectangle.Height);
            using var cf = new Font("Segoe UI Symbol", 10f, FontStyle.Regular);
            TextRenderer.DrawText(e.Graphics, check, cf, cr, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            var r = new Rectangle(g.ColText, e.TextRectangle.Top, e.Item.Width - g.ColText - g.RightInset, e.TextRectangle.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            return;
        }

        // ── All other items: glyph (left column) + text ──
        DrawGlyph(e);
        {
            var r = new Rectangle(g.ColText, e.TextRectangle.Top, e.Item.Width - g.ColText - g.RightInset, e.TextRectangle.Height);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, r, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    private void DrawGlyph(ToolStripItemTextRenderEventArgs e)
    {
        var g = GeometryFor(e.Graphics);
        string glyph = e.Item.Tag as string ?? "";
        if (string.IsNullOrEmpty(glyph)) return;

        var r = new Rectangle(g.ColIcon, e.TextRectangle.Top, g.ColIconW, e.TextRectangle.Height);
        float sz = e.TextFont?.Size ?? 9.5f;
        using var f = new Font("Segoe UI Symbol", sz, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, glyph, f, r, e.Item.ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    internal static GraphicsPath CreateRoundedRectangle(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
    internal static GraphicsPath CreateRoundedRectangleF(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => Color.FromArgb(18, 19, 22);
    public override Color MenuBorder => Color.FromArgb(37, 40, 51);
    public override Color MenuItemSelected => Color.FromArgb(28, 31, 40);
    public override Color MenuItemBorder => Color.Transparent;
    public override Color SeparatorDark => Color.FromArgb(30, 33, 42);
    public override Color SeparatorLight => Color.FromArgb(30, 33, 42);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(28, 31, 40);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(28, 31, 40);
    public override Color MenuItemPressedGradientBegin => Color.FromArgb(28, 31, 40);
    public override Color MenuItemPressedGradientEnd => Color.FromArgb(28, 31, 40);
    public override Color ImageMarginGradientBegin => Color.FromArgb(18, 19, 22);
    public override Color ImageMarginGradientMiddle => Color.FromArgb(18, 19, 22);
    public override Color ImageMarginGradientEnd => Color.FromArgb(18, 19, 22);
}
