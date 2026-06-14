using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public partial class PreviewWindow : Window
{
    private readonly NovelProject _project;
    private readonly NovelPlayer _player;
    private readonly string _assetDirectory;
    private readonly bool _debugMode;
    private readonly NovelBuildManifest? _buildManifest;
    private readonly MediaPlayer _musicPlayer = new();
    private readonly MediaPlayer _transitionPlayer = new();
    private string _currentMusicPath = string.Empty;
    private bool _transitioning;

    public PreviewWindow(
        NovelProject project,
        string? startNodeId,
        string assetDirectory,
        bool debugMode = false,
        NovelBuildManifest? buildManifest = null)
    {
        InitializeComponent();
        _project = project;
        _player = new NovelPlayer(project);
        _assetDirectory = assetDirectory;
        _debugMode = debugMode;
        _buildManifest = buildManifest;
        DebugPanel.Visibility = debugMode
            ? Visibility.Visible
            : Visibility.Collapsed;
        Title = debugMode
            ? $"{project.Title} — Debug"
            : startNodeId is null
                ? project.Title
                : "Предпросмотр ноды";

        _musicPlayer.Volume = 0.55;
        _musicPlayer.MediaEnded += (_, _) =>
        {
            _musicPlayer.Position = TimeSpan.Zero;
            _musicPlayer.Play();
        };
        _transitionPlayer.Volume = 0.8;

        try
        {
            var node = startNodeId is null
                ? _player.Start()
                : _player.StartAt(startNodeId);
            RenderNode(node);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            Loaded += (_, _) =>
            {
                MessageBox.Show(
                    this,
                    error.Message,
                    "Ошибка запуска",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
            };
        }
    }

    private void RenderNode(NovelNode node)
    {
        SetBackground(_player.State.CurrentBackground);
        SetCharacters(_player.State.CurrentCharacters);
        SyncMusic(_player.State.CurrentMusic);
        RefreshDebugState(node);

        NodeTitleText.Text = node.Title;
        SpeakerText.Text = node.Kind == NodeKind.Dialogue ? node.Speaker : string.Empty;
        SpeakerText.Visibility = SpeakerText.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        DialogueText.Text = node.Text;
        ChoicesPanel.Children.Clear();

        var outputs = _player.GetAvailableOutputs();
        for (var index = 0; index < outputs.Count; index++)
        {
            var output = outputs[index];
            var label = output.TargetNodeId is null
                ? $"{index + 1}. {output.Label} · не подключено"
                : $"{index + 1}. {output.Label}";
            var button = CreateChoiceButton(label);
            button.Tag = output.Id;
            button.Click += async (_, _) => await ChooseAsync(output.Id);
            ChoicesPanel.Children.Add(button);
        }

        if (outputs.Count == 0)
        {
            var end = CreateChoiceButton("Конец");
            end.Click += (_, _) => Close();
            ChoicesPanel.Children.Add(end);
        }
    }

    private void RefreshDebugState(NovelNode node)
    {
        if (!_debugMode)
        {
            return;
        }
        DebugBuildText.Text = _buildManifest is null
            ? "preview runtime"
            : $"build {_buildManifest.BuildId} · {_buildManifest.CompiledAtUtc:O}";
        var variables = _player.State.Variables.Count == 0
            ? "(нет)"
            : string.Join(
                "\n",
                _player.State.Variables
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => $"{pair.Key} = {pair.Value ?? "null"}"));
        var characters = _player.State.CurrentCharacters.Count == 0
            ? "(нет)"
            : string.Join(
                ", ",
                _player.State.CurrentCharacters.Select(character => character.Id));
        DebugStateText.Text =
            $"node: {node.Id} ({node.Kind})\n"
            + $"background: {DisplayStateValue(_player.State.CurrentBackground)}\n"
            + $"music: {DisplayStateValue(_player.State.CurrentMusic)}\n"
            + $"characters: {characters}\n"
            + $"variables:\n{variables}";
    }

    private static string DisplayStateValue(string value) =>
        value.Length == 0 ? "(нет)" : value;

    private async Task ChooseAsync(string outputId)
    {
        if (_transitioning)
        {
            return;
        }

        var output = _player.CurrentNode?.Outputs.FirstOrDefault(
            candidate => candidate.Id == outputId);
        if (output is null)
        {
            return;
        }
        if (output.TargetNodeId is null)
        {
            MessageBox.Show(
                this,
                "Этот выход пока не подключён к ноде.",
                "Переход",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _transitioning = true;
        SetChoicesEnabled(false);
        PlayTransitionSound(output.TransitionSound);

        try
        {
            var halfDuration = TimeSpan.FromMilliseconds(output.FadeDurationMs / 2d);
            if (halfDuration > TimeSpan.Zero)
            {
                await AnimateOpacity(FadeOverlay, FadeOverlay.Opacity, 1, halfDuration);
            }

            var node = _player.Choose(outputId);
            RenderNode(node);

            if (halfDuration > TimeSpan.Zero)
            {
                await AnimateOpacity(FadeOverlay, 1, 0, halfDuration);
            }
            else
            {
                FadeOverlay.Opacity = 0;
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            FadeOverlay.Opacity = 0;
            MessageBox.Show(
                this,
                error.Message,
                "Ошибка перехода",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _transitioning = false;
            SetChoicesEnabled(true);
        }
    }

    private void SetBackground(string path)
    {
        BackgroundImage.Source = LoadBitmap(ResolveAsset(path));
        RootGrid.Background = BackgroundImage.Source is null
            ? new LinearGradientBrush(
                Color.FromRgb(30, 47, 68),
                Color.FromRgb(7, 11, 17),
                45)
            : Brushes.Black;
    }

    private void SetCharacters(IReadOnlyList<CharacterPlacement> characters)
    {
        CharacterLayer.Children.Clear();
        foreach (var character in characters)
        {
            var element = CreateCharacterElement(character);
            CharacterLayer.Children.Add(element);
        }
    }

    private FrameworkElement CreateCharacterElement(CharacterPlacement character)
    {
        var root = new Grid
        {
            Width = CharacterLayout.BaseWidth,
            Height = CharacterLayout.BaseHeight,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var image = LoadBitmap(ResolveAsset(character.Sprite));
        if (image is not null)
        {
            root.Children.Add(
                new Image
                {
                    Source = image,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Bottom,
                });
        }
        else
        {
            root.Children.Add(
                new Border
                {
                    Width = 330,
                    Height = 650,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CornerRadius = new CornerRadius(24),
                    Background = new SolidColorBrush(Color.FromArgb(190, 40, 54, 74)),
                    BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
                    BorderThickness = new Thickness(3),
                    Child = new TextBlock
                    {
                        Text = character.Name,
                        FontSize = 28,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
        }

        var x = character.HasCustomTransform
            ? character.X
            : CharacterLayout.DefaultCenterX(character.Position);
        var y = character.HasCustomTransform
            ? character.Y
            : CharacterLayout.DefaultCenterY;
        var scale = character.HasCustomTransform ? character.Scale : 1;
        var rotation = character.HasCustomTransform ? character.Rotation : 0;
        Canvas.SetLeft(root, x - CharacterLayout.BaseWidth / 2);
        Canvas.SetTop(root, y - CharacterLayout.BaseHeight / 2);
        root.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new RotateTransform(rotation),
            },
        };
        return root;
    }

    private void SyncMusic(string path)
    {
        var resolved = ResolveAsset(path);
        if (string.Equals(resolved, _currentMusicPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _musicPlayer.Stop();
        _musicPlayer.Close();
        _currentMusicPath = resolved;
        if (resolved.Length == 0 || !File.Exists(resolved))
        {
            return;
        }

        try
        {
            _musicPlayer.Open(new Uri(resolved, UriKind.Absolute));
            _musicPlayer.Play();
        }
        catch (Exception)
        {
            _currentMusicPath = string.Empty;
        }
    }

    private void PlayTransitionSound(string path)
    {
        var resolved = ResolveAsset(path);
        if (resolved.Length == 0 || !File.Exists(resolved))
        {
            return;
        }
        try
        {
            _transitionPlayer.Stop();
            _transitionPlayer.Close();
            _transitionPlayer.Open(new Uri(resolved, UriKind.Absolute));
            _transitionPlayer.Play();
        }
        catch (Exception)
        {
            // Preview remains usable when an audio codec is unavailable.
        }
    }

    private string ResolveAsset(string path)
    {
        path = _project.ResolveAssetReference(path);
        if (path.Length == 0 || Path.IsPathRooted(path))
        {
            return path;
        }
        return Path.GetFullPath(Path.Combine(_assetDirectory, path));
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (path.Length == 0 || !File.Exists(path))
        {
            return null;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Button CreateChoiceButton(string label) =>
        new()
        {
            Content = label,
            MinWidth = 175,
            FontSize = 14,
            Padding = new Thickness(16, 9, 16, 9),
            Margin = new Thickness(0, 0, 10, 8),
            Background = new SolidColorBrush(Color.FromArgb(215, 37, 53, 74)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(74, 98, 128)),
        };

    private void SetChoicesEnabled(bool enabled)
    {
        foreach (var button in ChoicesPanel.Children.OfType<Button>())
        {
            button.IsEnabled = enabled;
        }
    }

    private static Task AnimateOpacity(
        UIElement element,
        double from,
        double to,
        TimeSpan duration)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation(from, to, duration)
        {
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        animation.Completed += (_, _) =>
        {
            element.Opacity = to;
            element.BeginAnimation(OpacityProperty, null);
            completion.SetResult();
        };
        element.BeginAnimation(OpacityProperty, animation);
        return completion.Task;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key is >= Key.D1 and <= Key.D9)
        {
            var index = e.Key - Key.D1;
            var buttons = ChoicesPanel.Children.OfType<Button>().ToList();
            if (index < buttons.Count)
            {
                buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _musicPlayer.Stop();
        _musicPlayer.Close();
        _transitionPlayer.Stop();
        _transitionPlayer.Close();
    }
}
