using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JackBean.Domain.CodeProfiles;

/// <summary>JSONC 를 읽은 결과. 내용이 비어 있으면(공백·주석뿐) <see cref="Value"/> 가 null.</summary>
/// <param name="HadComments">`//` 또는 `/* */` 주석이 하나라도 있었는지. 다시 쓰면 주석이 사라진다.</param>
public sealed record JsoncDocument(JsonNode? Value, bool HadComments);

/// <summary>
/// VS Code 설정 파일(JSONC: 주석과 끝 쉼표 허용)과 규칙 파일을 읽는다.
/// 키 순서는 파일 그대로 보존된다(<see cref="JsonObject"/>).
/// </summary>
public static class Jsonc
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Allow,
        AllowTrailingCommas = true,
    };

    public static CodeResult<JsoncDocument> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];   // BOM
        if (string.IsNullOrWhiteSpace(text)) return CodeResult<JsoncDocument>.Success(new JsoncDocument(null, false));   // 리더는 토큰이 없으면 예외를 던진다
        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            // 먼저 토큰을 끝까지 읽어 문법을 확인하고 주석·내용 유무를 본다(주석 처리 방식을 바꿔 가며 두 번 읽는다).
            var hadComments = false;
            var anyValue = false;
            var reader = new Utf8JsonReader(bytes, ReaderOptions);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.Comment) hadComments = true;
                else anyValue = true;
            }
            if (!anyValue) return CodeResult<JsoncDocument>.Success(new JsoncDocument(null, hadComments));
            var node = JsonNode.Parse(bytes, nodeOptions: null, DocumentOptions);
            return CodeResult<JsoncDocument>.Success(new JsoncDocument(node, hadComments));
        }
        catch (JsonException e)
        {
            return CodeResult<JsoncDocument>.Fail($"JSON 문법 오류: {e.Message}", e.LineNumber is { } n ? (int)n + 1 : null);
        }
    }
}
