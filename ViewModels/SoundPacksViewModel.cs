using System.Collections.ObjectModel;
using KeySounds.Application.Profiles;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Audio;
using KeySounds.Core.Input;
using KeySounds.Core.Profiles;
using KeySounds.Core;
using KeySounds.Core.Audio;
using KeySounds.Core.SoundPacks;

namespace KeySounds.ViewModels;

public sealed class SoundPackSampleViewModel : ObservableObject
{
    private readonly SoundPreviewService _preview;
    private readonly Func<bool> _canOperate;
    private string? _status;

    public SoundPackSampleViewModel(string packId, string soundSetId, SoundSampleValidationMetadata metadata, SoundPreviewService preview, Func<bool> canOperate)
    {
        PackId = packId;
        SoundSetId = soundSetId;
        Metadata = metadata;
        _preview = preview;
        _canOperate = canOperate;
        PreviewCommand = new(PreviewAsync, () => _canOperate(), ex => Status = $"Preview failed: {ex.Message}");
    }

    public string PackId { get; }
    public string SoundSetId { get; }
    public SoundSampleValidationMetadata Metadata { get; }
    public string Path => Metadata.RelativePath;
    public string Duration => Metadata.Duration is null ? "Unknown duration" : $"{Metadata.Duration.Value.TotalMilliseconds:F0} ms";
    public string DecodedSize => Metadata.EstimatedDecodedBytes is null ? "Unknown size" : $"{Metadata.EstimatedDecodedBytes.Value / 1024d:F1} KiB";
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand PreviewCommand { get; }

    private async Task PreviewAsync()
    {
        Status = null;
        await _preview.PreviewSampleAsync(new SoundId(PackId, SoundSetId, Path));
        Status = "Preview submitted.";
    }
}

public sealed class SoundSetRowViewModel : ObservableObject
{
    private readonly SoundPreviewService _preview;
    private readonly Func<bool> _canOperate;
    private string? _status;

    public SoundSetRowViewModel(string packId, SoundSetDefinition set, IReadOnlyList<SoundSampleValidationMetadata> samples, ISoundSampleCache cache, SoundPreviewService preview, Func<bool> canOperate)
    {
        PackId = packId;
        Set = set;
        _preview = preview;
        _canOperate = canOperate;
        Samples = new(samples.Select(item => new SoundPackSampleViewModel(packId, set.Id, item, preview, canOperate)));
        var snapshot = cache.GetSnapshot();
        LoadedState = snapshot.LoadedSampleCount == 0 ? "Not loaded" : "Loaded state available in cache summary";
        PreviewCommand = new(PreviewAsync, () => _canOperate() && Samples.Count > 0, ex => Status = $"Preview failed: {ex.Message}");
    }

    public string PackId { get; }
    public SoundSetDefinition Set { get; }
    public string Id => Set.Id;
    public string Name => Set.Name;
    public int SampleCount => Set.Files.Count;
    public string ReferencedFiles => string.Join(", ", Set.Files);
    public string LoadedState { get; }
    public ObservableCollection<SoundPackSampleViewModel> Samples { get; }
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public AsyncCommand PreviewCommand { get; }

    private async Task PreviewAsync()
    {
        Status = null;
        await _preview.PreviewSoundSetAsync(PackId, Id);
        Status = "Preview submitted.";
    }
}

public sealed class SoundPackRowViewModel : ObservableObject
{
    private readonly ISoundSampleCache _cache;
    private readonly SoundPreviewService _preview;
    private readonly Func<bool> _canOperate;

    public SoundPackRowViewModel(InstalledSoundPack pack, SoundPackDependencySummary dependencies, ISoundSampleCache cache, SoundPreviewService preview, Func<bool> canOperate)
    {
        Pack = pack;
        Dependencies = dependencies;
        _cache = cache;
        _preview = preview;
        _canOperate = canOperate;
        BuildSoundSets();
    }

