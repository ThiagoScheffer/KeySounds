using System.Collections.ObjectModel;
using KeySounds.Application;
using KeySounds.Application.Profiles;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;

namespace KeySounds.ViewModels;

public sealed class ProfileListItemViewModel(SoundProfile profile, bool active, bool valid) : ObservableObject
{
    public SoundProfile Profile { get; } = profile;
    public string Id => Profile.Id;
    public string Name => Profile.Name;
    public bool IsActive => active;
    public bool IsValid => valid;
    public bool IsFallback => Id == "default";
    public string Status => $"{(IsActive ? "Active · " : string.Empty)}{(IsValid ? "Valid" : "Invalid")}{(IsFallback ? " · Built-in" : string.Empty)}";
}

public sealed class GroupEditorViewModel : ObservableObject
{
    private readonly EditableProfileSession _session; private readonly SoundPackCatalogService _catalog; private readonly SoundPreviewService _preview;
    public GroupEditorViewModel(KeySoundGroup group, KeyGroupMapping mapping, EditableProfileSession session, SoundPackCatalogService catalog, SoundPreviewService preview, Action changed)
    { Group = group; Mapping = mapping; _session = session; _catalog = catalog; _preview = preview; _changed = changed; PreviewDownCommand = new(async () => await PreviewAsync(false), CanPreviewDown); PreviewUpCommand = new(async () => await PreviewAsync(true), CanPreviewUp); }
    private readonly Action _changed;
    public KeySoundGroup Group { get; } public string DisplayName => Group switch { KeySoundGroup.NumberRow => "Number row", KeySoundGroup.Spacebar => "Spacebar", _ => Group.ToString() };
    public KeyGroupMapping Mapping { get; }
    public bool Enabled { get => Mapping.Enabled; set { Mapping.Enabled = value; Changed(); } }
    public string PackId { get => Mapping.SoundPackId; set { Mapping.SoundPackId = value; Raise(); Raise(nameof(SoundSets)); Changed(); } }
    public string DownSetId { get => Mapping.KeyDownSoundSetId; set { Mapping.KeyDownSoundSetId = value; Changed(); } }
    public string? UpSetId { get => Mapping.KeyUpSoundSetId; set { Mapping.KeyUpSoundSetId = string.IsNullOrWhiteSpace(value) ? null : value; Changed(); } }
    public double Volume { get => Mapping.Volume; set { Mapping.Volume = (float)value; Changed(); } }
    public double PitchVariation { get => Mapping.PitchVariationSemitones; set { Mapping.PitchVariationSemitones = (float)value; Changed(); } }
    public double VolumeVariation { get => Mapping.VolumeVariation; set { Mapping.VolumeVariation = (float)value; Changed(); } }
    public VariationSelectionMode VariationMode { get => Mapping.VariationMode; set { Mapping.VariationMode = value; Changed(); } }
    public IReadOnlyList<string> Packs => _catalog.Packs.Where(pack => pack.Validation.IsValid && pack.Manifest is not null).Select(pack => pack.Manifest!.Id).ToArray();
    public IReadOnlyList<string> SoundSets
    {
        get
        {
            var sets = _catalog.TryGetPack(PackId, out var pack) && pack.Manifest is not null ? pack.Manifest.SoundSets.Select(set => set.Id).ToList() : [];
            if (!string.IsNullOrWhiteSpace(DownSetId) && !sets.Contains(DownSetId)) sets.Insert(0, DownSetId);
            if (!string.IsNullOrWhiteSpace(UpSetId) && !sets.Contains(UpSetId)) sets.Add(UpSetId);
            return sets;
        }
    }
    public IReadOnlyList<VariationSelectionMode> VariationModes => Enum.GetValues<VariationSelectionMode>();
    public AsyncCommand PreviewDownCommand { get; } public AsyncCommand PreviewUpCommand { get; }
    private bool CanPreviewDown() => _catalog.TryGetFiles(PackId, DownSetId, out _);
    private bool CanPreviewUp() => UpSetId is not null && _catalog.TryGetFiles(PackId, UpSetId, out _);
    private Task PreviewAsync(bool up) => _preview.PreviewSoundSetAsync(PackId, up ? UpSetId! : DownSetId);
    private void Changed() { Raise(); _changed(); PreviewDownCommand.Refresh(); PreviewUpCommand.Refresh(); }
}

