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

namespace KeySounds;

public partial class App : Avalonia.Application
{
    private KeyboardSoundCoordinator? _coordinator;
    private Task? _shutdownTask;
    private bool _shutdownComplete;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var input = new WindowsRawInputSource();
            var audio = new WasapiAudioEngine("avares://KeySounds/Assets/Audio/key-click.wav");
            var activeProfiles = new ActiveProfileService(ProfileCompiler.Compile(Phase2Defaults.CreateProfile()));
            var router = Phase2Defaults.CreateRouter(activeProfiles);
            var paths = new StoragePaths();
            var writer = new AtomicFileWriter();
            var settings = new Phase2SettingsService(new JsonApplicationSettingsRepository(paths, writer), new JsonProfileRepository(paths, writer), activeProfiles, router);
            _coordinator = new KeyboardSoundCoordinator(input, audio, router);
            var viewModel = new MainWindowViewModel(_coordinator, settings);
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
            desktop.Startup += async (_, _) => await viewModel.StartAsync();
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
            if (_coordinator is not null) await _coordinator.DisposeAsync();
        }
        catch (Exception ex)
        {
            viewModel.ReportShutdownError(ex);
        }
        finally
        {
            _shutdownComplete = true;
            desktop.Shutdown();
        }
    }
}
