using System.Windows;

namespace NovelEngine.Editor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length == 2 && e.Args[0] == "--screenshot")
        {
            ScreenshotRenderer.RenderEditor(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--code-screenshot")
        {
            ScreenshotRenderer.RenderEditor(e.Args[1], showCode: true);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--files-screenshot")
        {
            ScreenshotRenderer.RenderEditor(e.Args[1], showFiles: true);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--preview-screenshot")
        {
            ScreenshotRenderer.RenderPreview(e.Args[1]);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }
}