    public InstalledSoundPack Pack { get; }
    public SoundPackDependencySummary Dependencies { get; }
    public string Id => Pack.Manifest?.Id ?? Path.GetFileName(Pack.RootDirectory);
    public string Name => Pack.Manifest?.Name ?? "Invalid sound pack";
    public string Author => string.IsNullOrWhiteSpace(Pack.Manifest?.Author) ? "Unknown author" : Pack.Manifest!.Author!;
    public string Description => string.IsNullOrWhiteSpace(Pack.Manifest?.Description) ? "No description." : Pack.Manifest!.Description!;
    public string LocationType => Pack.IsBuiltIn ? "Built-in / protected" : "User-installed";
    public int SchemaVersion => Pack.Manifest?.SchemaVersion ?? 0;
    public string ValidationStatus => Pack.Validation.IsValid ? "Valid" : "Invalid";
    public int SoundSetCount => Pack.Manifest?.SoundSets.Count ?? 0;
    public int SampleCount => Pack.Validation.SampleCount;
    public string EstimatedMemory => $"{Pack.Validation.EstimatedDecodedBytes / 1024d:F1} KiB";
    public int LoadedSampleCount => _cache.GetSnapshot().LoadedSampleCount;
    public string ReferencedBy => BuildReferencedBy();
    public string RemovalEligibility => Pack.IsBuiltIn ? "Protected built-in pack" : Dependencies.CanRemoveUserPack ? "Can remove" : "Removal blocked by profile dependencies";
    public string ValidationIssues => Pack.Validation.Issues.Count == 0 ? "No validation issues." : string.Join(Environment.NewLine, Pack.Validation.Issues.Select(issue => $"{issue.Severity}: {issue.Code} — {issue.Message}"));
    public string MissingReferences => Dependencies.MissingReferencedPackIds.Count == 0 ? "No missing pack references." : string.Join(", ", Dependencies.MissingReferencedPackIds);
    public ObservableCollection<SoundSetRowViewModel> SoundSets { get; } = [];

    public void RefreshRuntimeFields()
    {
        Raise(nameof(LoadedSampleCount));
        Raise(nameof(ReferencedBy));
        Raise(nameof(RemovalEligibility));
    }

