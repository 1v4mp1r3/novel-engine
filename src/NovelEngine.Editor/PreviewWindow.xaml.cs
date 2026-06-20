using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    private const int VoicePlayerPoolSize = 6;
    private const int MaxVoiceSoundPools = 32;
    private const int MaxCachedPreviewBitmaps = 96;
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
    private readonly string _saveDirectoryName;
    private readonly MediaPlayer _musicPlayer = new();
    private readonly MediaPlayer _transitionPlayer = new();
    private readonly Dictionary<string, List<MediaPlayer>> _voicePlayerPools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _voicePlayerIndexes = new(StringComparer.OrdinalIgnoreCase);
    private readonly BoundedCache<string, BitmapImage?> _bitmapCache =
        new(MaxCachedPreviewBitmaps, StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _voicePoolLru = [];
    private readonly HashSet<MediaPlayer> _activeVoicePlayers = new();
    private readonly GameRuntimeSettings _settings = new();
    private string _currentMusicPath = string.Empty;
    private bool _transitioning;
    private bool _paused;
    private bool _mainMenuActive;
    private bool _choicesReady;
    private bool _skipTypingRequested;
    private int _typingVersion;
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
        _saveDirectoryName = CreateSaveDirectoryName(project, buildManifest);
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
        StopAllVoicePlayers(close: false);
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
        _choicesReady = false;
        _skipTypingRequested = false;

        _dialogueCts = new CancellationTokenSource();
        _ = TypeDialogueAsync(node, _dialogueCts.Token, ++_typingVersion);
    }

    private void ShowChoices()
    {
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
        _choicesReady = true;
        UpdateChoiceAvailability();
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
        StopAllVoicePlayers(close: true);
        ChoicesPanel.Children.Clear();
        _choicesReady = false;
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
            ? new ImageBrush(LoadBitmapCached(background)) { Stretch = Stretch.UniformToFill }
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
                    Source = LoadBitmapCached(imagePath),
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
        UpdateChoiceAvailability();
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
            UpdateChoiceAvailability();
        }
    }

    private void SetBackground(string path)
    {
        BackgroundImage.Source = LoadBitmapCached(ResolveAsset(path));
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
        var image = LoadBitmapCached(ResolveAsset(character.Sprite));
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

    private BitmapImage? LoadBitmapCached(string path)
    {
        if (path.Length == 0)
        {
            return null;
        }
        if (_bitmapCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var image = LoadBitmap(path);
        _bitmapCache.Set(path, image);
        return image;
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

    private void UpdateChoiceAvailability() =>
        SetChoicesEnabled(ChoiceAvailability.CanEnable(
            _choicesReady,
            _paused,
            _transitioning));

    private async Task TypeDialogueAsync(
        NovelNode node,
        CancellationToken cancellationToken,
        int version)
    {
        var text = node.Text;
        var voice = ResolveVoice(node);
        var visible = new StringBuilder(text.Length);
        var voicedCharacters = 0;
        try
        {
            foreach (var character in text)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_skipTypingRequested)
                {
                    DialogueText.Text = text;
                    break;
                }
                while (_paused)
                {
                    await Task.Delay(35, cancellationToken);
                }

                visible.Append(character);
                DialogueText.Text = visible.ToString();
                if (VoicePlaybackCadence.CountsAsVoicedCharacter(character)
                    && voice is not null)
                {
                    voicedCharacters++;
                    if (VoicePlaybackCadence.ShouldPlay(
                        voicedCharacters,
                        voice.EveryNthCharacter))
                    {
                        PlayCharacterVoice(voice);
                    }
                }
                if (_settings.TextDelayMs > 0)
                {
                    await WaitTypingDelayAsync(cancellationToken);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (version == _typingVersion)
            {
                ShowChoices();
            }
        }
        catch (OperationCanceledException)
        {
            // A new node started rendering.
        }
        finally
        {
            if (version == _typingVersion)
            {
                _skipTypingRequested = false;
            }
        }
    }

    private async Task WaitTypingDelayAsync(CancellationToken cancellationToken)
    {
        var remaining = _settings.TextDelayMs;
        while (remaining > 0 && !_skipTypingRequested)
        {
            var step = Math.Min(remaining, 15);
            await Task.Delay(step, cancellationToken);
            remaining -= step;
        }
    }

    private bool TrySkipTyping()
    {
        if (_mainMenuActive
            || _transitioning
            || _choicesReady
            || _dialogueCts is null)
        {
            return false;
        }

        _skipTypingRequested = true;
        return true;
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
        var sounds = character.GetVoiceSounds()
            .Select(ResolveAsset)
            .Where(sound => sound.Length > 0 && File.Exists(sound))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sounds.Count == 0)
        {
            return null;
        }
        return new CharacterVoice(
            sounds,
            Math.Clamp(character.VoicePitch, 0.25, 4),
            Math.Clamp(character.VoiceEveryNthCharacter, 1, 12));
    }

    private void PlayCharacterVoice(CharacterVoice voice)
    {
        try
        {
            var soundPath = VoiceSoundPicker.Pick(voice.SoundPaths);
            var player = GetNextVoicePlayer(soundPath, voice.Pitch);
            _activeVoicePlayers.Add(player);
            player.Stop();
            player.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception)
        {
            // Missing codecs or very short files should not block the scene.
        }
    }

    private MediaPlayer GetNextVoicePlayer(string soundPath, double pitch)
    {
        if (!_voicePlayerPools.TryGetValue(soundPath, out var pool))
        {
            pool = CreateVoicePlayerPool(soundPath, pitch);
            _voicePlayerPools[soundPath] = pool;
        }
        TouchVoicePool(soundPath);
        TrimVoicePools();

        var index = _voicePlayerIndexes.TryGetValue(soundPath, out var currentIndex)
            ? currentIndex
            : 0;
        _voicePlayerIndexes[soundPath] = (index + 1) % pool.Count;

        var player = pool[index % pool.Count];
        player.Volume = _settings.VoiceVolume;
        player.SpeedRatio = pitch;
        return player;
    }

    private List<MediaPlayer> CreateVoicePlayerPool(string soundPath, double pitch)
    {
        var pool = new List<MediaPlayer>(VoicePlayerPoolSize);
        for (var index = 0; index < VoicePlayerPoolSize; index++)
        {
            var player = new MediaPlayer
            {
                Volume = _settings.VoiceVolume,
                SpeedRatio = pitch,
            };
            player.MediaEnded += (_, _) => _activeVoicePlayers.Remove(player);
            player.MediaFailed += (_, _) => _activeVoicePlayers.Remove(player);
            player.Open(new Uri(soundPath, UriKind.Absolute));
            pool.Add(player);
        }

        return pool;
    }

    private void TouchVoicePool(string soundPath)
    {
        var node = _voicePoolLru.Find(soundPath);
        if (node is not null)
        {
            _voicePoolLru.Remove(node);
        }
        _voicePoolLru.AddLast(soundPath);
    }

    private void TrimVoicePools()
    {
        var attempts = 0;
        while (_voicePlayerPools.Count > MaxVoiceSoundPools
            && _voicePoolLru.First is not null
            && attempts++ < _voicePlayerPools.Count)
        {
            var soundPath = _voicePoolLru.First.Value;
            _voicePoolLru.RemoveFirst();
            if (!_voicePlayerPools.TryGetValue(soundPath, out var pool))
            {
                continue;
            }
            if (pool.Any(_activeVoicePlayers.Contains))
            {
                _voicePoolLru.AddLast(soundPath);
                continue;
            }

            foreach (var player in pool)
            {
                player.Stop();
                player.Close();
            }
            _voicePlayerPools.Remove(soundPath);
            _voicePlayerIndexes.Remove(soundPath);
        }
    }

    private void StopAllVoicePlayers(bool close)
    {
        foreach (var player in _voicePlayerPools.Values.SelectMany(pool => pool))
        {
            player.Stop();
            if (close)
            {
                player.Close();
            }
        }

        _activeVoicePlayers.Clear();
        if (close)
        {
            _voicePlayerPools.Clear();
            _voicePlayerIndexes.Clear();
            _voicePoolLru.Clear();
        }
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
        UpdateChoiceAvailability();
        if (_paused)
        {
            _musicPlayer.Pause();
            _transitionPlayer.Pause();
            foreach (var player in _activeVoicePlayers.ToList())
            {
                player.Pause();
            }
        }
        else
        {
            if (_currentMusicPath.Length > 0)
            {
                _musicPlayer.Play();
            }
            _transitionPlayer.Play();
            foreach (var player in _activeVoicePlayers.ToList())
            {
                player.Play();
            }
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
        foreach (var player in _voicePlayerPools.Values.SelectMany(pool => pool))
        {
            player.Volume = _settings.VoiceVolume;
        }
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
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NovelEngine",
            "Saves",
            _saveDirectoryName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string CreateSaveDirectoryName(
        NovelProject project,
        NovelBuildManifest? buildManifest)
    {
        var safeTitle = string.Concat(
            project.Title.Select(
                character => Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character)).Trim();
        if (safeTitle.Length == 0)
        {
            safeTitle = "NovelProject";
        }

        var key = buildManifest?.BuildId;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(ProjectSerializer.ToJson(project))))[..12];
        }
        return $"{safeTitle}-{key[..Math.Min(12, key.Length)]}";
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

        if ((e.Key is Key.Space or Key.Enter) && TrySkipTyping())
        {
            e.Handled = true;
            return;
        }

        if (TryGetChoiceShortcutIndex(e.Key, out var index))
        {
            var buttons = ChoicesPanel.Children.OfType<Button>().ToList();
            if (ChoiceAvailability.CanChooseByShortcut(
                    _choicesReady,
                    _paused,
                    _transitioning,
                    index,
                    buttons.Count))
            {
                buttons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                e.Handled = true;
            }
        }
    }

    private static bool TryGetChoiceShortcutIndex(Key key, out int index)
    {
        if (key is >= Key.D1 and <= Key.D9)
        {
            index = key - Key.D1;
            return true;
        }
        if (key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            index = key - Key.NumPad1;
            return true;
        }

        index = -1;
        return false;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TrySkipTyping())
        {
            e.Handled = true;
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _musicPlayer.Stop();
        _musicPlayer.Close();
        _transitionPlayer.Stop();
        _transitionPlayer.Close();
        StopAllVoicePlayers(close: true);
        _dialogueCts?.Cancel();
    }

    private sealed record CharacterVoice(
        IReadOnlyList<string> SoundPaths,
        double Pitch,
        int EveryNthCharacter);
}