public sealed class OverrideEditorViewModel : ObservableObject
{
    private readonly Action _changed;
    public OverrideEditorViewModel(KeyIdentifier key, KeyOverride value, Action changed, Action<OverrideEditorViewModel>? remove = null)
    { Key = key; Value = value; _changed = changed; RemoveCommand = new(() => remove?.Invoke(this)); }
    public RelayCommand RemoveCommand { get; }
    public KeyIdentifier Key { get; } public KeyOverride Value { get; } public string Label => KeyLabelFormatter.Format(Key);
    public bool Enabled { get => Value.Enabled; set { Value.Enabled = value; _changed(); Raise(); } }
    public string? PackId { get => Value.SoundPackId; set { Value.SoundPackId = Null(value); _changed(); Raise(); } }
    public string? DownSetId { get => Value.KeyDownSoundSetId; set { Value.KeyDownSoundSetId = Null(value); _changed(); Raise(); } }
    public string? UpSetId { get => Value.KeyUpSoundSetId; set { Value.KeyUpSoundSetId = Null(value); _changed(); Raise(); } }
    public double? Volume { get => Value.Volume; set { Value.Volume = value is null ? null : (float)value; _changed(); Raise(); } }
    public double? PitchVariation { get => Value.PitchVariationSemitones; set { Value.PitchVariationSemitones = value is null ? null : (float)value; _changed(); Raise(); } }
    public double? VolumeVariation { get => Value.VolumeVariation; set { Value.VolumeVariation = value is null ? null : (float)value; _changed(); Raise(); } }
    public VariationSelectionMode? VariationMode { get => Value.VariationMode; set { Value.VariationMode = value; _changed(); Raise(); } }
    public IReadOnlyList<VariationSelectionMode?> VariationModes { get; } = [null, .. Enum.GetValues<VariationSelectionMode>().Cast<VariationSelectionMode?>()];
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class ProfileEditorViewModel : ObservableObject
{
    public event EventHandler? StateChanged;
    private readonly EditableProfileSession _session; private readonly KeyboardSoundCoordinator _coordinator; private readonly SoundPackCatalogService _catalog; private readonly SoundPreviewService _preview; private CancellationTokenSource? _captureCancellation;
    public ProfileEditorViewModel(EditableProfileSession session, SoundPackCatalogService catalog, SoundPreviewService preview, KeyboardSoundCoordinator coordinator)
    {
        _session = session; _coordinator = coordinator; _catalog = catalog; _preview = preview;
        foreach (var group in Enum.GetValues<KeySoundGroup>()) if (!session.WorkingCopy.Groups.ContainsKey(group)) session.WorkingCopy.Groups[group] = new() { SoundPackId = "builtin", KeyDownSoundSetId = "click" };
        Groups = []; Overrides = []; BuildCollections();
        CaptureCommand = new(CaptureAsync); CancelCaptureCommand = new RelayCommand(CancelCapture); RevertCommand = new RelayCommand(Revert);
    }
    public EditableProfileSession Session => _session;
    public string Name { get => _session.WorkingCopy.Name; set { _session.WorkingCopy.Name = value; Changed(); } }
    public string? Description { get => _session.WorkingCopy.Description; set { _session.WorkingCopy.Description = value; Changed(); } }
    public bool Enabled { get => _session.WorkingCopy.Enabled; set { _session.WorkingCopy.Enabled = value; Changed(); } }
    public double MasterVolume { get => _session.WorkingCopy.MasterVolume; set { _session.WorkingCopy.MasterVolume = (float)value; Changed(); } }
    public bool PlayOnKeyDown { get => _session.WorkingCopy.PlayOnKeyDown; set { _session.WorkingCopy.PlayOnKeyDown = value; Changed(); } }
    public bool PlayOnKeyUp { get => _session.WorkingCopy.PlayOnKeyUp; set { _session.WorkingCopy.PlayOnKeyUp = value; Changed(); } }
    public RepeatSoundMode RepeatMode { get => _session.WorkingCopy.RepeatMode; set { _session.WorkingCopy.RepeatMode = value; Changed(); } }
    public int RepeatInterval { get => _session.WorkingCopy.RepeatMinimumIntervalMilliseconds; set { _session.WorkingCopy.RepeatMinimumIntervalMilliseconds = value; Changed(); } }
    public IReadOnlyList<RepeatSoundMode> RepeatModes => Enum.GetValues<RepeatSoundMode>();
    public ObservableCollection<GroupEditorViewModel> Groups { get; private set; }
    public ObservableCollection<OverrideEditorViewModel> Overrides { get; private set; }
    public bool IsDirty => _session.HasUnsavedChanges; public bool IsValid => _session.IsValid;
    public string ValidationSummary => IsValid ? "Profile is valid" : string.Join(Environment.NewLine, _session.ValidationIssues.Select(issue => $"{issue.Path}: {issue.Message}"));
    public bool IsCapturing { get; private set; } public string CaptureStatus => IsCapturing ? "Press one physical key…" : "";
    public AsyncCommand CaptureCommand { get; } public RelayCommand CancelCaptureCommand { get; } public RelayCommand RevertCommand { get; }
    private async Task CaptureAsync()
    {
        _captureCancellation = new(); IsCapturing = true; Raise(nameof(IsCapturing)); Raise(nameof(CaptureStatus));
        try
        {
            var key = await _coordinator.CaptureNextKeyAsync(_captureCancellation.Token);
            if (!_session.WorkingCopy.KeyOverrides.TryGetValue(key, out var value)) { value = new(); _session.AddOrUpdateOverride(key, value); }
            var existing = Overrides.FirstOrDefault(item => item.Key == key); if (existing is null) Overrides.Add(new OverrideEditorViewModel(key, value, Changed, RemoveOverride));
            Changed();
        }
        catch (OperationCanceledException) { }
        finally { IsCapturing = false; _captureCancellation?.Dispose(); _captureCancellation = null; Raise(nameof(IsCapturing)); Raise(nameof(CaptureStatus)); }
    }
    public void RemoveOverride(OverrideEditorViewModel item) { if (_session.RemoveOverride(item.Key)) { Overrides.Remove(item); Changed(); } }
    private void CancelCapture() { _captureCancellation?.Cancel(); _coordinator.CancelKeyCapture(); }
    private void Revert() { _session.Revert(); BuildCollections(); RaiseAll(); }
    public void AcceptSaved(SoundProfile profile) { _session.AcceptSaved(profile); BuildCollections(); RaiseAll(); }
    private void Changed() { Raise(nameof(IsDirty)); Raise(nameof(IsValid)); Raise(nameof(ValidationSummary)); StateChanged?.Invoke(this, EventArgs.Empty); }
    private void RaiseAll() { Raise(nameof(Name)); Raise(nameof(Description)); Raise(nameof(Enabled)); Raise(nameof(MasterVolume)); Raise(nameof(PlayOnKeyDown)); Raise(nameof(PlayOnKeyUp)); Raise(nameof(RepeatMode)); Raise(nameof(RepeatInterval)); Changed(); }
    private void BuildCollections()
    {
        Groups = new(_session.WorkingCopy.Groups.OrderBy(item => item.Key).Select(item => new GroupEditorViewModel(item.Key, item.Value, _session, _catalog, _preview, Changed)));
        Overrides = new(_session.WorkingCopy.KeyOverrides.Select(item => new OverrideEditorViewModel(item.Key, item.Value, Changed, RemoveOverride)));
        Raise(nameof(Groups)); Raise(nameof(Overrides));
    }
}

public sealed class ProfilesPageViewModel : ObservableObject, IAsyncDisposable
{
    private readonly ProfileManagementService _management; private readonly Phase2SettingsService _settings; private readonly ProfileValidationService _validator;
    private readonly SoundPackCatalogService _catalog; private readonly SoundPreviewService _preview; private readonly KeyboardSoundCoordinator _coordinator; private readonly IProfileFilePicker _picker;
    private ProfileListItemViewModel? _selected; private ProfileEditorViewModel? _editor; private string? _status; private bool _shutdown;
    public ProfilesPageViewModel(ProfileManagementService management, Phase2SettingsService settings, ProfileValidationService validator, SoundPackCatalogService catalog,
        SoundPreviewService preview, KeyboardSoundCoordinator coordinator, IProfileFilePicker picker)
    {
        _management = management; _settings = settings; _validator = validator; _catalog = catalog; _preview = preview; _coordinator = coordinator; _picker = picker;
        CreateCommand = Command(CreateAsync); DuplicateCommand = Command(DuplicateAsync, () => Selected is not null); DeleteCommand = Command(DeleteAsync, () => Selected is not null);
        SaveCommand = Command(() => SaveAsync(false), () => Editor?.IsDirty == true && Editor.IsValid); SaveAndActivateCommand = Command(() => SaveAsync(true), () => Editor?.IsValid == true);
        ActivateCommand = Command(ActivateAsync, () => Selected is not null && Editor?.IsDirty != true); ImportCommand = Command(ImportAsync); ExportCommand = Command(ExportAsync, () => Selected is not null);
    }
    public ObservableCollection<ProfileListItemViewModel> Items { get; } = [];
    public ProfileListItemViewModel? Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            if (Editor?.IsDirty == true) { Status = "Save or revert the current profile before switching."; Raise(); return; }
            _selected = value; Raise(); LoadEditor(); RefreshCommands();
        }
    }
    public ProfileEditorViewModel? Editor { get => _editor; private set { _editor = value; Raise(); Raise(nameof(IsDirty)); } }
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy => Commands.Any(command => command.IsRunning);
    public bool IsDirty => Editor?.IsDirty == true;
    public int ProfileCount => Items.Count;
    public AsyncCommand CreateCommand { get; } public AsyncCommand DuplicateCommand { get; } public AsyncCommand DeleteCommand { get; }
    public AsyncCommand SaveCommand { get; } public AsyncCommand SaveAndActivateCommand { get; } public AsyncCommand ActivateCommand { get; }
    public AsyncCommand ImportCommand { get; } public AsyncCommand ExportCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [CreateCommand, DuplicateCommand, DeleteCommand, SaveCommand, SaveAndActivateCommand, ActivateCommand, ImportCommand, ExportCommand];
    private AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(async () => { if (_shutdown) return; Status = null; await action(); Raise(nameof(IsBusy)); }, can, _ => Status = "Operation failed. Check the profile and try again.");
    public Task InitializeAsync() { RefreshItems(); return Task.CompletedTask; }
    public void RefreshFromServices() => RefreshItems();
    private void RefreshItems(string? selectId = null)
    {
        selectId ??= Selected?.Id ?? _settings.Settings.ActiveProfileId;
        Items.Clear(); foreach (var profile in _management.Profiles) Items.Add(new(profile, profile.Id == _settings.Settings.ActiveProfileId, !_validator.Validate(profile).Any(issue => issue.Severity == ProfileValidationSeverity.Error)));
        _selected = Items.FirstOrDefault(item => item.Id == selectId) ?? Items.FirstOrDefault(); Raise(nameof(Selected)); Raise(nameof(ProfileCount)); LoadEditor(); RefreshCommands();
    }
    private void LoadEditor()
    {
        Editor = Selected is null ? null : new(new(Selected.Profile, _validator), _catalog, _preview, _coordinator);
        if (Editor is not null) Editor.StateChanged += (_, _) => { Raise(nameof(IsDirty)); RefreshCommands(); };
    }
    private async Task CreateAsync() { var result = await _management.CreateAsync("New Profile", Selected?.Profile); Complete(result); }
    private async Task DuplicateAsync() { if (Selected is null) return; Complete(await _management.DuplicateAsync(Selected.Profile)); }
    private async Task DeleteAsync() { if (Selected is null) return; var deleted = Selected.Id; var result = await _management.DeleteAsync(Selected.Profile); Complete(result, result.Success ? null : deleted); }
    private async Task SaveAsync(bool activate) { if (Editor is null) return; var result = await _management.SaveAsync(Editor.Session, activate); if (result.Success && result.Profile is not null) Editor.AcceptSaved(result.Profile); Complete(result); }
    private async Task ActivateAsync() { if (Selected is null) return; try { await _settings.ActivateAsync(Selected.Id); RefreshItems(Selected.Id); Status = "Profile activated."; } catch (InvalidOperationException ex) { Status = ex.Message; } catch { Status = "The profile could not be activated."; } }
    private async Task ImportAsync() { var path = await _picker.PickImportAsync(); if (path is null) return; Complete(await _management.ImportAsync(path, ProfileImportConflictPolicy.ImportAsCopy)); }
    private async Task ExportAsync() { if (Selected is null) return; var path = await _picker.PickExportAsync(Selected.Id); if (path is null) return; Complete(await _management.ExportAsync(Selected.Profile, path)); }
    private void Complete(ProfileOperationResult result, string? selectId = null) { Status = result.Success ? "Operation completed." : result.Message; if (result.Success) RefreshItems(result.Profile?.Id ?? selectId); RefreshCommands(); }
    private void RefreshCommands() { foreach (var command in Commands) command.Refresh(); Raise(nameof(IsDirty)); }
    public ValueTask DisposeAsync() { _shutdown = true; Editor?.CancelCaptureCommand.Execute(null); return ValueTask.CompletedTask; }
}
