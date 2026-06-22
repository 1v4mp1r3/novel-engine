using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using NovelEngine.Core;
using IOPath = System.IO.Path;

namespace NovelEngine.Editor;

public partial class SceneEditorWindow : Window
{
    private const double MinimumScale = 0.1;
    private const double MaximumScale = 5;
    private const double TransformUpdateEpsilon = 0.5;
    private const double ScaleUpdateEpsilon = 0.001;
    private const double RotationUpdateEpsilon = 0.1;
    private const int MaxCachedSceneBitmaps = 64;
    private const int MaxCachedResolvedAssetPaths = 128;

    private readonly NovelProject _project;
    private readonly string _assetDirectory;
    private readonly BoundedCache<string, string> _resolvedAssetCache =
        new(MaxCachedResolvedAssetPaths, StringComparer.OrdinalIgnoreCase);
    private readonly BoundedCache<string, BitmapImage?> _bitmapCache =
        new(MaxCachedSceneBitmaps, StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CharacterVisual> _visuals = [];
    private CharacterPlacement? _selectedCharacter;
    private TransformOperation _operation;
    private Point _operationStart;
    private double _startX;
    private double _startY;
    private double _startScale;
    private double _startRotation;
    private double _startDistance;
    private double _startAngle;
    private bool _transformMode;
    private readonly List<CharacterPlacement> _characters;
    private CharacterListStamp? _characterListStamp;

    public SceneEditorWindow(
        NovelProject project,
        NovelNode node,
        string assetDirectory)
    {
        InitializeComponent();
        _project = project;
        _assetDirectory = assetDirectory;

        var player = new NovelPlayer(project);
        player.StartAt(node.Id);
        _characters = CloneCharacterPlacements(player.State.CurrentCharacters);
        BackgroundImage.Source = LoadBitmapCached(ResolveAsset(player.State.CurrentBackground));

        if (node.InheritCharacters && Characters.Count > 0)
        {
            InheritanceText.Text =
                "Персонажи унаследованы. После применения их состояние будет закреплено в этой ноде.";
            InheritanceText.Visibility = Visibility.Visible;
        }

        RefreshCharacterList();
        BuildCharacterVisuals();
        if (Characters.Count > 0)
        {
            CharacterList.SelectedIndex = 0;
        }
    }

    public IReadOnlyList<CharacterPlacement> Characters => _characters;

    internal static List<CharacterPlacement> CloneCharacterPlacements(
        IReadOnlyList<CharacterPlacement> source)
    {
        var characters = new List<CharacterPlacement>(source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            characters.Add(source[index].Clone());
        }
        return characters;
    }

    internal void EnableTransformModeForScreenshot()
    {
        if (!_transformMode)
        {
            ToggleTransformMode();
        }
    }

    private void BuildCharacterVisuals()
    {
        StageCanvas.Children.Clear();
        _visuals.Clear();
        foreach (var character in _characters)
        {
            var visual = CreateCharacterVisual(character);
            _visuals.Add(character.Id, visual);
            StageCanvas.Children.Add(visual.Root);
            UpdateVisual(visual);
        }
        RefreshEmptyState();
    }

    private CharacterVisual CreateCharacterVisual(CharacterPlacement character)
    {
        var root = new Grid
        {
            Width = CharacterLayout.BaseWidth,
            Height = CharacterLayout.BaseHeight,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Cursor = Cursors.SizeAll,
            Tag = character,
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
                    IsHitTestVisible = false,
                });
        }
        else
        {
            root.Children.Add(CreatePlaceholder(character.Name));
        }

        var outline = new Border
        {
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(3),
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        root.Children.Add(outline);

        var chrome = CreateTransformChrome(character);
        chrome.Visibility = Visibility.Collapsed;
        root.Children.Add(chrome);

        root.MouseLeftButtonDown += Character_MouseLeftButtonDown;
        return new CharacterVisual(character, root, outline, chrome);
    }

