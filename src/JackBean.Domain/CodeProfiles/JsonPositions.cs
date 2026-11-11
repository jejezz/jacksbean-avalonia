using System.Text;
using System.Text.Json;

namespace JackBean.Domain.CodeProfiles;

/// <summary>
/// JSON 텍스트에서 값마다 "경로 → 줄 번호"를 모은다(오류 메시지에 줄 번호를 붙이려는 것 — <see cref="System.Text.Json.Nodes.JsonNode"/> 는 위치를 기억하지 않는다).
/// 경로는 `types[2].match.any[0]` 모양이고 루트는 빈 문자열이다. 주석은 건너뛴다.
/// </summary>
public sealed class JsonPositions
{
    private readonly Dictionary<string, int> _lines;

    private JsonPositions(Dictionary<string, int> lines) => _lines = lines;

    /// <summary>그 경로의 값이 시작하는 줄(1부터). 모르면 null.</summary>
    public int? LineOf(string path) => _lines.TryGetValue(path, out var line) ? line : null;

    public static JsonPositions Build(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var bytes = Encoding.UTF8.GetBytes(text);
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == (byte)'\n') lineStarts.Add(i + 1);

        var lines = new Dictionary<string, int>();
        var frames = new List<Frame>();
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        frames[^1].Property = reader.GetString();
                        continue;
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        frames.RemoveAt(frames.Count - 1);
                        continue;
                }

                var path = NextPath(frames);
                var index = lineStarts.BinarySearch((int)reader.TokenStartIndex);
                lines[path] = (index >= 0 ? index : ~index - 1) + 1;
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    frames.Add(new Frame(reader.TokenType == JsonTokenType.StartArray, path));
            }
        }
        catch (JsonException)
        {
            // 문법 오류는 Jsonc.Parse 가 이미 알렸다. 여기서는 읽은 데까지만 쓴다.
        }
        return new JsonPositions(lines);
    }

    private static string NextPath(List<Frame> frames)
    {
        if (frames.Count == 0) return "";
        var top = frames[^1];
        if (top.IsArray) return $"{top.Path}[{top.Index++}]";
        return top.Path.Length == 0 ? top.Property! : $"{top.Path}.{top.Property}";
    }

    private sealed class Frame(bool isArray, string path)
    {
        public bool IsArray { get; } = isArray;
        public string Path { get; } = path;
        public int Index { get; set; }
        public string? Property { get; set; }
    }
}
