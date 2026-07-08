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

namespace KeySounds;

public partial class App : Avalonia.Application
{
    private KeyboardSoundCoordinator? _coordinator;
    private Task? _shutdownTask;
    private bool _shutdownComplete;
    private ISoundSampleCache? _sampleCache;
    private SoundPackCatalogService? _catalog;
    private Phase2SettingsService? _settings;
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
            var activeProfiles = new ActiveProfileService(ProfileCompiler.Compile(Phase2Defaults.CreateProfile()));
            var paths = new StoragePaths();
            var writer = new AtomicFileWriter();
            _catalog = new SoundPackCatalogService(paths, new SoundPackValidationService());
            _sampleCache = new SoundSampleCache(_catalog, new NAudioSoundDecoder());
            var router = new KeyboardSoundRouter(activeProfiles, new KeyGroupResolver(), new SoundVariationSelector(), _catalog);
            var audio = new WasapiAudioEngine("avares://KeySounds/Assets/Audio/key-click.wav", _sampleCache);
            var settings = new Phase2SettingsService(new JsonApplicationSettingsRepository(paths, writer), new JsonProfileRepository(paths, writer), activeProfiles, router, _sampleCache, _catalog);
            _settings = settings;
            _coordinator = new KeyboardSoundCoordinator(input, audio, router);
            var viewModel = new MainWindowViewModel(_coordinator, settings);
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
            if (_settings is not null) await _settings.DisposeAsync();
            if (_coordinator is not null) await _coordinator.DisposeAsync();
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
