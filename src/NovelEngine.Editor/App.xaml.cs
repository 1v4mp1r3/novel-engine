using System.IO;
using System.Windows;

namespace NovelEngine.Editor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (e.Args.Length is 3 or 4
            && e.Args[0] == "--compile-project")
        {
            CompileProject(
                e.Args[1],
                e.Args[2],
                e.Args.Length == 4 && e.Args[3] == "--debug");
            return;
        }
        if (e.Args.Length is 2 or 3
            && e.Args[0] == "--play-build")
        {
            RunBuild(
                e.Args[1],
                e.Args.Length == 3 && e.Args[2] == "--debug");
            return;
        }
        if (e.Args.Length is 2 or 3 && e.Args[0] == "--screenshot")
        {
            ScreenshotRenderer.RenderEditor(
                e.Args[1],
                startupProjectPath: e.Args.Length == 3 ? e.Args[2] : null);
            Shutdown();
            return;
        }
        if (e.Args.Length is 2 or 3 && e.Args[0] == "--code-screenshot")
        {
            ScreenshotRenderer.RenderEditor(
                e.Args[1],
                showCode: true,
                startupProjectPath: e.Args.Length == 3 ? e.Args[2] : null);
            Shutdown();
            return;
        }
        if (e.Args.Length is 2 or 3 && e.Args[0] == "--files-screenshot")
        {
            ScreenshotRenderer.RenderEditor(
                e.Args[1],
                showFiles: true,
                startupProjectPath: e.Args.Length == 3 ? e.Args[2] : null);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--preview-screenshot")
        {
            ScreenshotRenderer.RenderPreview(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--scene-editor-screenshot")
        {
            ScreenshotRenderer.RenderSceneEditor(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--debug-build-screenshot")
        {
            ScreenshotRenderer.RenderCompiledPreview(e.Args[1]);
            Shutdown();
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--context-menu-smoke")
        {
            ScreenshotRenderer.SmokeContextMenus();
            Shutdown();
            return;
        }

        var startupProjectPath = GetStartupProjectPath(e.Args);
        if (startupProjectPath is null
            && !TryGetStartupProjectFromDialog(out startupProjectPath))
        {
            Shutdown();
            return;
        }

        var window = new MainWindow(startupProjectPath);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
    }

    private static string? GetStartupProjectPath(string[] args)
    {
        if (args.Length == 1 && !args[0].StartsWith("--", StringComparison.Ordinal))
        {
            return args[0];
        }
        if (args.Length == 2
            && args[0].Equals("--open-project", StringComparison.OrdinalIgnoreCase))
        {
            return args[1];
        }
        return null;
    }

    private bool TryGetStartupProjectFromDialog(out string? startupProjectPath)
    {
        startupProjectPath = null;
        var launcher = new ProjectStartupWindow(RecentProjectsStore.Load());
        if (launcher.ShowDialog() != true || launcher.SelectedPath is null)
        {
            return false;
        }

        try
        {
            startupProjectPath = launcher.SelectedAction == ProjectStartupAction.Create
                ? ProjectWorkspace.CreateProjectInDirectory(launcher.SelectedPath)
                : launcher.SelectedPath;
            return true;
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                error.Message,
                "Проект Novel Engine",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void CompileProject(
        string projectPath,
        string outputDirectory,
        bool debugSymbols)
    {
        try
        {
            var project = NovelEngine.Core.ProjectSerializer.Load(projectPath);
            NovelEngine.Core.NovelBuildCompiler.Compile(
                project,
                projectPath,
                outputDirectory,
                debugSymbols);
            Shutdown(0);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                error.Message,
                "Компиляция игры",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
        }
    }

    private void RunBuild(string manifestPath, bool debugMode)
    {
        try
        {
            var build = NovelEngine.Core.NovelBuildCompiler.LoadBuild(manifestPath);
            var directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            var window = new PreviewWindow(
                build.Project,
                null,
                directory,
                debugMode,
                build.Manifest);
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                error.Message,
                "Не удалось запустить build",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
        }
    }
}
