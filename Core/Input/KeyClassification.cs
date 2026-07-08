using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeySounds.Core.Input;

public enum KeySoundGroup { Alpha, NumberRow, Modifier, Spacebar, Enter, Backspace, Tab, Navigation, Arrow, Function, Numpad, Escape, Other }

[JsonConverter(typeof(KeyIdentifierJsonConverter))]
public readonly record struct KeyIdentifier(ushort ScanCode, bool IsExtended);

public sealed class KeyIdentifierJsonConverter : JsonConverter<KeyIdentifier>
{
    public override KeyIdentifier Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());
    public override void Write(Utf8JsonWriter writer, KeyIdentifier value, JsonSerializerOptions options) => writer.WriteStringValue(Format(value));
    public override KeyIdentifier ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Parse(reader.GetString());
    public override void WriteAsPropertyName(Utf8JsonWriter writer, KeyIdentifier value, JsonSerializerOptions options) => writer.WritePropertyName(Format(value));
    private static string Format(KeyIdentifier value) => $"{value.ScanCode:X4}:{(value.IsExtended ? 1 : 0)}";
    private static KeyIdentifier Parse(string? text)
    {
        var parts = text?.Split(':');
        if (parts is not { Length: 2 } || !ushort.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out var scan) || parts[1] is not ("0" or "1"))
            throw new JsonException("Invalid physical key identifier.");
        return new KeyIdentifier(scan, parts[1] == "1");
    }
}
