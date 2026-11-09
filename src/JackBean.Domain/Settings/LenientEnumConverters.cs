using System.Text.Json;
using System.Text.Json.Serialization;
using JackBean.Domain.Theme;

namespace JackBean.Domain.Settings;

/// <summary>
/// 설정 파일의 열거형 값을 소문자 이름(<c>"dark"</c>)으로 읽고 쓴다. 모르는 값(다른 버전이 쓴 파일)은 예외 없이
/// 기본값(첫 항목)으로 읽는다 — Kotlin 판의 <c>coerceInputValues</c>. 소스 생성기와 NativeAOT에서 동작한다(리플렉션 없음).
/// </summary>
public abstract class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    readonly Dictionary<string, T> _byName;
    readonly Dictionary<T, string> _byValue;

    protected LenientEnumConverter(params (string Name, T Value)[] names)
    {
        _byName = names.ToDictionary(n => n.Name, n => n.Value);
        _byValue = names.ToDictionary(n => n.Value, n => n.Name);
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } s && _byName.TryGetValue(s, out var value))
            return value;
        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(_byValue.TryGetValue(value, out var name) ? name : _byValue[default]);
}

public sealed class ThemeModeConverter() : LenientEnumConverter<ThemeMode>(
    ("system", ThemeMode.System), ("light", ThemeMode.Light), ("dark", ThemeMode.Dark));

public sealed class ColorPaletteConverter() : LenientEnumConverter<ColorPalette>(
    ("saturn", ColorPalette.Saturn), ("teal", ColorPalette.Teal), ("amber", ColorPalette.Amber),
    ("rose", ColorPalette.Rose), ("slate", ColorPalette.Slate), ("highContrast", ColorPalette.HighContrast));

/// <summary>언어는 비어 있을 수 있다(null = 시스템). 모르는 값도 null(시스템)로 읽는다.</summary>
public sealed class AppLocaleConverter : JsonConverter<AppLocale?>
{
    public override AppLocale? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }
        return reader.GetString() switch
        {
            "ko" => AppLocale.Korean,
            "en" => AppLocale.English,
            _ => null,
        };
    }

    public override void Write(Utf8JsonWriter writer, AppLocale? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value == AppLocale.Korean ? "ko" : "en");
    }
}
