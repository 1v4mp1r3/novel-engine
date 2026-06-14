using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal static class ScreenshotRenderer
{
    public static void RenderEditor(
        string path,
        bool showCode = false,
        bool showFiles = false,
        string? startupProjectPath = null)
    {
        var window = new MainWindow(startupProjectPath)
        {
            Width = 1480,
            Height = 900,
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        if (showFiles)
        {
            window.SelectFilesWorkspace();
        }
        else if (showCode)
        {
            window.SelectCodeWorkspace();
        }
        else
        {
            window.SelectPreviewNode(NodeKind.Dialogue);
        }
        RenderWindow(window, path);
        window.Close();
    }

    public static void RenderPreview(string path)
    {
        var assetDirectory = Path.Combine(Path.GetTempPath(), "novel-engine-wpf-preview");
        Directory.CreateDirectory(assetDirectory);
        var background = Path.Combine(assetDirectory, "background.png");
        var character = Path.Combine(assetDirectory, "character.png");
        CreateImage(background, 1280, 720, backgroundImage: true);
        CreateImage(character, 360, 640, backgroundImage: false);

        var project = NovelProject.CreateDefault();
        project.Assets.Add(
            new NovelAsset
            {
                Id = "preview_background",
                Kind = AssetKind.Image,
                Path = background,
            });
        project.Assets.Add(
            new NovelAsset
            {
                Id = "preview_character",
                Kind = AssetKind.Image,
                Path = character,
            });
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = "@preview_background";
        scene.InheritBackground = false;
        scene.InheritCharacters = false;
        scene.Characters.Add(
            new CharacterPlacement
            {
                Id = "preview-character",
                Name = "Герой",
                Sprite = "@preview_character",
                Position = CharacterPosition.Left,
            });
        var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);

        var window = new PreviewWindow(project, dialogue.Id, assetDirectory)
        {
            Width = 1160,
            Height = 780,
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        RenderWindow(window, path);
        window.Close();
    }

    public static void RenderSceneEditor(string path)
    {
        var assetDirectory = Path.Combine(
            Path.GetTempPath(),
            "novel-engine-scene-editor-preview");
        Directory.CreateDirectory(assetDirectory);
        var background = Path.Combine(assetDirectory, "background.png");
        var character = Path.Combine(assetDirectory, "character.png");
        CreateImage(background, 1280, 720, backgroundImage: true);
        CreateImage(character, 360, 640, backgroundImage: false);

        var project = NovelProject.CreateDefault();
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = background;
        scene.InheritBackground = false;
        scene.InheritCharacters = false;
        scene.Characters.Add(
            new CharacterPlacement
            {
                Id = "scene-editor-character",
                Name = "Герой",
                Sprite = character,
                Position = CharacterPosition.Center,
                HasCustomTransform = true,
                X = 1040,
                Y = 510,
                Scale = 0.86,
                Rotation = -4,
            });

        var window = new SceneEditorWindow(project, scene, assetDirectory)
        {
            Width = 1320,
            Height = 820,
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        window.EnableTransformModeForScreenshot();
        RenderWindow(window, path);
        window.Close();
    }

    public static void RenderCompiledPreview(string path)
    {
        var rootDirectory = Path.Combine(
            Path.GetTempPath(),
            "novel-engine-compiled-preview");
        if (Directory.Exists(rootDirectory))
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
        Directory.CreateDirectory(rootDirectory);
        var background = Path.Combine(rootDirectory, "background.png");
        var character = Path.Combine(rootDirectory, "character.png");
        CreateImage(background, 1280, 720, backgroundImage: true);
        CreateImage(character, 360, 640, backgroundImage: false);

        var project = NovelProject.CreateDefault();
        var backgroundAsset = ProjectAssets.Import(
            project,
            Path.Combine(rootDirectory, "preview.novel.json"),
            background,
            "backgrounds");
        var characterAsset = ProjectAssets.Import(
            project,
            Path.Combine(rootDirectory, "preview.novel.json"),
            character,
            "characters");
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = AssetReference.Create(backgroundAsset.Id);
        scene.InheritBackground = false;
        scene.InheritCharacters = false;
        scene.Characters.Add(
            new CharacterPlacement
            {
                Id = "debug-character",
                Name = "Debug Hero",
                Sprite = AssetReference.Create(characterAsset.Id),
                Position = CharacterPosition.Left,
            });
        scene.Script = "set debug_score = 7";
        project.SourceCode = ProjectLanguage.Format(project);
        var projectPath = Path.Combine(rootDirectory, "preview.novel.json");
        ProjectSerializer.Save(project, projectPath);
        var build = NovelBuildCompiler.Compile(
            project,
            projectPath,
            Path.Combine(rootDirectory, "build"),
            debugSymbols: true);
        var loaded = NovelBuildCompiler.LoadBuild(build.ManifestPath);
        var dialogue = loaded.Project.Nodes.Single(
            node => node.Kind == NodeKind.Dialogue);
        var window = new PreviewWindow(
            loaded.Project,
            dialogue.Id,
            build.OutputDirectory,
            debugMode: true,
            loaded.Manifest)
        {
            Width = 1160,
            Height = 780,
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();
        RenderWindow(window, path);
        window.Close();
    }

    public static void SmokeContextMenus()
    {
        var window = new MainWindow
        {
            Width = 900,
            Height = 600,
            Left = -20_000,
            Top = -20_000,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        window.Show();

        var submenu = new MenuItem { Header = "Подменю" };
        submenu.Items.Add(new MenuItem { Header = "Вложенный пункт" });
        var menu = new ContextMenu
        {
            PlacementTarget = window,
            Items =
            {
                new MenuItem { Header = "Проверка пункта" },
                new Separator(),
                submenu,
            },
        };
        menu.IsOpen = true;
        window.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
        menu.IsOpen = false;
        window.Close();
    }

    private static void RenderWindow(Window window, string path)
    {
        window.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CreateImage(
        string path,
        int width,
        int height,
        bool backgroundImage)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                byte red;
                byte green;
                byte blue;
                byte alpha;
                if (backgroundImage)
                {
                    red = (byte)(22 + x * 28 / width);
                    green = (byte)(36 + x * 42 / width);
                    blue = (byte)(58 + x * 54 / width);
                    alpha = 255;
                }
                else
                {
                    var inHead = Math.Pow((x - width / 2d) / 70, 2)
                        + Math.Pow((y - 95d) / 70, 2) <= 1;
                    var inBody = x > 70 && x < width - 70 && y > 155 && y < height - 35;
                    if (!inHead && !inBody)
                    {
                        red = green = blue = alpha = 0;
                    }
                    else
                    {
                        red = inHead ? (byte)92 : (byte)52;
                        green = inHead ? (byte)211 : (byte)77;
                        blue = inHead ? (byte)178 : (byte)112;
                        alpha = 255;
                    }
                }

                pixels[index] = blue;
                pixels[index + 1] = green;
                pixels[index + 2] = red;
                pixels[index + 3] = alpha;
            }
        }

        var bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
