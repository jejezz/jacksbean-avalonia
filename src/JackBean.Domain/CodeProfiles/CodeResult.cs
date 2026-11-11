namespace JackBean.Domain.CodeProfiles;

/// <summary>실패 사유. <see cref="Line"/> 은 규칙 파일·설정 파일처럼 줄 번호가 의미 있을 때만 채운다(1부터).</summary>
public sealed record CodeError(string Message, int? Line = null)
{
    public override string ToString() => Line == null ? Message : $"줄 {Line}: {Message}";
}

/// <summary>성공하면 <see cref="Value"/>, 실패하면 <see cref="Error"/>. (예외 대신 값으로 알린다.)</summary>
public sealed record CodeResult<T>(T? Value, CodeError? Error)
{
    public bool Ok => Error == null;

    public static CodeResult<T> Success(T value) => new(value, null);

    public static CodeResult<T> Fail(string message, int? line = null) => new(default, new CodeError(message, line));

    public static CodeResult<T> Fail(CodeError error) => new(default, error);
}
