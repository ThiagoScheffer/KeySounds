using System.Collections.ObjectModel;
using Avalonia.Threading;
using KeySounds.Application.Keyboards;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.Input;

namespace KeySounds.ViewModels;

public sealed class KeyboardRowViewModel : ObservableObject
{
    private readonly KeyboardProfileAssignmentService _assignments; private readonly Phase2SettingsService _settings; private readonly SoundPreviewService _preview; private string? _selectedProfile;
    public KeyboardRowViewModel(KeyboardDeviceDescriptor descriptor, KeyboardProfileAssignmentService assignments, Phase2SettingsService settings, SoundPreviewService preview)
    { Descriptor = descriptor; _assignments = assignments; _settings = settings; _preview = preview; _selectedProfile = AssignedProfileId; ApplyCommand = new(ApplyAsync, () => descriptor.IsPersistent); ClearCommand = new(ClearAsync); PreviewCommand = new(PreviewAsync, () => EffectiveProfile is not null); }
    public KeyboardDeviceDescriptor Descriptor { get; } public string DisplayName => Descriptor.DisplayName;
    public string Reliability => !Descriptor.IsPersistent ? "Session-only — persistent assignment unavailable" : Descriptor.IsIdentityAmbiguous ? "Persistent but ambiguous identity" : "Persistent identity";
    public string Instances => $"{Descriptor.ConnectedSessionCount} connected instance{(Descriptor.ConnectedSessionCount == 1 ? "" : "s")}";
    public IReadOnlyList<string> Profiles => _settings.Profiles.Select(profile => profile.Name).ToArray();
    public string? SelectedProfile { get => _selectedProfile; set => Set(ref _selectedProfile, value); }
    public string? AssignedProfileId => _assignments.GetAssignedProfileId(Descriptor.Id);
    public string AssignedProfile => _settings.Profiles.FirstOrDefault(profile => profile.Id == AssignedProfileId)?.Name ?? (AssignedProfileId is null ? "None" : $"Missing profile “{AssignedProfileId}”");
    public Core.Profiles.SoundProfile? EffectiveProfile => _settings.Profiles.FirstOrDefault(profile => profile.Id == (AssignedProfileId ?? _settings.Settings.ActiveProfileId));
    public string EffectiveProfileName => EffectiveProfile?.Name + (AssignedProfileId is null ? " (global)" : string.Empty) ?? "Fallback";
    public string RuntimeStatus => _assignments.IsRuntimeReady(Descriptor.Id) ? "Ready" : AssignedProfileId is null ? "Using global profile" : "Preparing or invalid assignment";
    public AsyncCommand ApplyCommand { get; } public AsyncCommand ClearCommand { get; } public AsyncCommand PreviewCommand { get; }
    public string? Message { get; private set; }
    private async Task ApplyAsync() { var profile = _settings.Profiles.FirstOrDefault(item => item.Name == SelectedProfile); if (profile is null) return; var result = await _assignments.AssignAsync(Descriptor.Id, profile.Id); Message = result.Success ? "Assignment applied." : result.Message; RaiseAll(); }
    private async Task ClearAsync() { var result = await _assignments.ClearAsync(Descriptor.Id); Message = result.Success ? "Assignment cleared." : result.Message; RaiseAll(); }
    private async Task PreviewAsync() { var profile = EffectiveProfile; var mapping = profile?.Groups.Values.FirstOrDefault(item => item.Enabled); if (mapping is not null) await _preview.PreviewSoundSetAsync(mapping.SoundPackId, mapping.KeyDownSoundSetId); }
    private void RaiseAll() { Raise(nameof(AssignedProfile)); Raise(nameof(EffectiveProfileName)); Raise(nameof(RuntimeStatus)); Raise(nameof(Message)); }
}

public sealed class KeyboardsViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IKeyboardDeviceIdentityService _identity; private readonly KeyboardProfileAssignmentService _assignments; private readonly Phase2SettingsService _settings; private readonly SoundPreviewService _preview; private string? _status; private bool _shutdown;
    public KeyboardsViewModel(IKeyboardDeviceIdentityService identity, KeyboardProfileAssignmentService assignments, Phase2SettingsService settings, SoundPreviewService preview)
    { _identity = identity; _assignments = assignments; _settings = settings; _preview = preview; RefreshCommand = new(RefreshAsync); _identity.DevicesChanged += OnDevicesChanged; _assignments.Changed += OnAssignmentsChanged; }
    public ObservableCollection<KeyboardRowViewModel> Devices { get; } = [];
    public AsyncCommand RefreshCommand { get; }
    public string GlobalProfile => _settings.Profiles.FirstOrDefault(profile => profile.Id == _settings.Settings.ActiveProfileId)?.Name ?? "Fallback";
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public async Task InitializeAsync(CancellationToken token = default) { await RefreshAsync(token); }
    private Task RefreshAsync() => RefreshAsync(default);
    private async Task RefreshAsync(CancellationToken token) { if (_shutdown) return; Status = "Resolving connected keyboards…"; await _identity.RefreshAsync(token); await _assignments.ReconcileAsync(token); Rebuild(); Status = Devices.Count == 0 ? "No Raw Input keyboards were detected." : $"{Devices.Count} keyboard identit{(Devices.Count == 1 ? "y" : "ies")} detected."; }
    private void Rebuild() { Devices.Clear(); foreach (var descriptor in _identity.GetConnectedDevicesSnapshot()) Devices.Add(new(descriptor, _assignments, _settings, _preview)); Raise(nameof(GlobalProfile)); }
    private void OnDevicesChanged(object? sender, KeyboardDevicesChangedEventArgs args) => Dispatcher.UIThread.Post(Rebuild);
    private void OnAssignmentsChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(Rebuild);
    public ValueTask DisposeAsync() { _shutdown = true; _identity.DevicesChanged -= OnDevicesChanged; _assignments.Changed -= OnAssignmentsChanged; return ValueTask.CompletedTask; }
}