    private static FrameworkElement CreatePlaceholder(string name) =>
        new Border
        {
            Width = 330,
            Height = 650,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(Color.FromArgb(215, 40, 54, 74)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(100, 218, 183)),
            BorderThickness = new Thickness(3),
            Child = new TextBlock
            {
                Text = name,
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private Canvas CreateTransformChrome(CharacterPlacement character)
    {
        var chrome = new Canvas
        {
            Width = CharacterLayout.BaseWidth,
            Height = CharacterLayout.BaseHeight,
            Background = Brushes.Transparent,
        };

        AddHandle(chrome, character, -10, -10, Cursors.SizeNWSE);
        AddHandle(
            chrome,
            character,
            CharacterLayout.BaseWidth - 10,
            -10,
            Cursors.SizeNESW);
        AddHandle(
            chrome,
            character,
            -10,
            CharacterLayout.BaseHeight - 10,
            Cursors.SizeNESW);
        AddHandle(
            chrome,
            character,
            CharacterLayout.BaseWidth - 10,
            CharacterLayout.BaseHeight - 10,
            Cursors.SizeNWSE);

        var rotationLine = new Line
        {
            X1 = CharacterLayout.BaseWidth / 2,
            Y1 = 0,
            X2 = CharacterLayout.BaseWidth / 2,
            Y2 = -55,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 3,
            IsHitTestVisible = false,
        };
        chrome.Children.Add(rotationLine);

        var rotateHandle = new Ellipse
        {
            Width = 24,
            Height = 24,
            Fill = new SolidColorBrush(Color.FromRgb(255, 208, 111)),
            Stroke = Brushes.Black,
            StrokeThickness = 2,
            Cursor = Cursors.Hand,
            Tag = character,
        };
        Canvas.SetLeft(rotateHandle, CharacterLayout.BaseWidth / 2 - 12);
        Canvas.SetTop(rotateHandle, -67);
        rotateHandle.MouseLeftButtonDown += RotateHandle_MouseLeftButtonDown;
        chrome.Children.Add(rotateHandle);
        return chrome;
    }

    private void AddHandle(
        Panel chrome,
        CharacterPlacement character,
        double left,
        double top,
        Cursor cursor)
    {
        var handle = new Rectangle
        {
            Width = 20,
            Height = 20,
            Fill = Brushes.White,
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 3,
            Cursor = cursor,
            Tag = character,
        };
        Canvas.SetLeft(handle, left);
        Canvas.SetTop(handle, top);
        handle.MouseLeftButtonDown += ScaleHandle_MouseLeftButtonDown;
        chrome.Children.Add(handle);
    }

    private void Character_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterPlacement character })
        {
            return;
        }

