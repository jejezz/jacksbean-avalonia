using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace JackBean.App.Tools;

public partial class EditorStyles : Styles
{
    public EditorStyles() => AvaloniaXamlLoader.Load(this);
}
