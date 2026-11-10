using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace JackBean.Services.GitHub;

/// <summary>
/// 액세스 토큰은 OS 의 보안 저장소에만 둔다 — 설정 파일·로그·정책 파일에 남기지 않는다.
/// Windows 는 자격 증명 관리자, macOS 는 키체인, Linux 는 libsecret(`secret-tool`). 쓸 수 없는 환경은 이번 실행 동안만 메모리에 둔다.
/// </summary>
public interface ITokenStore
{
    /// <summary>다음 실행에도 남는다(OS 보안 저장소). false 면 이번 실행에서만 쓴다.</summary>
    bool IsPersistent { get; }

    string? Read();
    void Write(string token);
    void Delete();
}

public static class TokenStores
{
    /// <param name="name">저장 항목 이름. 시험은 진짜 로그인 토큰을 지우지 않게 다른 이름을 쓴다.</param>
    public static ITokenStore Create(string name = "JacksBean GitHub token")
    {
        if (OperatingSystem.IsWindows()) return new WindowsCredentialStore(name);
        if (OperatingSystem.IsMacOS()) return new MacKeychainStore(name);
        if (OperatingSystem.IsLinux() && SecretToolStore.IsAvailable()) return new SecretToolStore(name);
        return new MemoryTokenStore();
    }
}

/// <summary>저장하지 않고 메모리에만 둔다(시험, 그리고 보안 저장소를 쓸 수 없는 환경).</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    private string? _token;
    public bool IsPersistent => false;
    public string? Read() => _token;
    public void Write(string token) => _token = token;
    public void Delete() => _token = null;
}

/// <summary>Windows 자격 증명 관리자(`CredWrite`/`CredRead`). 현재 사용자 로그인에 묶인다.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCredentialStore(string target) : ITokenStore
{
    private const uint CRED_TYPE_GENERIC = 1, CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct CREDENTIAL
    {
        public uint Flags, Type;
        public nint TargetName, Comment;
        public uint LastWrittenLow, LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist, AttributeCount;
        public nint Attributes, TargetAlias, UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref CREDENTIAL credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(nint buffer);

    public bool IsPersistent => true;

    public string? Read()
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlob == 0 || cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally { CredFree(ptr); }
    }

    public void Write(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        var targetPtr = Marshal.StringToCoTaskMemUni(target);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC, TargetName = targetPtr, CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
            };
            if (!CredWrite(ref cred, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            Marshal.FreeCoTaskMem(blob);
            Marshal.FreeCoTaskMem(targetPtr);
        }
    }

    public void Delete() => CredDelete(target, CRED_TYPE_GENERIC, 0);   // 없으면 그냥 실패한다 — 괜찮다
}

/// <summary>macOS 키체인(`security` 명령). 토큰이 잠깐 명령 인자로 넘어간다는 한계가 있다(같은 사용자의 프로세스 목록에서만 보인다).</summary>
internal sealed class MacKeychainStore(string service) : ITokenStore
{
    private const string Account = "github";
    public bool IsPersistent => true;

    public string? Read()
    {
        var r = ProcessUtil.Run("security", ["find-generic-password", "-a", Account, "-s", service, "-w"]);
        return r.ExitCode == 0 ? r.Output.TrimEnd('\r', '\n') : null;
    }

    public void Write(string token)
    {
        var r = ProcessUtil.Run("security", ["add-generic-password", "-a", Account, "-s", service, "-w", token, "-U"]);
        if (r.ExitCode != 0) throw new IOException($"키체인에 저장하지 못했습니다: {r.Error.Trim()}");
    }

    public void Delete() => ProcessUtil.Run("security", ["delete-generic-password", "-a", Account, "-s", service]);
}

/// <summary>Linux libsecret(`secret-tool`). 토큰은 표준 입력으로 넘기므로 명령 인자에 보이지 않는다.</summary>
internal sealed class SecretToolStore(string service) : ITokenStore
{
    public bool IsPersistent => true;

    public static bool IsAvailable() => ProcessUtil.Run("secret-tool", ["--version"]).ExitCode >= 0 && ProcessUtil.Exists("secret-tool");

    public string? Read()
    {
        var r = ProcessUtil.Run("secret-tool", ["lookup", "service", service, "account", "github"]);
        return r.ExitCode == 0 && r.Output.Length > 0 ? r.Output.TrimEnd('\r', '\n') : null;
    }

    public void Write(string token)
    {
        var r = ProcessUtil.Run("secret-tool", ["store", "--label=Jack's Bean GitHub token", "service", service, "account", "github"], stdin: token);
        if (r.ExitCode != 0) throw new IOException($"보안 저장소에 저장하지 못했습니다: {r.Error.Trim()}");
    }

    public void Delete() => ProcessUtil.Run("secret-tool", ["clear", "service", service, "account", "github"]);
}

internal static class ProcessUtil
{
    public sealed record Result(int ExitCode, string Output, string Error);

    /// <summary>명령을 실행해 끝날 때까지 기다린다. 명령이 없으면 종료 코드 -1.</summary>
    public static Result Run(string file, string[] args, string? stdin = null)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (stdin != null) p.StandardInput.Write(stdin);
            p.StandardInput.Close();
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            return new Result(p.ExitCode, output, error);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { return new Result(-1, "", e.Message); }
    }

    public static bool Exists(string file) => Run(file, ["--version"]).ExitCode != -1;
}
