using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace JackBean.App;

/// <summary>
/// 화면 글자 크기 배율(<see cref="Current"/>)을 글자가 아닌 것들에도 적용한다: 아이콘 크기, 버튼·행 높이.
/// 컨트롤에 <c>UiScale.Enabled</c>를 켜면 그 컨트롤이 처음 가졌던 Width·Height·MinHeight를 기억해 두고, 배율이 바뀔 때마다
/// 기억한 값 × 배율로 다시 정한다 (XAML의 고정 숫자나 스타일 값을 그대로 두고 켜기만 하면 된다).
/// 배율은 <see cref="ThemeService"/>가 설정에서 읽어 <c>UiScale</c> 리소스와 이 클래스에 내려 준다.
/// </summary>
public static class UiScale
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("Enabled");

    public static bool GetEnabled(Control c) => c.GetValue(EnabledProperty);
    public static void SetEnabled(Control c, bool value) => c.SetValue(EnabledProperty, value);

    /// <summary>지금 배율 (1 = 100%).</summary>
    public static double Current { get; private set; } = 1;

    /// <summary>배율이 바뀌었다 (코드로 크기를 계산하는 곳이 다시 그리는 데 쓴다).</summary>
    public static event Action? Changed;

    internal static void Set(double scale)
    {
        if (scale == Current) return;
        Current = scale;
        foreach (var state in States.ToArray()) state.Apply();
        Changed?.Invoke();
    }

    /// <summary>크기를 배율에 맞춘다: <paramref name="size"/> × 배율, 반올림.</summary>
    public static double Scaled(double size) => Math.Round(size * Current);

    /// <summary>코드로 만든 컨트롤에 켠다 (XAML은 <c>local:UiScale.Enabled="True"</c>).</summary>
    public static T Enable<T>(T control) where T : Control
    {
        SetEnabled(control, true);
        return control;
    }

    static readonly ConditionalWeakTable<Control, State> Table = new();
    static readonly List<State> States = [];

    static UiScale()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            if (e.NewValue is true && !Table.TryGetValue(c, out _))
            {
                var state = new State(c);
                Table.Add(c, state);
                States.Add(state);
                // 스타일의 Width·Height가 모두 적용된 뒤(= 트리에 붙은 뒤)에 처음 값을 기억한다.
                if (c.IsAttachedToVisualTree()) state.Apply();
                else c.AttachedToVisualTree += (_, _) => state.Apply();
            }
        });
    }

    sealed class State
    {
        readonly WeakReference<Control> _control;
        bool _captured;
        double _width = double.NaN, _height = double.NaN, _minHeight;

        public State(Control control) => _control = new(control);

        public void Apply()
        {
            if (!_control.TryGetTarget(out var c)) { States.Remove(this); return; }
            if (!_captured && !c.IsAttachedToVisualTree()) return; // 스타일이 다 적용된 뒤에 처음 값을 기억한다
            if (!_captured)
            {
                _width = c.Width; _height = c.Height; _minHeight = c.MinHeight;
                _captured = true;
            }
            if (double.IsFinite(_width)) c.Width = Scaled(_width);
            if (double.IsFinite(_height)) c.Height = Scaled(_height);
            if (_minHeight > 0) c.MinHeight = Scaled(_minHeight);
        }
    }
}
