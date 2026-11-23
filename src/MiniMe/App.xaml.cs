using System.Threading;
using System.Windows;
using MiniMe.Engine;
using MiniMe.Models;
using MiniMe.Views;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MiniMe;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _bossModeMenuItem;
    private Drawing.Icon? _generatedTrayIcon;

    private AppSettings _settings = new();
    private BrainService _brain = new();
    private PetWindow? _petWindow;
    private SetupWindow? _setupWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isBossMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "MiniMe_DesktopPet_SingleInstance_Mutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show(
                "MiniMe is already running in your system tray!",
                "MiniMe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load();
        _brain = new BrainService();

        InitializeTrayIcon();

        _petWindow = new PetWindow(_settings, _brain);
        _petWindow.OpenSetupRequested += OpenSetupWindow;
        _petWindow.OpenSettingsRequested += OpenSettingsWindow;
        _petWindow.BossModeToggleRequested += ToggleBossMode;
        _petWindow.ExitRequested += ExitApplication;
        _petWindow.Show();
    }

    private void InitializeTrayIcon()
    {
        _generatedTrayIcon = CreateProceduralTrayIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Skin Setup...", null, (_, _) => Dispatcher.Invoke(OpenSetupWindow));
        menu.Items.Add("Settings...", null, (_, _) => Dispatcher.Invoke(OpenSettingsWindow));

        _bossModeMenuItem = new Forms.ToolStripMenuItem("Boss / Focus Mode (Ctrl+Shift+H)")
        {
            CheckOnClick = false,
            Checked = _isBossMode
        };
        _bossModeMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleBossMode);
        menu.Items.Add(_bossModeMenuItem);

        menu.Items.Add("Summon to Center", null, (_, _) => Dispatcher.Invoke(SummonPetToCenter));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit MiniMe", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "MiniMe Desktop Pet",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(SummonPetToCenter);
    }

    public void OpenSetupWindow()
    {
        if (_setupWindow is not null && _setupWindow.IsLoaded)
        {
            _setupWindow.Activate();
            return;
        }

        _setupWindow = new SetupWindow(_settings);
        _setupWindow.SkinActivated += (skinPath, tolerance) =>
        {
            _settings.ActiveSkinPath = skinPath;
            _settings.ChromaTolerance = tolerance;
            SettingsStore.Save(_settings);
            _petWindow?.ReloadSkin(_settings);
        };
        _setupWindow.Closed += (_, _) => _setupWindow = null;
        _setupWindow.Show();
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is not null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _isBossMode);
        _settingsWindow.SettingsSaved += updated =>
        {
            _settings = updated;
            SettingsStore.Save(_settings);
            _petWindow?.ApplySettings(_settings);
        };
        _settingsWindow.BossModeToggleRequested += ToggleBossMode;
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    public void ToggleBossMode()
    {
        _isBossMode = !_isBossMode;
        if (_bossModeMenuItem is not null)
        {
            _bossModeMenuItem.Checked = _isBossMode;
        }

        _petWindow?.SetBossMode(_isBossMode);
        _settingsWindow?.SyncBossModeState(_isBossMode);

        if (_trayIcon is not null)
        {
            _trayIcon.Text = _isBossMode
                ? "MiniMe (Boss Mode: Hidden)"
                : "MiniMe Desktop Pet";
        }
    }

    public void SummonPetToCenter()
    {
        if (_isBossMode)
        {
            ToggleBossMode();
        }
        _petWindow?.SummonToCenter();
    }

    public void ExitApplication()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _generatedTrayIcon?.Dispose();
        _generatedTrayIcon = null;

        _setupWindow?.Close();
        _settingsWindow?.Close();
        _petWindow?.Close();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;

        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _generatedTrayIcon?.Dispose();
        base.OnExit(e);
    }

    private static Drawing.Icon CreateProceduralTrayIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (Drawing.Graphics g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(245, 158, 11));
            using var borderPen = new Drawing.Pen(Drawing.Color.FromArgb(18, 20, 26), 2.5f);
            g.FillEllipse(bgBrush, 2, 2, 28, 28);
            g.DrawEllipse(borderPen, 2, 2, 28, 28);

            // Cute chibi eyes + smile in tray icon
            using var eyeBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(18, 20, 26));
            g.FillEllipse(eyeBrush, 9, 11, 4, 5);
            g.FillEllipse(eyeBrush, 19, 11, 4, 5);
            using var smilePen = new Drawing.Pen(Drawing.Color.FromArgb(18, 20, 26), 2.2f);
            g.DrawArc(smilePen, 11, 15, 10, 7, 15, 150);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Drawing.Icon.FromHandle(hIcon);
    }
}
