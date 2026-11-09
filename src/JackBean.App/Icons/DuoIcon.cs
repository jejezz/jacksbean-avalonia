using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace JackBean.App.Icons;

/// <summary>
/// 투톤(duotone) 아이콘: 한 색을 옅게 깐 채움 층 위에 같은 색 윤곽을 그린다 (Phosphor Icons). 벡터라 어떤 크기·배율에서도 또렷하고,
/// 색은 <see cref="Foreground"/>(없으면 부모의 글자색) 하나뿐이라 라이트·다크·팔레트 변경을 그대로 따라간다.
/// <see cref="ColorKey"/>를 주면 그 이름의 앱 색 자원을 따라간다 (테마가 바뀌면 같이 바뀐다).
/// </summary>
public sealed class DuoIcon : Control
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<DuoIcon, string?>(nameof(Icon));
    public static readonly StyledProperty<string?> ColorKeyProperty = AvaloniaProperty.Register<DuoIcon, string?>(nameof(ColorKey));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<DuoIcon>();

    const double FillOpacity = 0.28;
    const double DisabledOpacity = 0.4;
    const double Grid = 256;

    static readonly Dictionary<string, (Geometry? Fill, Geometry Outline)> Cache = [];

    static DuoIcon()
    {
        AffectsRender<DuoIcon>(IconProperty, ForegroundProperty);
        ColorKeyProperty.Changed.AddClassHandler<DuoIcon>((icon, _) => icon.BindColorKey());
    }

    public string? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? ColorKey { get => GetValue(ColorKeyProperty); set => SetValue(ColorKeyProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public DuoIcon() => UiScale.Enable(this); // 글자 크기 배율에 맞춰 아이콘도 커지고 작아진다

    IDisposable? _colorBinding;

    void BindColorKey()
    {
        _colorBinding?.Dispose();
        _colorBinding = null;
        if (ColorKey is { Length: > 0 } key && Application.Current is { } app)
            _colorBinding = this.Bind(ForegroundProperty, app.GetResourceObservable(key));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    static (Geometry? Fill, Geometry Outline)? Lookup(string? name)
    {
        if (name is null || !PhosphorIcons.Data.TryGetValue(name, out var data)) return null;
        if (!Cache.TryGetValue(name, out var pair))
        {
            pair = (data.Fill.Length > 0 ? Geometry.Parse(data.Fill) : null, Geometry.Parse(data.Outline));
            Cache[name] = pair;
        }
        return pair;
    }

    public override void Render(DrawingContext context)
    {
        if (Lookup(Icon) is not { } geometry || Foreground is not { } brush) return;
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0) return;

        var scale = side / Grid;
        var offset = new Vector((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using var move = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y));
        using var enabled = context.PushOpacity(IsEffectivelyEnabled ? 1 : DisabledOpacity);
        if (geometry.Fill is { } fill)
        {
            using var faint = context.PushOpacity(FillOpacity);
            context.DrawGeometry(brush, null, fill);
        }
        context.DrawGeometry(brush, null, geometry.Outline);
    }
}
