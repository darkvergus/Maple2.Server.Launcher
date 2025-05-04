namespace Maple2.Server.Launcher.UI;

public sealed class TextBlockProgress(TextBox box) : IProgress<string>
{
    public void Report(string value) => box.Dispatcher.Invoke(() =>
    {
        box.AppendText(value + Environment.NewLine);
        box.ScrollToEnd();
    });
}