namespace KeySounds.Core;

public enum OperationStatusSeverity { Info, Warning, Error }

public sealed record OperationStatus(OperationStatusSeverity Severity, string Code, string Message, string? Details = null)
{
    public static OperationStatus Info(string code, string message, string? details = null) => new(OperationStatusSeverity.Info, code, message, details);
    public static OperationStatus Warning(string code, string message, string? details = null) => new(OperationStatusSeverity.Warning, code, message, details);
    public static OperationStatus Error(string code, string message, string? details = null) => new(OperationStatusSeverity.Error, code, message, details);
    public override string ToString() => string.IsNullOrWhiteSpace(Details) ? Message : $"{Message}{Environment.NewLine}{Details}";
}
