namespace Maple2.Server.Launcher.UI;

public static class LogViewFactory
{
    public static (TabItem, IProgress<string>) Create(string header)
    {
        TextBox box = new()
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        box.TextChanged += (_, _) => box.ScrollToEnd();

        TabItem tab = new() { Header = header, Content = box };
        return (tab, new TextBlockProgress(box));
    }
}