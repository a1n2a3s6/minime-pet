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
    private ScreenVisionService _vision = new();

    private PetWindow? _petWindow;
    private SetupWizardWindow? _wizardWindow;
    private SettingsWindow? _settingsWindow;
    private bool _isBossMode;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, "MiniMe_PetV2_SingleInstance_Mutex", out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show(
                "MiniMe Pet v2.0 is already running in your system tray!",
                "MiniMe",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _settings = SettingsStore.Load();
        _brain = new BrainService();
        _vision = new ScreenVisionService();

        InitializeTrayIcon();

        if (!_settings.HasCompletedOnboarding)
        {
            OpenSetupWizard(launchPetAfterFinish: true);
        }
        else
        {
            EnsurePetWindowRunning();
        }
    }

    private void EnsurePetWindowRunning()
    {
        if (_petWindow is not null && _petWindow.IsLoaded)
        {
            _petWindow.ApplySettings(_settings);
            _petWindow.ReloadSkin(_settings);
            if (!_isBossMode)
            {
                _petWindow.Show();
            }
            return;
        }

        _petWindow = new PetWindow(_settings, _brain, _vision);
        _petWindow.OpenWizardRequested += () => OpenSetupWizard(launchPetAfterFinish: false);
        _petWindow.OpenSettingsRequested += OpenSettingsWindow;
        _petWindow.BossModeToggleRequested += ToggleBossMode;
        _petWindow.ExitRequested += ExitApplication;
        _petWindow.Show();
    }

    private void InitializeTrayIcon()
    {
        _generatedTrayIcon = CreateProceduralTrayIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("⚡ Scan My Screen Now (Ctrl+Shift+S)", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerInstantScreenScan()));

        var chaosSubmenu = new Forms.ToolStripMenuItem("😈 Trigger Chaos Now");
        chaosSubmenu.DropDownItems.Add("Shove Window", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.ShoveWindow)));
        chaosSubmenu.DropDownItems.Add("Steal Mouse", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.StealMouse)));
        chaosSubmenu.DropDownItems.Add("Shake Window", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.ShakeWindow)));
        chaosSubmenu.DropDownItems.Add("Squish / Resize Window", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.SquishWindow)));
        chaosSubmenu.DropDownItems.Add("Climb Active Window", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.ClimbWindow)));
        chaosSubmenu.DropDownItems.Add("Minimize Active Window", null, (_, _) => Dispatcher.Invoke(() => _petWindow?.TriggerChaosPrank(ChaosPrankType.MinimizeWindow)));
        menu.Items.Add(chaosSubmenu);

        _bossModeMenuItem = new Forms.ToolStripMenuItem("🤫 Boss / Focus Mode (Ctrl+Shift+H)")
        {
            CheckOnClick = false,
            Checked = _isBossMode
        };
        _bossModeMenuItem.Click += (_, _) => Dispatcher.Invoke(ToggleBossMode);
        menu.Items.Add(_bossModeMenuItem);

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("🎨 Open Skin & Setup Wizard", null, (_, _) => Dispatcher.Invoke(() => OpenSetupWizard(launchPetAfterFinish: false)));
        menu.Items.Add("⚙️ Settings", null, (_, _) => Dispatcher.Invoke(OpenSettingsWindow));
        menu.Items.Add("Summon to Center", null, (_, _) => Dispatcher.Invoke(SummonPetToCenter));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("❌ Exit", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _generatedTrayIcon,
            Text = "MiniMe Pet v2.0",
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(SummonPetToCenter);
    }

    public void OpenSetupWizard(bool launchPetAfterFinish)
    {
        if (_wizardWindow is not null && _wizardWindow.IsLoaded)
        {
            _wizardWindow.Activate();
            return;
        }

        _wizardWindow = new SetupWizardWindow(_settings, _brain);
        _wizardWindow.WizardCompleted += updated =>
        {
            updated.HasCompletedOnboarding = true;
            _settings = updated;
            SettingsStore.Save(_settings);
            EnsurePetWindowRunning();
        };
        _wizardWindow.Closed += (_, _) =>
        {
            _wizardWindow = null;
            if (launchPetAfterFinish && _petWindow is null)
            {
                _settings.HasCompletedOnboarding = true;
                SettingsStore.Save(_settings);
                EnsurePetWindowRunning();
            }
        };
        _wizardWindow.Show();
    }

    public void OpenSettingsWindow()
    {
        if (_settingsWindow is not null && _settingsWindow.IsLoaded)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _brain, _isBossMode);
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
                ? "MiniMe v2.0 (Boss Mode: Hidden)"
                : "MiniMe Pet v2.0";
        }
    }

    public void SummonPetToCenter()
    {
        if (_isBossMode)
        {
            ToggleBossMode();
        }
        EnsurePetWindowRunning();
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

        _wizardWindow?.Close();
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

            using var bgBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(16, 185, 129));
            using var borderPen = new Drawing.Pen(Drawing.Color.FromArgb(12, 15, 20), 2.5f);
            g.FillEllipse(bgBrush, 2, 2, 28, 28);
            g.DrawEllipse(borderPen, 2, 2, 28, 28);

            using var eyeBrush = new Drawing.SolidBrush(Drawing.Color.FromArgb(12, 15, 20));
            g.FillEllipse(eyeBrush, 9, 11, 4, 5);
            g.FillEllipse(eyeBrush, 19, 11, 4, 5);
            using var smilePen = new Drawing.Pen(Drawing.Color.FromArgb(12, 15, 20), 2.2f);
            g.DrawArc(smilePen, 11, 15, 10, 7, 15, 150);
        }

        IntPtr hIcon = bmp.GetHicon();
        return Drawing.Icon.FromHandle(hIcon);
    }
}
