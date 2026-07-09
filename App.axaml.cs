using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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

namespace KeySounds;

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
            var writer = new AtomicFileWriter();
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
            var viewModel = new MainWindowViewModel(_coordinator, settings, _profilesPage, _audioPage, _keyboardsPage, _soundPacksPage);
            _mainViewModel = viewModel;
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
            desktop.Startup += (_, _) => _startupTask = StartApplicationAsync(viewModel);
            mainWindow.Closing += async (_, args) =>
            {
                if (_shutdownComplete) return;
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
        catch (Exception ex) { viewModel.ReportShutdownError(ex); }
    }
}
