using System.Text.RegularExpressions;

namespace JackBean.Domain;

/// <summary>
/// 릴리스 서버(introduce-public-repos의 src/update.js)와 같은 SemVer 부분집합: <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2.3-rc.1</c>, <c>1.2.3+4</c>.
/// 빌드 메타데이터(<c>+4</c>)는 비교에서 무시한다. Flutter 앱의 app_updater/version.dart와 같은 규칙.
/// </summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    SemanticVersion(int major, int minor, int patch, object[] preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>프리릴리스 식별자들. 각각 <see cref="int"/> 또는 <see cref="string"/>.</summary>
    public IReadOnlyList<object> PreRelease { get; }

    public bool IsPreRelease => PreRelease.Count > 0;

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();

    /// <summary>버전이 아니면 null.</summary>
    public static SemanticVersion? TryParse(string? text)
    {
        if (text is null) return null;
        var m = Pattern().Match(text.Trim());
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups[1].Value, out var major) || !int.TryParse(m.Groups[2].Value, out var minor)
            || !int.TryParse(m.Groups[3].Value, out var patch)) return null;
        object[] pre = m.Groups[4].Success
            ? m.Groups[4].Value.Split('.').Select(id => int.TryParse(id, out var n) && id.All(char.IsAsciiDigit) ? (object)n : id).ToArray()
            : [];
        return new SemanticVersion(major, minor, patch, pre);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null) return 1;
        foreach (var c in new[] { Major.CompareTo(other.Major), Minor.CompareTo(other.Minor), Patch.CompareTo(other.Patch) })
            if (c != 0) return c;
        // 정식 버전은 같은 숫자의 프리릴리스보다 새롭다.
        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
        {
            if (PreRelease.Count == 0 && other.PreRelease.Count == 0) return 0;
            return PreRelease.Count == 0 ? 1 : -1;
        }
        var n = Math.Max(PreRelease.Count, other.PreRelease.Count);
        for (var i = 0; i < n; i++)
        {
            if (i >= PreRelease.Count) return -1;
            if (i >= other.PreRelease.Count) return 1;
            var a = PreRelease[i];
            var b = other.PreRelease[i];
            if (a.Equals(b)) continue;
            if (a is int x && b is int y) return x.CompareTo(y);
            if (a is int) return -1; // 숫자 식별자가 문자보다 앞
            if (b is int) return 1;
            return string.CompareOrdinal((string)a, (string)b) < 0 ? -1 : 1;
        }
        return 0;
    }

    public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, string.Join('.', PreRelease));

    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;

    /// <summary>빌드 메타데이터 없는 표준 표기: <c>1.2.3</c> 또는 <c>1.2.3-rc.1</c>.</summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}{(PreRelease.Count == 0 ? "" : "-" + string.Join('.', PreRelease))}";
}
