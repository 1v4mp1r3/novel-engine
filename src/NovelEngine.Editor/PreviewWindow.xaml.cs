using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public partial class PreviewWindow : Window
{
    private static readonly TimeSpan MinVoiceRestartDelay = TimeSpan.FromMilliseconds(45);
    private static readonly JsonSerializerOptions SaveOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly NovelProject _project;
    private readonly NovelPlayer _player;
    private readonly string _assetDirectory;
    private readonly bool _debugMode;
    private readonly NovelBuildManifest? _buildManifest;
    private readonly MediaPlayer _musicPlayer = new();
    private readonly MediaPlayer _transitionPlayer = new();
    private readonly MediaPlayer _voicePlayer = new();
    private readonly GameRuntimeSettings _settings = new();
    private string _currentMusicPath = string.Empty;
    private bool _transitioning;
    private bool _paused;
    private bool _mainMenuActive;
    private string _currentVoicePath = string.Empty;
    private double _currentVoicePitch = 1;
    private DateTime _lastVoiceStartedUtc = DateTime.MinValue;
    private CancellationTokenSource? _dialogueCts;

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

        _musicPlayer.MediaEnded += (_, _) =>
        {
            _musicPlayer.Position = TimeSpan.Zero;
            _musicPlayer.Play();
        };
        ApplyRuntimeSettings();

        try
        {
            if (startNodeId is null)
            {
                RenderMainMenu();
            }
            else
            {
                RenderNode(_player.StartAt(startNodeId));
            }
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
        _mainMenuActive = false;
        MainMenuHost.Visibility = Visibility.Collapsed;
        _dialogueCts?.Cancel();
        SetBackground(_player.State.CurrentBackground);
        SetCharacters(_player.State.CurrentCharacters);
        SyncMusic(_player.State.CurrentMusic);
        RefreshDebugState(node);

        NodeTitleText.Text = node.Title;
        SpeakerText.Text = node.Kind == NodeKind.Dialogue ? node.Speaker : string.Empty;
        SpeakerText.Visibility = SpeakerText.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        DialogueText.Text = string.Empty;
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
        SetChoicesEnabled(!_paused && !_transitioning);
        _dialogueCts = new CancellationTokenSource();
        _ = TypeDialogueAsync(node, _dialogueCts.Token);
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

    private void RenderMainMenu()
    {
        _mainMenuActive = true;
        _paused = false;
        _dialogueCts?.Cancel();
        _musicPlayer.Stop();
        _transitionPlayer.Stop();
        _voicePlayer.Stop();
        _voicePlayer.Close();
        _currentVoicePath = string.Empty;
        ChoicesPanel.Children.Clear();
        NodeTitleText.Text = string.Empty;
        SpeakerText.Text = string.Empty;
        SpeakerText.Visibility = Visibility.Collapsed;
        DialogueText.Text = string.Empty;
        CharacterLayer.Children.Clear();
        BackgroundImage.Source = null;
        RootGrid.Background = Brushes.Black;
        MainMenuStage.Children.Clear();

        var background = ResolveAsset(_project.MainMenu.Background);
        MainMenuStage.Background = background.Length > 0 && File.Exists(background)
            ? new ImageBrush(LoadBitmap(background)) { Stretch = Stretch.UniformToFill }
            : new LinearGradientBrush(
                Color.FromRgb(24, 38, 55),
                Color.FromRgb(7, 11, 17),
                45);

        foreach (var element in _project.MainMenu.Elements)
        {
            var visual = CreateMainMenuElement(element);
            Canvas.SetLeft(visual, element.X);
            Canvas.SetTop(visual, element.Y);
            MainMenuStage.Children.Add(visual);
        }
        MainMenuHost.Visibility = Visibility.Visible;
    }

    private FrameworkElement CreateMainMenuElement(MainMenuElement element)
    {
        var isButton = element.Kind is MainMenuElementKind.Button or MainMenuElementKind.ImageButton;
        var style = MainMenuElementStyle.From(
            element,
            Color.FromArgb(208, 37, 53, 74),
            Color.FromRgb(80, 101, 127));
        var border = new Border
        {
            Width = element.Width,
            Height = element.Height,
            Background = style.Background,
            BorderBrush = style.Border,
            BorderThickness = isButton ? new Thickness(1) : new Thickness(0),
            CornerRadius = style.CornerRadius,
            Opacity = style.Opacity,
            Child = CreateMainMenuElementContent(element, style),
            Cursor = isButton ? Cursors.Hand : Cursors.Arrow,
        };
        if (isButton)
        {
            var normalBackground = style.Background;
            border.MouseLeftButtonUp += (_, _) => ExecuteMainMenuAction(element.Action);
            border.MouseEnter += (_, _) =>
            {
                border.Background = new SolidColorBrush(Color.FromArgb(230, 45, 68, 94));
            };
            border.MouseLeave += (_, _) =>
            {
                border.Background = normalBackground;
            };
        }
        return border;
    }

    private UIElement CreateMainMenuElementContent(
        MainMenuElement element,
        MainMenuElementStyle style)
    {
        var text = new TextBlock
        {
            Text = element.Text,
            Foreground = style.Foreground,
            FontFamily = new FontFamily(element.FontFamily),
            FontSize = style.FontSize,
            FontWeight = style.FontWeight,
            FontStyle = style.FontStyle,
            TextAlignment = style.TextAlignment,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        if (element.Kind is MainMenuElementKind.Label or MainMenuElementKind.Button)
        {
            return text;
        }

        var grid = new Grid();
        var imagePath = ResolveAsset(element.Image);
        if (imagePath.Length > 0 && File.Exists(imagePath))
        {
            grid.Children.Add(
                new Image
                {
                    Source = LoadBitmap(imagePath),
                    Stretch = Stretch.UniformToFill,
                });
        }
        grid.Children.Add(text);
        return grid;
    }

    private void ExecuteMainMenuAction(MainMenuAction action)
    {
        switch (action)
        {
            case MainMenuAction.NewGame:
                RenderNode(_player.Start());
                break;
            case MainMenuAction.Continue:
                if (File.Exists(GetQuickSavePath()))
                {
                    QuickLoad();
                }
                else
                {
                    RenderNode(_player.Start());
                }
                break;
            case MainMenuAction.LoadGame:
                LoadGameFromDialog();
                break;
            case MainMenuAction.Settings:
                ShowSettings();
                break;
            case MainMenuAction.Exit:
                Close();
                break;
            case MainMenuAction.None:
            default:
                break;
        }
    }

    private async Task ChooseAsync(string outputId)
    {
        if (_transitioning || _paused || _mainMenuActive)
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

    private async Task TypeDialogueAsync(NovelNode node, CancellationToken cancellationToken)
    {
        var text = node.Text;
        var voice = ResolveVoice(node);
        PrepareCharacterVoice(voice);
        var visible = string.Empty;
        var voicedCharacters = 0;
        try
        {
            foreach (var character in text)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (_paused)
                {
                    await Task.Delay(35, cancellationToken);
                }

                visible += character;
                DialogueText.Text = visible;
                if (!char.IsWhiteSpace(character) && voice is not null)
                {
                    voicedCharacters++;
                    if (voicedCharacters % voice.EveryNthCharacter == 0)
                    {
                        PlayCharacterVoice(voice);
                    }
                }
                if (_settings.TextDelayMs > 0)
                {
                    await Task.Delay(_settings.TextDelayMs, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A new node started rendering.
        }
    }

    private CharacterVoice? ResolveVoice(NovelNode node)
    {
        var character = _player.State.CurrentCharacters.FirstOrDefault(
                candidate => candidate.Name.Equals(
                    node.Speaker,
                    StringComparison.OrdinalIgnoreCase))
            ?? _player.State.CurrentCharacters.FirstOrDefault();
        if (character is null)
        {
            return null;
        }
        var sound = ResolveAsset(character.VoiceSound);
        if (sound.Length == 0 || !File.Exists(sound))
        {
            return null;
        }
        return new CharacterVoice(
            sound,
            Math.Clamp(character.VoicePitch, 0.25, 4),
            Math.Clamp(character.VoiceEveryNthCharacter, 1, 12));
    }

    private void PlayCharacterVoice(CharacterVoice voice)
    {
        try
        {
            if (DateTime.UtcNow - _lastVoiceStartedUtc < MinVoiceRestartDelay)
            {
                return;
            }
            PrepareCharacterVoice(voice);
            _voicePlayer.Stop();
            _voicePlayer.Position = TimeSpan.Zero;
            _voicePlayer.Volume = _settings.VoiceVolume;
            _lastVoiceStartedUtc = DateTime.UtcNow;
            _voicePlayer.Play();
        }
        catch (Exception)
        {
            // Missing codecs or very short files should not block the scene.
        }
    }

    private void PrepareCharacterVoice(CharacterVoice? voice)
    {
        if (voice is null)
        {
            _voicePlayer.Stop();
            _voicePlayer.Close();
            _currentVoicePath = string.Empty;
            return;
        }
        if (_currentVoicePath.Equals(voice.SoundPath, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(_currentVoicePitch - voice.Pitch) < 0.001)
        {
            return;
        }

        _voicePlayer.Stop();
        _voicePlayer.Close();
        _voicePlayer.Volume = _settings.VoiceVolume;
        _voicePlayer.SpeedRatio = voice.Pitch;
        _voicePlayer.Open(new Uri(voice.SoundPath, UriKind.Absolute));
        _currentVoicePath = voice.SoundPath;
        _currentVoicePitch = voice.Pitch;
        _lastVoiceStartedUtc = DateTime.MinValue;
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        OpenGameContextMenu();
    }

    private void OpenGameContextMenu()
    {
        var hasGameState = _player.CurrentNode is not null && !_mainMenuActive;
        var menu = new ContextMenu();
        var pause = CreateGameMenuItem(
            _paused ? "Продолжить" : "Пауза",
            () => SetPaused(!_paused));
        pause.IsEnabled = hasGameState;
        menu.Items.Add(pause);
        menu.Items.Add(CreateGameMenuItem(
            "Настройки...",
            ShowSettings));
        menu.Items.Add(new Separator());
        var save = CreateGameMenuItem(
            "Сохранить...",
            SaveGameAs);
        save.IsEnabled = hasGameState;
        menu.Items.Add(save);
        menu.Items.Add(CreateGameMenuItem(
            "Загрузить...",
            LoadGameFromDialog));
        var quickSave = CreateGameMenuItem(
            "Быстрое сохранение",
            QuickSave);
        quickSave.IsEnabled = hasGameState;
        menu.Items.Add(quickSave);
        var quickLoad = CreateGameMenuItem(
            "Быстрая загрузка",
            QuickLoad);
        quickLoad.IsEnabled = File.Exists(GetQuickSavePath());
        menu.Items.Add(quickLoad);
        menu.Items.Add(new Separator());
        var mainMenu = CreateGameMenuItem(
            "В главное меню",
            ReturnToMainMenu);
        mainMenu.IsEnabled = hasGameState;
        menu.Items.Add(mainMenu);
        menu.Items.Add(CreateGameMenuItem(
            "Выйти из игры",
            Close));
        menu.PlacementTarget = RootGrid;
        menu.IsOpen = true;
    }

    private static MenuItem CreateGameMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        SetChoicesEnabled(!_paused && !_transitioning);
        if (_paused)
        {
            _musicPlayer.Pause();
            _transitionPlayer.Pause();
            _voicePlayer.Pause();
        }
        else
        {
            if (_currentMusicPath.Length > 0)
            {
                _musicPlayer.Play();
            }
            _transitionPlayer.Play();
            _voicePlayer.Play();
        }
    }

    private void ShowSettings()
    {
        var dialog = new GameSettingsWindow(_settings.Clone())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            _settings.CopyFrom(dialog.Settings);
            ApplyRuntimeSettings();
        }
    }

    private void ApplyRuntimeSettings()
    {
        _settings.MusicVolume = Math.Clamp(_settings.MusicVolume, 0, 1);
        _settings.EffectsVolume = Math.Clamp(_settings.EffectsVolume, 0, 1);
        _settings.VoiceVolume = Math.Clamp(_settings.VoiceVolume, 0, 1);
        _settings.TextDelayMs = Math.Clamp(_settings.TextDelayMs, 0, 120);
        _musicPlayer.Volume = _settings.MusicVolume;
        _transitionPlayer.Volume = _settings.EffectsVolume;
        _voicePlayer.Volume = _settings.VoiceVolume;
    }

    private void SaveGameAs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить игру",
            Filter = "Сохранение Novel Engine|*.save.json|JSON|*.json",
            DefaultExt = ".save.json",
            AddExtension = true,
            InitialDirectory = GetSaveDirectory(),
            FileName = "save-1.save.json",
        };
        if (dialog.ShowDialog(this) == true)
        {
            SaveGame(dialog.FileName);
        }
    }

    private void LoadGameFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Загрузить игру",
            Filter = "Сохранение Novel Engine|*.save.json|JSON|*.json|Все файлы|*.*",
            InitialDirectory = GetSaveDirectory(),
        };
        if (dialog.ShowDialog(this) == true)
        {
            LoadGame(dialog.FileName);
        }
    }

    private void QuickSave() => SaveGame(GetQuickSavePath());

    private void QuickLoad()
    {
        var path = GetQuickSavePath();
        if (!File.Exists(path))
        {
            MessageBox.Show(
                this,
                "Быстрое сохранение ещё не создано.",
                "Быстрая загрузка",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        LoadGame(path);
    }

    private void SaveGame(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var state = _player.CreateSaveState();
            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, state, SaveOptions);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Не удалось сохранить игру:\n\n{error.Message}",
                "Сохранение",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadGame(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var state = JsonSerializer.Deserialize<RuntimeSaveState>(stream, SaveOptions)
                ?? throw new InvalidDataException("Файл сохранения пуст.");
            var node = _player.Restore(state);
            _paused = false;
            FadeOverlay.Opacity = 0;
            RenderNode(node);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or JsonException
            or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"Не удалось загрузить игру:\n\n{error.Message}",
                "Загрузка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ReturnToMainMenu()
    {
        SetPaused(false);
        FadeOverlay.Opacity = 0;
        RenderMainMenu();
    }

    private string GetQuickSavePath() =>
        Path.Combine(GetSaveDirectory(), "quick.save.json");

    private string GetSaveDirectory()
    {
        var safeTitle = string.Concat(
            _project.Title.Select(
                character => Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character));
        if (safeTitle.Length == 0)
        {
            safeTitle = "NovelProject";
        }
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NovelEngine",
            "Saves",
            safeTitle);
        Directory.CreateDirectory(directory);
        return directory;
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

        if (_mainMenuActive)
        {
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
        _voicePlayer.Stop();
        _voicePlayer.Close();
        _dialogueCts?.Cancel();
    }

    private sealed record CharacterVoice(
        string SoundPath,
        double Pitch,
        int EveryNthCharacter);
}
