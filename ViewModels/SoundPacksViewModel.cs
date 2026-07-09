using System.Collections.ObjectModel;
using KeySounds.Application.Settings;
using KeySounds.Application.SoundPacks;
using KeySounds.Audio;
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
    private SoundPackRowViewModel? _selected;
    private OperationStatus? _status;
    private bool _shutdown;

    public SoundPacksViewModel(SoundPackCatalogService catalog, SoundPackValidationService validator, SoundPackImportService importer, Phase2SettingsService settings,
        SoundPreviewService preview, ISoundSampleCache cache, ISoundPackFolderPicker picker, ISoundPackConflictResolver conflicts, SoundPackDependencyService dependencies, StorageCleanupService cleanup)
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
        RefreshCommand = Command(() => RefreshAsync());
        ImportCommand = Command(ImportAsync);
        RemoveCommand = Command(RemoveAsync, () => Selected is not null && Selected.Pack.Manifest is not null && !Selected.Pack.IsBuiltIn);
        CleanupCommand = Command(CleanupAsync);
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
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand CleanupCommand { get; }
    private IEnumerable<AsyncCommand> Commands => [RefreshCommand, ImportCommand, RemoveCommand, CleanupCommand];
    private AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(async () => { if (_shutdown) return; Status = null; await action(); RaiseBusyAndSummary(); }, can, ex => Status = OperationStatus.Error("OperationFailed", "Operation failed.", ex.Message));

    public async Task InitializeAsync(CancellationToken token = default) => await RefreshAsync(token);

    public async Task RefreshAsync(CancellationToken token = default)
    {
        await _settings.RefreshPacksAsync(token);
        Rebuild(Selected?.Id);
        Status ??= OperationStatus.Info("CatalogRefreshed", "Sound-pack catalog refreshed.");
    }

    private async Task ImportAsync()
    {
        var folder = await _picker.PickImportFolderAsync();
        if (string.IsNullOrWhiteSpace(folder)) { Status = OperationStatus.Info("ImportCanceled", "Sound-pack import canceled."); return; }
        var validation = await _validator.ValidateAsync(folder);
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
            policy = await _conflicts.ResolveDuplicateAsync(existing);
            if (policy == SoundPackConflictPolicy.Cancel) { Status = OperationStatus.Info("ImportCanceled", "Sound-pack replacement canceled."); return; }
        }

        var result = await _importer.ImportFolderAsync(folder, policy);
        if (!result.Success)
        {
            Status = OperationStatus.Error(result.ErrorCode ?? "ImportFailed", result.Message ?? "Sound-pack import failed.");
            await RefreshAsync();
            return;
        }

        await _settings.ReloadProfilesAsync();
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
        var result = await _importer.RemoveAsync(pack.Pack.Manifest.Id);
        if (!result.Success)
        {
            Status = OperationStatus.Warning(result.ErrorCode ?? "RemovalBlocked", result.Message ?? "Sound-pack removal failed.", string.Join(", ", result.ReferencingProfileIds));
            await RefreshAsync();
            return;
        }
        Rebuild(Packs.ElementAtOrDefault(Math.Max(0, currentIndex - 1))?.Id);
        Status = OperationStatus.Info("RemovalCompleted", "Sound pack removed.");
    }

    private async Task CleanupAsync()
    {
        var result = await _cleanup.CleanupAsync();
        await RefreshAsync();
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
        foreach (var set in Packs.SelectMany(pack => pack.SoundSets)) set.PreviewCommand.Refresh();
    }

    public ValueTask DisposeAsync()
    {
        _shutdown = true;
        _settings.Changed -= OnExternalChanged;
        return ValueTask.CompletedTask;
    }
}
