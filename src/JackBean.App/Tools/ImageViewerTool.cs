using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using JackBean.Domain;

namespace JackBean.App.Tools;

/// <summary>첫 내장 도구: 이미지 뷰어. 파일 하나당 창 하나. 휠로 확대/축소, 창 크기에 맞춰 보여 준다.</summary>
public sealed class ImageViewerTool : IBuiltInTool
{
    public string Id => "image-viewer";

    public Window CreateWindow(string path)
    {
        var window = new Window
        {
            Title = WindowTitle.Of(Path.GetFileName(path)),
            Width = 900,
            Height = 640,
            MinWidth = 240,
            MinHeight = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        try
        {
            var bitmap = new Bitmap(path);
            window.Closed += (_, _) => bitmap.Dispose();
            var image = new Image { Source = bitmap, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
            var scale = new ScaleTransform(1, 1);
            var host = new Border { Child = image, RenderTransform = scale, RenderTransformOrigin = RelativePoint.Center };
            window.PointerWheelChanged += (_, e) =>
            {
                var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
                var s = Math.Clamp(scale.ScaleX * factor, 0.1, 20);
                scale.ScaleX = scale.ScaleY = s;
            };
            window.DoubleTapped += (_, _) => scale.ScaleX = scale.ScaleY = 1;
            var info = new TextBlock
            {
                Text = $"{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}",
                Margin = new Thickness(8, 4),
                Opacity = 0.7,
            };
            var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
            grid.Children.Add(host);
            Grid.SetRow(info, 1);
            grid.Children.Add(info);
            window.Content = grid;
        }
        catch (Exception e)
        {
            window.Content = new TextBlock
            {
                Text = Strings.Current.ViewerFailed(e.Message),
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        return window;
    }
}