    private void BuildSoundSets()
    {
        SoundSets.Clear();
        if (Pack.Manifest is null || !Pack.Validation.IsValid) return;
        foreach (var set in Pack.Manifest.SoundSets)
        {
            var samples = Pack.Validation.Samples?.Where(item => string.Equals(item.SoundSetId, set.Id, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            SoundSets.Add(new(Pack.Manifest.Id, set, samples, _cache, _preview, _canOperate));
        }
    }

    private string BuildReferencedBy()
    {
        var parts = new List<string>();
        if (Dependencies.IsRequiredByActiveProfile) parts.Add("active global profile");
        if (Dependencies.IsRequiredByKeyboardAssignment) parts.Add($"{Dependencies.KeyboardAssignments.Count} keyboard assignment(s)");
        if (Dependencies.IsReferencedBySavedProfiles) parts.Add($"{Dependencies.Profiles.Count} saved profile(s)");
        return parts.Count == 0 ? "Not referenced by saved runtime data" : string.Join(", ", parts);
    }
}

public sealed class SoundPackDraftSampleViewModel(string path) : ObservableObject
{
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed class SoundPackDraftSetViewModel : ObservableObject
{
    private readonly SoundPackCreatorViewModel _owner;
    private readonly SoundPackDraftSet _set;

    public SoundPackDraftSetViewModel(SoundPackCreatorViewModel owner, SoundPackDraftSet set)
    {
        _owner = owner;
        _set = set;
        AddFilesCommand = new(async () => await _owner.AddFilesToSetAsync(this));
        RemoveSetCommand = new(() => _owner.RemoveSet(this), () => _owner.Sets.Count > 1);
    }

    public SoundPackDraftSet Model => _set;
    public string Id { get => _set.Id; set { _set.Id = value; Changed(); } }
    public string Name { get => _set.Name; set { _set.Name = value; Changed(); } }
    public int SampleCount => _set.Files.Count;
    public string FilesSummary => _set.Files.Count == 0 ? "Drop or add WAV files here." : string.Join(", ", _set.Files.Select(System.IO.Path.GetFileName));
    public ObservableCollection<SoundPackDraftSampleViewModel> Samples { get; } = [];
    public AsyncCommand AddFilesCommand { get; }
    public Task AddDroppedFilesAsync(IReadOnlyList<string> files) => _owner.AddFilesToSetAsync(this, files);
    public RelayCommand RemoveSetCommand { get; }

    public void RebuildSamples()
    {
        Samples.Clear();
        foreach (var file in _set.Files) Samples.Add(new(file));
        Raise(nameof(SampleCount));
        Raise(nameof(FilesSummary));
    }

    private void Changed()
    {
        Raise(nameof(Id));
        Raise(nameof(Name));
        _owner.MarkDirtyAndValidate();
    }
}

public sealed class SoundPackCreatorViewModel : ObservableObject
{
    private readonly SoundPackDraftService _drafts;
    private readonly ISoundPackFolderPicker _picker;
    private readonly ProfileManagementService _profiles;
    private readonly Func<Task> _afterSave;
    private SoundPackDraft? _draft;
    private string? _status;
    private bool _isEditing;
    private bool _confirmReferenceChanges;
    private bool _activateGeneratedProfile = true;

    public SoundPackCreatorViewModel(SoundPackDraftService drafts, ISoundPackFolderPicker picker, ProfileManagementService profiles, Func<Task> afterSave)
    {
        _drafts = drafts;
        _picker = picker;
        _profiles = profiles;
        _afterSave = afterSave;
        CreateCommand = new(StartCreate);
        EditCommand = new(async () => await Task.CompletedTask, () => false);
        AddSetCommand = new(AddSet, () => IsEditing);
        SaveCommand = new(async () => await SaveAsync(false), () => IsEditing);
        SaveAndCreateProfileCommand = new(async () => await SaveAsync(true), () => IsEditing);
        CancelCommand = new(Cancel, () => IsEditing);
    }

    public ObservableCollection<SoundPackDraftSetViewModel> Sets { get; } = [];
    public bool IsEditing { get => _isEditing; private set { if (Set(ref _isEditing, value)) RefreshCommands(); } }
    public string Title => _draft is null ? "Sound Pack Creator" : _draft.EditingExisting ? "Editing sound pack draft" : "Creating sound pack draft";
    public string PackId { get => _draft?.PackId ?? ""; set { if (_draft is null) return; _draft.PackId = value; MarkDirtyAndValidate(); Raise(); } }
    public string Name { get => _draft?.Name ?? ""; set { if (_draft is null) return; _draft.Name = value; MarkDirtyAndValidate(); Raise(); } }
    public string? Author { get => _draft?.Author; set { if (_draft is null) return; _draft.Author = value; MarkDirtyAndValidate(); Raise(); } }
    public string? Description { get => _draft?.Description; set { if (_draft is null) return; _draft.Description = value; MarkDirtyAndValidate(); Raise(); } }
    public bool ActivateGeneratedProfile { get => _activateGeneratedProfile; set => Set(ref _activateGeneratedProfile, value); }
    public string? Status { get => _status; private set => Set(ref _status, value); }
    public RelayCommand CreateCommand { get; }
    public AsyncCommand EditCommand { get; private set; }
    public RelayCommand AddSetCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public AsyncCommand SaveAndCreateProfileCommand { get; }
    public RelayCommand CancelCommand { get; }

    public void ConfigureEdit(Func<InstalledSoundPack?> selectedPack)
    {
        EditCommand = new(async () =>
        {
            var pack = selectedPack();
            if (pack is null) return;
            var draft = await _drafts.CreateFromInstalledAsync(pack);
            if (draft is null) { Status = "Built-in or invalid packs cannot be edited."; return; }
            LoadDraft(draft);
        }, () => !IsEditing && selectedPack() is { IsBuiltIn: false, Manifest: not null });
        Raise(nameof(EditCommand));
    }

    public async Task AddFilesToSetAsync(SoundPackDraftSetViewModel set)
    {
        var files = await _picker.PickSampleFilesAsync();
        await AddFilesToSetAsync(set, files);
    }

    public async Task AddFilesToSetAsync(SoundPackDraftSetViewModel set, IReadOnlyList<string> files)
    {
        if (_draft is null || files.Count == 0) return;
        var warnings = new List<string>();
        foreach (var file in files)
        {
            var issue = await _drafts.AddSampleAsync(_draft, set.Model, file);
            if (issue is not null) warnings.Add(issue.Message);
        }
        set.RebuildSamples();
        MarkDirtyAndValidate();
        Status = warnings.Count == 0 ? "Samples added to draft." : string.Join(Environment.NewLine, warnings.Distinct());
    }

    public void RemoveSet(SoundPackDraftSetViewModel set)
    {
        if (_draft is null || _draft.SoundSets.Count <= 1) return;
        _draft.SoundSets.Remove(set.Model);
        Sets.Remove(set);
        MarkDirtyAndValidate();
    }

    public void MarkDirtyAndValidate()
    {
        _confirmReferenceChanges = false;
        _ = ValidateStatusAsync();
    }

    private void StartCreate() => LoadDraft(_drafts.CreateNew());

    private void LoadDraft(SoundPackDraft draft)
    {
        _drafts.DeleteDraft(_draft);
        _draft = draft;
        Sets.Clear();
        foreach (var set in draft.SoundSets)
        {
            var vm = new SoundPackDraftSetViewModel(this, set);
            vm.RebuildSamples();
            Sets.Add(vm);
        }
        IsEditing = true;
        Status = "Draft ready. Add WAV files and save after validation passes.";
        RaiseAll();
        _ = ValidateStatusAsync();
    }

    private void AddSet()
    {
        if (_draft is null) return;
        var id = UniqueSetId();
        var set = new SoundPackDraftSet { Id = id, Name = "New Sound Set" };
        _draft.SoundSets.Add(set);
        var vm = new SoundPackDraftSetViewModel(this, set);
        Sets.Add(vm);
        MarkDirtyAndValidate();
    }

    private async Task SaveAsync(bool createProfile)
    {
        if (_draft is null) return;
        var result = await _drafts.SaveAsync(_draft, _confirmReferenceChanges);
        if (!result.Success)
        {
            if (result.ErrorCode == "ReferenceConfirmationRequired") _confirmReferenceChanges = true;
            Status = result.Message ?? "Pack save failed.";
            return;
        }

        if (createProfile && result.PackId is not null)
        {
            var profile = await _drafts.CreateProfileForPackAsync(result.PackId, $"{_draft.Name} Profile", _draft.SoundSets.Select(set => set.Id).ToArray());
            var profileResult = await _profiles.SaveGeneratedAsync(profile, ActivateGeneratedProfile);
            if (!profileResult.Success) { Status = "Pack saved, but profile creation failed: " + profileResult.Message; await _afterSave(); return; }
        }

        var savedId = result.PackId;
        _drafts.DeleteDraft(_draft);
        _draft = null;
        Sets.Clear();
        IsEditing = false;
        Status = createProfile ? $"Pack '{savedId}' saved and profile created." : $"Pack '{savedId}' saved.";
        RaiseAll();
        await _afterSave();
    }

    private void Cancel()
    {
        _drafts.DeleteDraft(_draft);
        _draft = null;
        Sets.Clear();
        IsEditing = false;
        Status = "Draft canceled.";
        RaiseAll();
    }

    private async Task ValidateStatusAsync()
    {
        if (_draft is null) return;
        var validation = await _drafts.ValidateAsync(_draft);
        if (validation.IsValid)
        {
            Status = validation.RequiresReferenceConfirmation
                ? "Valid draft. Saving will remove sound sets referenced by profiles; press Save once to review, then again to confirm."
                : "Draft is valid.";
        }
        else Status = string.Join(Environment.NewLine, validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}").Distinct().Take(6));
        SaveCommand.Refresh();
        SaveAndCreateProfileCommand.Refresh();
    }

    private string UniqueSetId()
    {
        var index = 1;
        string id;
        do id = $"new-set-{index++}";
        while (_draft?.SoundSets.Any(set => string.Equals(set.Id, id, StringComparison.OrdinalIgnoreCase)) == true);
        return id;
    }

    private void RefreshCommands()
    {
        AddSetCommand.Refresh();
        SaveCommand.Refresh();
        SaveAndCreateProfileCommand.Refresh();
        CancelCommand.Refresh();
        EditCommand.Refresh();
    }

    private void RaiseAll()
    {
        Raise(nameof(Title));
        Raise(nameof(PackId));
        Raise(nameof(Name));
        Raise(nameof(Author));
        Raise(nameof(Description));
        Raise(nameof(IsEditing));
    }
}

public sealed class SoundPacksViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SoundPackCatalogService _catalog;
    private readonly SoundPackValidationService _validator;
    private readonly SoundPackImportService _importer;
    private readonly Phase2SettingsService _settings;
    private readonly SoundPreviewService _preview;
    private readonly ISoundSampleCache _cache;
    private readonly ISoundPackFolderPicker _picker;
    private readonly ISoundPackConflictResolver _conflicts;
    private readonly SoundPackDependencyService _dependencies;
    private readonly StorageCleanupService _cleanup;
    private readonly SoundPackDraftService _drafts;
    private readonly ProfileManagementService _profileManagement;
    private readonly CancellationTokenSource _lifetime = new();
    private SoundPackRowViewModel? _selected;
    private OperationStatus? _status;
    private bool _shutdown;

    public SoundPacksViewModel(SoundPackCatalogService catalog, SoundPackValidationService validator, SoundPackImportService importer, Phase2SettingsService settings,
        SoundPreviewService preview, ISoundSampleCache cache, ISoundPackFolderPicker picker, ISoundPackConflictResolver conflicts, SoundPackDependencyService dependencies, StorageCleanupService cleanup,
        SoundPackDraftService drafts, ProfileManagementService profileManagement)
    {
        _catalog = catalog;
        _validator = validator;
        _importer = importer;
        _settings = settings;
        _preview = preview;
        _cache = cache;
        _picker = picker;
        _conflicts = conflicts;
        _dependencies = dependencies;
        _cleanup = cleanup;
        _drafts = drafts;
        _profileManagement = profileManagement;
        RefreshCommand = Command(() => RefreshAsync());
        ImportCommand = Command(ImportAsync);
        RemoveCommand = Command(RemoveAsync, () => Selected is not null && Selected.Pack.Manifest is not null && !Selected.Pack.IsBuiltIn);
        CleanupCommand = Command(CleanupAsync);
        Creator = new(_drafts, _picker, _profileManagement, async () => { await _settings.ReloadProfilesAsync(_lifetime.Token); await RefreshAsync(_lifetime.Token); });
        Creator.ConfigureEdit(() => Selected?.Pack);
        _settings.Changed += OnExternalChanged;
    }

    public ObservableCollection<SoundPackRowViewModel> Packs { get; } = [];
    public SoundPackRowViewModel? Selected { get => _selected; set { Set(ref _selected, value); RemoveCommand.Refresh(); Raise(nameof(SelectedDetails)); } }
    public string SelectedDetails => Selected is null ? "Select a sound pack to inspect details." : $"{Selected.Name} · {Selected.ValidationStatus} · {Selected.ReferencedBy}";
    public OperationStatus? Status { get => _status; private set { Set(ref _status, value); Raise(nameof(StatusText)); } }
    public string? StatusText => Status?.ToString();
    public bool IsBusy => Commands.Any(command => command.IsRunning);
    public string Summary => $"{Packs.Count} installed pack(s), {Packs.Count(pack => !pack.Pack.Validation.IsValid)} invalid, {_cache.GetSnapshot().LoadedSampleCount} loaded sample(s), {_cache.GetSnapshot().DecodedBytes / 1024d:F1} KiB decoded";
    public string MissingPackReferences => _dependencies.GetMissingReferencedPackIds().Count == 0 ? "No missing referenced packs." : "Missing referenced packs: " + string.Join(", ", _dependencies.GetMissingReferencedPackIds());
    public SoundPackCreatorViewModel Creator { get; }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand CleanupCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, ImportCommand, RemoveCommand, CleanupCommand];
    private AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(async () => { if (_shutdown) return; Status = null; await action(); RaiseBusyAndSummary(); }, can, _ => Status = OperationStatus.Error("OperationFailed", "Operation failed. Check the selected item and try again."));

    public async Task InitializeAsync(CancellationToken token = default) => await RefreshAsync(token);

    public async Task RefreshAsync(CancellationToken token = default)
    {
        await _settings.RefreshPacksAsync(token);
        Rebuild(Selected?.Id);
        Status ??= OperationStatus.Info("CatalogRefreshed", "Sound-pack catalog refreshed.");
    }

    private async Task ImportAsync()
    {
        var token = _lifetime.Token;
        var folder = await _picker.PickImportFolderAsync(token);
        if (_shutdown) return;
        if (string.IsNullOrWhiteSpace(folder)) { Status = OperationStatus.Info("ImportCanceled", "Sound-pack import canceled."); return; }
        var validation = await _validator.ValidateAsync(folder, token);
        if (!validation.IsValid || validation.Manifest is null)
        {
            Status = OperationStatus.Error(validation.Issues.FirstOrDefault()?.Code ?? "ValidationFailed", "Sound-pack validation failed.", string.Join(Environment.NewLine, validation.Issues.Select(issue => issue.Message)));
            return;
        }

        var policy = SoundPackConflictPolicy.Cancel;
        if (_catalog.TryGetPack(validation.Manifest.Id, out var existing))
        {
            if (existing.IsBuiltIn)
            {
                Status = OperationStatus.Error("BuiltInProtected", "The built-in sound pack cannot be replaced.");
                return;
            }
            policy = await _conflicts.ResolveDuplicateAsync(existing, token);
            if (_shutdown) return;
            if (policy == SoundPackConflictPolicy.Cancel) { Status = OperationStatus.Info("ImportCanceled", "Sound-pack replacement canceled."); return; }
        }

        var result = await _importer.ImportFolderAsync(folder, policy, token);
        if (!result.Success)
        {
            Status = OperationStatus.Error(result.ErrorCode ?? "ImportFailed", result.Message ?? "Sound-pack import failed.");
            await RefreshAsync(token);
            return;
        }

        await _settings.ReloadProfilesAsync(token);
        Rebuild(result.PackId);
        Status = OperationStatus.Info("ImportCompleted", "Sound pack imported.");
    }

    private async Task RemoveAsync()
    {
        if (Selected?.Pack.Manifest is null) return;
        var pack = Selected;
        var dependencies = _dependencies.GetPackDependencies(pack.Pack.Manifest.Id);
        if (pack.Pack.IsBuiltIn || !dependencies.CanRemoveUserPack)
        {
            Status = OperationStatus.Warning("RemovalBlocked", "Sound pack removal is blocked.", BuildRemovalBlockDetails(pack, dependencies));
            return;
        }
        var currentIndex = Packs.IndexOf(pack);
        var token = _lifetime.Token;
        var result = await _importer.RemoveAsync(pack.Pack.Manifest.Id, token);
        if (!result.Success)
        {
            Status = OperationStatus.Warning(result.ErrorCode ?? "RemovalBlocked", result.Message ?? "Sound-pack removal failed.", string.Join(", ", result.ReferencingProfileIds));
            await RefreshAsync(token);
            return;
        }
        Rebuild(Packs.ElementAtOrDefault(Math.Max(0, currentIndex - 1))?.Id);
        Status = OperationStatus.Info("RemovalCompleted", "Sound pack removed.");
    }

    private async Task CleanupAsync()
    {
        var token = _lifetime.Token;
        var result = await _cleanup.CleanupAsync(token);
        await RefreshAsync(token);
        Status = OperationStatus.Info("CleanupCompleted", "Storage cleanup completed.", result.ToString());
    }

    public void Rebuild(string? selectId = null)
    {
        selectId ??= Selected?.Id;
        Packs.Clear();
        foreach (var pack in _catalog.Packs.OrderByDescending(pack => pack.IsBuiltIn).ThenBy(pack => pack.Manifest?.Name ?? pack.RootDirectory, StringComparer.CurrentCultureIgnoreCase))
        {
            var id = pack.Manifest?.Id ?? $"invalid:{Path.GetFileName(pack.RootDirectory)}";
            Packs.Add(new(pack, _dependencies.GetPackDependencies(id), _cache, _preview, () => !_shutdown && !IsBusy));
        }
        Selected = Packs.FirstOrDefault(pack => string.Equals(pack.Id, selectId, StringComparison.OrdinalIgnoreCase)) ?? Packs.FirstOrDefault();
        Raise(nameof(Summary));
        Raise(nameof(MissingPackReferences));
        Creator.EditCommand.Refresh();
        RaiseBusyAndSummary();
    }

    private void OnExternalChanged(object? sender, EventArgs args)
    {
        foreach (var pack in Packs) pack.RefreshRuntimeFields();
        Raise(nameof(Summary));
        Raise(nameof(MissingPackReferences));
    }

    private static string BuildRemovalBlockDetails(SoundPackRowViewModel pack, SoundPackDependencySummary dependencies)
    {
        if (pack.Pack.IsBuiltIn) return "Built-in fallback sound packs are protected.";
        var lines = new List<string>();
        lines.AddRange(dependencies.Profiles.Select(profile => $"{(profile.IsActive ? "Active" : "Saved")} profile: {profile.ProfileName} ({profile.ProfileId})"));
        lines.AddRange(dependencies.KeyboardAssignments.Select(item => $"Keyboard assignment {item.KeyboardId}: {item.ProfileName} ({item.ProfileId})"));
        lines.Add("Remediation: activate/edit/delete referencing profiles or clear keyboard assignments before removing this pack.");
        return string.Join(Environment.NewLine, lines);
    }

    private void RaiseBusyAndSummary()
    {
        Raise(nameof(IsBusy));
        Raise(nameof(Summary));
        foreach (var command in Commands) command.Refresh();
        Creator.EditCommand.Refresh();
        foreach (var set in Packs.SelectMany(pack => pack.SoundSets)) set.PreviewCommand.Refresh();
    }

    public ValueTask DisposeAsync()
    {
        _shutdown = true;
        _lifetime.Cancel();
        Creator.CancelCommand.Execute(null);
        _settings.Changed -= OnExternalChanged;
        _lifetime.Dispose();
        return ValueTask.CompletedTask;
    }
}
