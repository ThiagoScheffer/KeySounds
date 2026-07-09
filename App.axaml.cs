using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using System.Runtime.Versioning;
using KeySounds.Application;
using KeySounds.Audio;
using KeySounds.Platform.Windows.Input;
using KeySounds.ViewModels;
using KeySounds.Application.Profiles;
using KeySounds.Application.Routing;
using KeySounds.Application.Settings;
using KeySounds.Infrastructure.Persistence;
using KeySounds.Application.SoundPacks;
using KeySounds.Application.Audio;
using KeySounds.Application.Keyboards;
using KeySounds.Application.Diagnostics;
using KeySounds.Application.SystemIntegration;

namespace KeySounds;

[SupportedOSPlatform("windows")]
public partial class App : Avalonia.Application
{
    private KeyboardSoundCoordinator? _coordinator;
    private Task? _shutdownTask;
    private bool _shutdownComplete;
    private ISoundSampleCache? _sampleCache;
    private SoundPackCatalogService? _catalog;
    private Phase2SettingsService? _settings;
    private ProfileManagementService? _profileManagement;
    private SoundPreviewService? _preview;
    private SoundPackImportService? _soundPackImport;
    private SoundPacksViewModel? _soundPacksPage;
    private ProfilesPageViewModel? _profilesPage;
    private MainWindowViewModel? _mainViewModel;
    private WindowsAudioOutputDeviceService? _audioDevices;
    private AudioDeviceSelectionService? _audioSelection;
    private AudioSettingsViewModel? _audioPage;
    private KeyboardDeviceIdentityService? _keyboardIdentity;
    private KeyboardProfileAssignmentService? _keyboardAssignments;
    private KeyboardsViewModel? _keyboardsPage;
    private SettingsViewModel? _settingsPage;
    private TrayIcon? _trayIcon;
    private StoragePaths? _paths;
    private LocalErrorReportService? _errorReports;
    private bool _exitRequested;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private Task? _startupTask;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var input = new WindowsRawInputSource();
            _keyboardIdentity = new KeyboardDeviceIdentityService(new WindowsKeyboardDeviceMetadataProvider());
            var activeProfiles = new ActiveProfileService(ProfileCompiler.Compile(Phase2Defaults.CreateProfile()));
            var paths = new StoragePaths();
            _paths = paths;
            var writer = new AtomicFileWriter();
            _errorReports = new LocalErrorReportService(paths);
            var soundPackValidator = new SoundPackValidationService();
            _catalog = new SoundPackCatalogService(paths, soundPackValidator);
            _sampleCache = new SoundSampleCache(_catalog, new NAudioSoundDecoder());
            var router = new KeyboardSoundRouter(activeProfiles, new KeyGroupResolver(), new SoundVariationSelector(), _catalog);
            _audioDevices = new WindowsAudioOutputDeviceService();
            var audio = new WasapiAudioEngine("avares://KeySounds/Assets/Audio/key-click.wav", _sampleCache, _audioDevices);
            var settings = new Phase2SettingsService(new JsonApplicationSettingsRepository(paths, writer), new JsonProfileRepository(paths, writer), activeProfiles, router, _sampleCache, _catalog, audio);
            _settings = settings;
            _keyboardAssignments = new KeyboardProfileAssignmentService(_keyboardIdentity, settings, activeProfiles, _sampleCache);
            router.SetDeviceProfileResolver(_keyboardAssignments);
            _coordinator = new KeyboardSoundCoordinator(input, audio, router, _keyboardIdentity);
            _preview = new SoundPreviewService(_sampleCache, audio, _catalog, new SoundVariationSelector());
            var profileRepository = new JsonProfileRepository(paths, writer);
            var profileValidator = new ProfileValidationService(_catalog);
            _profileManagement = new ProfileManagementService(profileRepository, settings, profileValidator, writer);
            _audioSelection = new AudioDeviceSelectionService(_audioDevices, audio, settings, _sampleCache);
            _audioPage = new AudioSettingsViewModel(_audioSelection, audio);
            var picker = new AvaloniaProfileFilePicker(() => desktop.MainWindow);
            _soundPackImport = new SoundPackImportService(paths, soundPackValidator, _catalog, () => settings.Profiles, _sampleCache);
            var soundPackPicker = new AvaloniaSoundPackFolderPicker(() => desktop.MainWindow);
            var soundPackConflicts = new AvaloniaSoundPackConflictResolver(() => desktop.MainWindow);
            var soundPackDependencies = new SoundPackDependencyService(settings);
            _soundPacksPage = new SoundPacksViewModel(_catalog, soundPackValidator, _soundPackImport, settings, _preview, _sampleCache, soundPackPicker, soundPackConflicts, soundPackDependencies, new StorageCleanupService(paths));
            _profilesPage = new ProfilesPageViewModel(_profileManagement, settings, profileValidator, _catalog, _preview, _coordinator, picker);
            _keyboardsPage = new KeyboardsViewModel(_keyboardIdentity, _keyboardAssignments, settings, _preview);
            _settingsPage = new SettingsViewModel(settings, new StartupRegistrationService(new RegistryStartupRegistrationStore()), new DiagnosticReportService(paths), paths);
            var viewModel = new MainWindowViewModel(_coordinator, settings, _profilesPage, _audioPage, _keyboardsPage, _soundPacksPage, _settingsPage);
            _mainViewModel = viewModel;
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
            _trayIcon = CreateTrayIcon(mainWindow, viewModel);
            Program.SingleInstance?.StartActivationServer();
            if (Program.SingleInstance is not null) Program.SingleInstance.ActivationRequested += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => OpenMainWindow(mainWindow));
            desktop.Startup += (_, _) =>
            {
                _startupTask = StartApplicationAsync(viewModel);
                if (!settings.Settings.ShowWindowOnStartup) mainWindow.Hide();
            };
            mainWindow.PropertyChanged += (_, args) =>
            {
                if (args.Property.Name == nameof(Window.WindowState) && mainWindow.WindowState == WindowState.Minimized && settings.Settings.MinimizeToTray)
                    mainWindow.Hide();
            };
            mainWindow.Closing += async (_, args) =>
            {
                if (_shutdownComplete) return;
                if (!_exitRequested && settings.Settings.CloseToTray)
                {
                    args.Cancel = true;
                    mainWindow.Hide();
                    return;
                }
                args.Cancel = true;
                try { await (_shutdownTask ??= ShutdownAndExitAsync(desktop, viewModel)); }
                catch (Exception ex) { viewModel.ReportShutdownError(ex); }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task ShutdownAndExitAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindowViewModel viewModel)
    {
        try
        {
            _lifetimeCancellation.Cancel();
            _trayIcon?.Dispose();
            if (_startupTask is not null) await _startupTask;
            if (_profilesPage is not null) await _profilesPage.DisposeAsync();
            if (_soundPacksPage is not null) await _soundPacksPage.DisposeAsync();
            if (_keyboardsPage is not null) await _keyboardsPage.DisposeAsync();
            if (_audioPage is not null) await _audioPage.DisposeAsync();
            if (_audioSelection is not null) await _audioSelection.DisposeAsync();
            if (_profileManagement is not null) await _profileManagement.DisposeAsync();
            if (_soundPackImport is not null) await _soundPackImport.DisposeAsync();
            if (_preview is not null) await _preview.DisposeAsync();
            if (_keyboardAssignments is not null) await _keyboardAssignments.DisposeAsync();
            if (_keyboardIdentity is not null) await _keyboardIdentity.DisposeAsync();
            if (_settings is not null) await _settings.DisposeAsync();
            if (_mainViewModel is not null) await _mainViewModel.StopAsync();
            if (_coordinator is not null) await _coordinator.DisposeAsync();
            if (_audioDevices is not null) await _audioDevices.DisposeAsync();
            if (_sampleCache is not null) await _sampleCache.DisposeAsync();
            if (_catalog is not null) await _catalog.DisposeAsync();
        }
        catch (Exception ex)
        {
            viewModel.ReportShutdownError(ex);
        }
        finally
        {
            _lifetimeCancellation.Dispose();
            _shutdownComplete = true;
            desktop.Shutdown();
        }
    }

    private async Task StartApplicationAsync(MainWindowViewModel viewModel)
    {
        try { await viewModel.StartAsync(_lifetimeCancellation.Token); }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested) { }
        catch (Exception ex) { viewModel.ReportShutdownError(ex); await WriteErrorReportAsync(ex, "Startup", "StartApplication"); }
    }

    private TrayIcon CreateTrayIcon(Window window, MainWindowViewModel viewModel)
    {
        var open = new NativeMenuItem("Open KeySounds");
        open.Click += (_, _) => OpenMainWindow(window);
        var toggle = new NativeMenuItem();
        toggle.Click += (_, _) => viewModel.IsEnabled = !viewModel.IsEnabled;
        var active = new NativeMenuItem();
        active.IsEnabled = false;
        var soundPacks = new NativeMenuItem("Open Sound Packs");
        soundPacks.Click += (_, _) =>
        {
            viewModel.SelectedTabIndex = 2;
            OpenMainWindow(window);
        };
        var diagnostics = new NativeMenuItem("Export Diagnostics");
        diagnostics.Click += (_, _) => viewModel.SettingsPage?.ExportDiagnosticsCommand.Execute(null);
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => RequestExit(window);
        var menu = new NativeMenu { Items = { open, toggle, active, soundPacks, diagnostics, exit } };
        menu.NeedsUpdate += (_, _) =>
        {
            toggle.Header = viewModel.IsEnabled ? "Sounds Enabled (click to disable)" : "Sounds Disabled (click to enable)";
            active.Header = "Active Profile: " + (viewModel.SelectedProfile ?? "Fallback");
        };
        var icon = new TrayIcon { ToolTipText = "KeySounds", IsVisible = true, Menu = menu };
        try { icon.Icon = new WindowIcon(new MemoryStream(TinyIcon)); } catch { }
        icon.Clicked += (_, _) => OpenMainWindow(window);
        return icon;
    }

    private void OpenMainWindow(Window window)
    {
        if (_shutdownComplete) return;
        window.Show();
        window.WindowState = WindowState.Normal;
        window.ShowInTaskbar = true;
        window.Activate();
    }

    private void RequestExit(Window window)
    {
        _exitRequested = true;
        window.Close();
    }

    private async Task WriteErrorReportAsync(Exception exception, string category, string operation)
    {
        if (_errorReports is null || _settings is null) return;
        try { await _errorReports.WriteAsync(exception, _settings.Settings, category, operation); } catch { }
    }

    private static readonly byte[] TinyIcon =
    [
        0,0,1,0,1,0,1,1,0,0,1,0,32,0,48,0,0,0,22,0,0,0,40,0,0,0,1,0,0,0,2,0,0,0,1,0,32,0,0,0,0,0,4,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0
    ];
}
