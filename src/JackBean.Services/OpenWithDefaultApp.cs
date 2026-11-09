using System.Diagnostics;

namespace JackBean.Services;

/// <summary>파일이나 주소를 열지 못했다.</summary>
public sealed class FileOperationException(string message) : Exception(message);

/// <summary>파일·주소를 OS가 연결해 둔 기본 앱으로 연다 (탐색기/Finder에서 더블클릭한 것과 같다).</summary>
public sealed class OpenWithDefaultApp
{
    public void Open(string pathOrUrl)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                using (Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true })) { }
            else if (OperatingSystem.IsMacOS())
                using (Process.Start("open", [pathOrUrl])) { }
            else
                using (Process.Start("xdg-open", [pathOrUrl])) { }
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new FileOperationException(e.Message);
        }
    }
}