        SelectCharacter(character);
        BeginOperation(TransformOperation.Move, e.GetPosition(StageCanvas));
        Mouse.Capture(StageCanvas);
        e.Handled = true;
    }

    private void ScaleHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterPlacement character })
        {
            return;
        }

        SelectCharacter(character);
        var point = e.GetPosition(StageCanvas);
        BeginOperation(TransformOperation.Scale, point);
        _startDistance = Distance(point, new Point(_startX, _startY));
        Mouse.Capture(StageCanvas);
        e.Handled = true;
    }

    private void RotateHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CharacterPlacement character })
        {
            return;
        }

        SelectCharacter(character);
        var point = e.GetPosition(StageCanvas);
        BeginOperation(TransformOperation.Rotate, point);
        _startAngle = Angle(point, new Point(_startX, _startY));
        Mouse.Capture(StageCanvas);
        e.Handled = true;
    }

    private void BeginOperation(TransformOperation operation, Point point)
    {
        if (_selectedCharacter is null)
        {
            return;
        }
        EnsureCustomTransform(_selectedCharacter);
        _operation = operation;
        _operationStart = point;
        _startX = _selectedCharacter.X;
        _startY = _selectedCharacter.Y;
        _startScale = _selectedCharacter.Scale;
        _startRotation = _selectedCharacter.Rotation;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_operation == TransformOperation.None || _selectedCharacter is null)
        {
            return;
        }

        var point = e.GetPosition(StageCanvas);
        var nextX = _selectedCharacter.X;
        var nextY = _selectedCharacter.Y;
        var nextScale = _selectedCharacter.Scale;
        var nextRotation = _selectedCharacter.Rotation;
        if (_operation == TransformOperation.Move)
        {
            var nextPosition = CalculateMovedCharacterPosition(
                _startX,
                _startY,
                _operationStart,
                point);
            nextX = nextPosition.X;
            nextY = nextPosition.Y;
        }
        else if (_operation == TransformOperation.Scale)
        {
            var distance = Distance(point, new Point(_startX, _startY));
            var factor = _startDistance < 1 ? 1 : distance / _startDistance;
            nextScale = Math.Clamp(
                _startScale * factor,
                MinimumScale,
                MaximumScale);
        }
        else if (_operation == TransformOperation.Rotate)
        {
            var angle = Angle(point, new Point(_startX, _startY));
            nextRotation = NormalizeAngle(_startRotation + angle - _startAngle);
        }

        if (!HasMeaningfulTransformChange(
                _selectedCharacter,
                nextX,
                nextY,
                nextScale,
                nextRotation))
        {
            return;
        }

        _selectedCharacter.X = nextX;
        _selectedCharacter.Y = nextY;
        _selectedCharacter.Scale = nextScale;
        _selectedCharacter.Rotation = nextRotation;
        UpdateSelectedVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_operation == TransformOperation.None)
        {
            return;
        }
        _operation = TransformOperation.None;
        Mouse.Capture(null);
        e.Handled = true;
    }

    private void CharacterList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (CharacterList.SelectedItem is CharacterPlacement character)
        {
            SelectCharacter(character);
        }
    }

    private void SelectCharacter(CharacterPlacement character)
    {
        _selectedCharacter = character;
        if (!ReferenceEquals(CharacterList.SelectedItem, character))
        {
            CharacterList.SelectedItem = character;
        }

        foreach (var visual in _visuals.Values)
        {
            var selected = ReferenceEquals(visual.Character, character);
            visual.Outline.Visibility = selected
                ? Visibility.Visible
                : Visibility.Collapsed;
            visual.Chrome.Visibility = selected && _transformMode
                ? Visibility.Visible
                : Visibility.Collapsed;
            Panel.SetZIndex(visual.Root, selected ? 100 : 0);
        }
        UpdateTransformText();
        UpdateCharacterButtons();
    }

    private void StageRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == StageRoot || e.OriginalSource == StageCanvas)
        {
            ClearSelection();
        }
    }

    private void ClearSelection()
    {
        _selectedCharacter = null;
        CharacterList.SelectedItem = null;
        foreach (var visual in _visuals.Values)
        {
            visual.Outline.Visibility = Visibility.Collapsed;
            visual.Chrome.Visibility = Visibility.Collapsed;
            Panel.SetZIndex(visual.Root, 0);
        }
        TransformText.Text = "Персонаж не выбран";
        UpdateCharacterButtons();
    }

    private void ToggleTransform_Click(object sender, RoutedEventArgs e) =>
        ToggleTransformMode();

    private void ToggleTransformMode()
    {
        _transformMode = !_transformMode;
        ModeText.Text = _transformMode
            ? "Свободная трансформация: углы — масштаб, верхний маркер — поворот"
            : "Режим перемещения";
        if (_selectedCharacter is not null)
        {
            SelectCharacter(_selectedCharacter);
        }
    }

    private void ResetTransform_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
        {
            return;
        }
        _selectedCharacter.HasCustomTransform = false;
        _selectedCharacter.X = CharacterLayout.DefaultCenterX(_selectedCharacter.Position);
        _selectedCharacter.Y = CharacterLayout.DefaultCenterY;
        _selectedCharacter.Scale = 1;
        _selectedCharacter.Rotation = 0;
        UpdateSelectedVisual();
    }

    private void AddLibraryCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Characters.Count == 0)
        {
            MessageBox.Show(
                this,
                "Библиотека персонажей пока пуста.",
                "Редактор сцены",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new CharacterLibraryPickerWindow(_project.Characters)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.SelectedCharacter is null)
        {
            return;
        }

        var character = NovelProject.AddCharacterClone(_characters, dialog.SelectedCharacter);
        EnsureCustomTransform(character);
        RefreshCharacterList();
        BuildCharacterVisuals();
        SelectCharacter(character);
    }

    private void RemoveCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCharacter is null)
        {
            return;
        }

        var removedIndex = _characters.IndexOf(_selectedCharacter);
        _characters.Remove(_selectedCharacter);
        _selectedCharacter = null;
        RefreshCharacterList();
        BuildCharacterVisuals();
        if (_characters.Count > 0)
        {
            CharacterList.SelectedIndex = Math.Clamp(removedIndex, 0, _characters.Count - 1);
        }
        else
        {
            TransformText.Text = "Персонаж не выбран";
            UpdateCharacterButtons();
        }
    }

    private void RefreshCharacterList()
    {
        var stamp = CreateCharacterListStamp(_characters);
        if (_characterListStamp != stamp
            || !ReferenceEquals(CharacterList.ItemsSource, _characters))
        {
            if (ReferenceEquals(CharacterList.ItemsSource, _characters))
            {
                CharacterList.Items.Refresh();
            }
            else
            {
                CharacterList.ItemsSource = _characters;
            }
            _characterListStamp = stamp;
        }
        RefreshEmptyState();
        UpdateCharacterButtons();
    }

    internal static CharacterListStamp CreateCharacterListStamp(
        IEnumerable<CharacterPlacement> characters)
    {
        var hash = new HashCode();
        var count = 0;
        foreach (var character in characters)
        {
            count++;
            hash.Add(character.Id, StringComparer.Ordinal);
            hash.Add(character.Name, StringComparer.Ordinal);
        }
        return new CharacterListStamp(count, hash.ToHashCode());
    }

    private void RefreshEmptyState()
    {
        EmptyStateText.Visibility = _characters.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateCharacterButtons()
    {
        AddLibraryCharacterButton.IsEnabled = _project.Characters.Count > 0;
        RemoveCharacterButton.IsEnabled = _selectedCharacter is not null;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.T && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleTransformMode();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _transformMode)
        {
            ToggleTransformMode();
            e.Handled = true;
            return;
        }
        if (_selectedCharacter is null
            || e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
        {
            return;
        }

        EnsureCustomTransform(_selectedCharacter);
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var nextX = Math.Clamp(
            _selectedCharacter.X
                + (e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0),
            0,
            CharacterLayout.StageWidth);
        var nextY = Math.Clamp(
            _selectedCharacter.Y
                + (e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0),
            0,
            CharacterLayout.StageHeight);
        if (!HasMeaningfulTransformChange(
                _selectedCharacter,
                nextX,
                nextY,
                _selectedCharacter.Scale,
                _selectedCharacter.Rotation))
        {
            e.Handled = true;
            return;
        }

        _selectedCharacter.X = nextX;
        _selectedCharacter.Y = nextY;
        UpdateSelectedVisual();
        e.Handled = true;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void UpdateSelectedVisual()
    {
        if (_selectedCharacter is null
            || !_visuals.TryGetValue(_selectedCharacter.Id, out var visual))
        {
            return;
        }
        UpdateVisual(visual);
        UpdateTransformText();
    }

    private static void UpdateVisual(CharacterVisual visual)
    {
        var character = visual.Character;
        var x = character.HasCustomTransform
            ? character.X
            : CharacterLayout.DefaultCenterX(character.Position);
        var y = character.HasCustomTransform
            ? character.Y
            : CharacterLayout.DefaultCenterY;
        var scale = character.HasCustomTransform ? character.Scale : 1;
        var rotation = character.HasCustomTransform ? character.Rotation : 0;

        Canvas.SetLeft(visual.Root, x - CharacterLayout.BaseWidth / 2);
        Canvas.SetTop(visual.Root, y - CharacterLayout.BaseHeight / 2);
        visual.Root.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(scale, scale),
                new RotateTransform(rotation),
            },
        };
    }

    private void UpdateTransformText()
    {
        if (_selectedCharacter is null)
        {
            TransformText.Text = "Персонаж не выбран";
            return;
        }

        var x = _selectedCharacter.HasCustomTransform
            ? _selectedCharacter.X
            : CharacterLayout.DefaultCenterX(_selectedCharacter.Position);
        var y = _selectedCharacter.HasCustomTransform
            ? _selectedCharacter.Y
            : CharacterLayout.DefaultCenterY;
        var scale = _selectedCharacter.HasCustomTransform
            ? _selectedCharacter.Scale
            : 1;
        var rotation = _selectedCharacter.HasCustomTransform
            ? _selectedCharacter.Rotation
            : 0;
        TransformText.Text =
            $"X {x:0}   Y {y:0}\nМасштаб {scale:0.00}\nПоворот {rotation:0.0}°";
    }

    private static void EnsureCustomTransform(CharacterPlacement character)
    {
        if (character.HasCustomTransform)
        {
            return;
        }
        character.HasCustomTransform = true;
        character.X = CharacterLayout.DefaultCenterX(character.Position);
        character.Y = CharacterLayout.DefaultCenterY;
        character.Scale = 1;
        character.Rotation = 0;
    }

    private string ResolveAsset(string path)
    {
        if (path.Length == 0)
        {
            return string.Empty;
        }
        if (_resolvedAssetCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var resolved = _project.ResolveAssetReference(path);
        if (resolved.Length > 0 && !IOPath.IsPathRooted(resolved))
        {
            resolved = IOPath.GetFullPath(IOPath.Combine(_assetDirectory, resolved));
        }
        _resolvedAssetCache.Set(path, resolved);
        return resolved;
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

    private static double Distance(Point first, Point second) =>
        (first - second).Length;

    private static double Angle(Point point, Point center) =>
        Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;

    private static double NormalizeAngle(double angle)
    {
        angle %= 360;
        return angle > 180 ? angle - 360 : angle < -180 ? angle + 360 : angle;
    }

    internal static Point CalculateMovedCharacterPosition(
        double startX,
        double startY,
        Point operationStart,
        Point cursorPosition)
    {
        var delta = cursorPosition - operationStart;
        return new Point(
            Math.Clamp(startX + delta.X, 0, CharacterLayout.StageWidth),
            Math.Clamp(startY + delta.Y, 0, CharacterLayout.StageHeight));
    }

    internal static bool HasMeaningfulTransformChange(
        CharacterPlacement character,
        double nextX,
        double nextY,
        double nextScale,
        double nextRotation) =>
        Math.Abs(character.X - nextX) >= TransformUpdateEpsilon
            || Math.Abs(character.Y - nextY) >= TransformUpdateEpsilon
            || Math.Abs(character.Scale - nextScale) >= ScaleUpdateEpsilon
            || Math.Abs(NormalizeAngle(character.Rotation - nextRotation))
                >= RotationUpdateEpsilon;

    private sealed record CharacterVisual(
        CharacterPlacement Character,
        Grid Root,
        Border Outline,
        Canvas Chrome);

    internal readonly record struct CharacterListStamp(
        int Count,
        int Hash);

    private enum TransformOperation
    {
        None,
        Move,
        Scale,
        Rotate,
    }
}
