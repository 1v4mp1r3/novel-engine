using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class MainMenuEditorWindow : Window
{
    private const double StageWidth = 960;
    private const double StageHeight = 540;
    private const double DragUpdateEpsilon = 0.5;
    private const int MaxCachedMainMenuBitmaps = 48;
    private const int MaxCachedResolvedAssetPaths = 128;

    private readonly NovelProject _project;
    private readonly string _assetDirectory;
    private readonly MainMenuDesign _design;
    private readonly BoundedCache<string, string> _resolvedAssetCache =
        new(MaxCachedResolvedAssetPaths, StringComparer.OrdinalIgnoreCase);
    private readonly BoundedCache<string, ImageSource?> _bitmapCache =
        new(MaxCachedMainMenuBitmaps, StringComparer.OrdinalIgnoreCase);
    private readonly Canvas _stage = new()
    {
        Width = StageWidth,
        Height = StageHeight,
        Background = new SolidColorBrush(Color.FromRgb(8, 12, 18)),
    };
    private readonly ListBox _elementList = new();
    private readonly TextBox _textBox = new();
    private readonly TextBox _imageBox = new();
    private readonly ComboBox _actionBox = new();
    private readonly TextBox _xBox = new();
    private readonly TextBox _yBox = new();
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly TextBox _fontSizeBox = new();
    private readonly TextBox _foregroundBox = new();
    private readonly TextBox _backgroundBox = new();
    private readonly TextBox _borderBox = new();
    private readonly TextBox _styleCodeBox = new();
    private MainMenuElement? _selected;
    private FrameworkElement? _dragVisual;
    private Point _dragStart;
    private double _dragElementStartX;
    private double _dragElementStartY;
    private bool _refreshingProperties;
    private bool _refreshingElementList;
    private MainMenuElementListStamp? _elementListStamp;
    private MainMenuStageStamp? _stageStamp;
    private MainMenuPropertyPanelStamp? _propertyPanelStamp;
    private readonly DispatcherTimer _propertyApplyTimer;

    public MainMenuEditorWindow(
        NovelProject project,
        MainMenuDesign design,
        string assetDirectory)
    {
        _project = project;
        _assetDirectory = assetDirectory;
        _design = design.Clone();
        Title = "Конструктор главного меню";
        Width = 1320;
        Height = 780;
        MinWidth = 1100;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["WindowBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];
        _propertyApplyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _propertyApplyTimer.Tick += (_, _) =>
        {
            _propertyApplyTimer.Stop();
            ApplyProperties();
        };

        Content = BuildLayout();
        RefreshStage();
        SelectElement(FirstElement(_design.Elements));
    }

    public MainMenuDesign Design => _design;

    private UIElement BuildLayout()
    {
        var root = new DockPanel();
        root.Children.Add(BuildFooter());

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.Children.Add(layout);

        var toolbox = BuildToolbox();
        Grid.SetColumn(toolbox, 0);
        layout.Children.Add(toolbox);

        var stageBorder = new Border
        {
            Margin = new Thickness(16),
            Background = new SolidColorBrush(Color.FromRgb(10, 15, 22)),
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            BorderThickness = new Thickness(1),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = _stage,
            },
        };
        Grid.SetColumn(stageBorder, 1);
        layout.Children.Add(stageBorder);

        var properties = BuildProperties();
        Grid.SetColumn(properties, 2);
        layout.Children.Add(properties);
        return root;
    }

    private UIElement BuildFooter()
    {
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(
            new Button
            {
                Content = "Отмена",
                MinWidth = 96,
                IsCancel = true,
            });
        footer.Children.Add(
            new Button
            {
                Content = "Сохранить",
                MinWidth = 110,
                IsDefault = true,
                Background = new SolidColorBrush(Color.FromRgb(48, 91, 85)),
            });
        ((Button)footer.Children[^1]).Click += (_, _) =>
        {
            FlushPendingPropertyChanges();
            DialogResult = true;
            Close();
        };
        return footer;
    }

    private UIElement BuildToolbox()
    {
        var panel = new DockPanel
        {
            Margin = new Thickness(12),
        };
        panel.Children.Add(
            new TextBlock
            {
                Text = "TOOLBOX",
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(4, 6, 0, 14),
            });
        DockPanel.SetDock(panel.Children[0], Dock.Top);

        var addPanel = new StackPanel();
        addPanel.Children.Add(CreateToolButton("+ Label", MainMenuElementKind.Label));
        addPanel.Children.Add(CreateToolButton("+ Button", MainMenuElementKind.Button));
        addPanel.Children.Add(CreateToolButton("+ Image Label", MainMenuElementKind.ImageLabel));
        addPanel.Children.Add(CreateToolButton("+ Image Button", MainMenuElementKind.ImageButton));
        addPanel.Children.Add(
            new Button
            {
                Content = "Удалить выбранный",
                Background = new SolidColorBrush(Color.FromRgb(74, 41, 50)),
                Margin = new Thickness(0, 10, 7, 14),
            });
        ((Button)addPanel.Children[^1]).Click += (_, _) => DeleteSelected();
        DockPanel.SetDock(addPanel, Dock.Top);
        panel.Children.Add(addPanel);

        _elementList.SelectionChanged += (_, _) =>
        {
            if (_refreshingElementList)
            {
                return;
            }
            if (_elementList.SelectedItem is MainMenuElement element)
            {
                SelectElement(element);
            }
        };
        panel.Children.Add(_elementList);
        return panel;
    }

    private Button CreateToolButton(string text, MainMenuElementKind kind)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => AddElement(kind);
        return button;
    }

    private UIElement BuildProperties()
    {
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = (Brush)Application.Current.Resources["PanelBrush"],
        };
        var panel = new StackPanel
        {
            Margin = new Thickness(18),
        };
        scroll.Content = panel;

        panel.Children.Add(
            new TextBlock
            {
                Text = "СВОЙСТВА ЭЛЕМЕНТА",
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 14),
            });
        AddLabeled(panel, "Text", _textBox);
        AddImagePicker(panel);
        _actionBox.ItemsSource = Enum.GetValues<MainMenuAction>();
        AddLabeled(panel, "Action", _actionBox);
        AddLabeled(panel, "X", _xBox);
        AddLabeled(panel, "Y", _yBox);
        AddLabeled(panel, "Width", _widthBox);
        AddLabeled(panel, "Height", _heightBox);
        AddLabeled(panel, "Font size", _fontSizeBox);
        AddLabeled(panel, "Text color", _foregroundBox);
        AddLabeled(panel, "Background", _backgroundBox);
        AddLabeled(panel, "Border", _borderBox);
        _styleCodeBox.AcceptsReturn = true;
        _styleCodeBox.Height = 110;
        _styleCodeBox.FontFamily = new FontFamily("Cascadia Mono");
        AddLabeled(panel, "Style code", _styleCodeBox);

        foreach (var textBox in new[]
        {
            _textBox,
            _imageBox,
            _xBox,
            _yBox,
            _widthBox,
            _heightBox,
            _fontSizeBox,
            _foregroundBox,
            _backgroundBox,
            _borderBox,
            _styleCodeBox,
        })
        {
            textBox.TextChanged += (_, _) => ScheduleApplyProperties();
        }
        _actionBox.SelectionChanged += (_, _) => FlushPendingPropertyChanges();

        return scroll;
    }

    private void AddImagePicker(Panel panel)
    {
        panel.Children.Add(Label("Image"));
        var dock = new DockPanel { Margin = new Thickness(0, 4, 0, 10) };
        var browse = new Button
        {
            Content = "...",
            Width = 42,
            Margin = new Thickness(7, 0, 0, 0),
        };
        browse.Click += (_, _) => BrowseElementImage();
        DockPanel.SetDock(browse, Dock.Right);
        dock.Children.Add(browse);
        dock.Children.Add(_imageBox);
        panel.Children.Add(dock);
    }

    private static void AddLabeled(Panel panel, string label, Control control)
    {
        panel.Children.Add(Label(label));
        control.Margin = new Thickness(0, 4, 0, 10);
        panel.Children.Add(control);
    }

    private static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            FontSize = 12,
        };

    private void AddElement(MainMenuElementKind kind)
    {
        var count = CountElementsOfKind(kind) + 1;
        var id = $"{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..18];
        var isButton = kind is MainMenuElementKind.Button or MainMenuElementKind.ImageButton;
        var element = new MainMenuElement
        {
            Id = id,
            Kind = kind,
            Action = isButton ? MainMenuAction.None : MainMenuAction.None,
            Text = kind switch
            {
                MainMenuElementKind.Label => $"Label {count}",
                MainMenuElementKind.ImageLabel => $"Image Label {count}",
                _ => $"Button {count}",
            },
            X = 140 + count * 22,
            Y = 160 + count * 22,
            Width = isButton ? 260 : 320,
            Height = isButton ? 58 : 72,
            Background = isButton ? "#D025354A" : "Transparent",
        };
        _design.Elements.Add(element);
        RefreshStage();
        SelectElement(element);
    }

    private int CountElementsOfKind(MainMenuElementKind kind)
    {
        var count = 0;
        for (var index = 0; index < _design.Elements.Count; index++)
        {
            if (_design.Elements[index].Kind == kind)
            {
                count++;
            }
        }

        return count;
    }

    private void DeleteSelected()
    {
        if (_selected is null)
        {
            return;
        }
        _propertyApplyTimer.Stop();
        _design.Elements.Remove(_selected);
        RefreshStage();
        SelectElement(FirstElement(_design.Elements));
    }

    private void RefreshStage()
    {
        var stamp = CreateStageStamp(_design, _selected?.Id);
        if (_stageStamp == stamp && _stage.Children.Count > 0)
        {
            RefreshElementList();
            return;
        }

        _stageStamp = stamp;
        _stage.Children.Clear();
        var background = ResolveAsset(_design.Background);
        if (background.Length > 0 && File.Exists(background))
        {
            _stage.Background = new ImageBrush(LoadBitmapCached(background))
            {
                Stretch = Stretch.UniformToFill,
            };
        }
        else
        {
            _stage.Background = new LinearGradientBrush(
                Color.FromRgb(24, 38, 55),
                Color.FromRgb(7, 11, 17),
                45);
        }

        foreach (var element in _design.Elements)
        {
            var visual = CreateElementVisual(element);
            Canvas.SetLeft(visual, element.X);
            Canvas.SetTop(visual, element.Y);
            _stage.Children.Add(visual);
        }
        RefreshElementList();
    }

    internal static MainMenuStageStamp CreateStageStamp(
        MainMenuDesign design,
        string? selectedElementId)
    {
        var hash = new HashCode();
        hash.Add(design.Background, StringComparer.Ordinal);
        hash.Add(selectedElementId, StringComparer.Ordinal);
        foreach (var element in design.Elements)
        {
            hash.Add(element.Id, StringComparer.Ordinal);
            hash.Add(element.Kind);
            hash.Add(element.Text, StringComparer.Ordinal);
            hash.Add(element.Image, StringComparer.Ordinal);
            hash.Add(element.X);
            hash.Add(element.Y);
            hash.Add(element.Width);
            hash.Add(element.Height);
            hash.Add(element.FontFamily, StringComparer.Ordinal);
            hash.Add(element.FontSize);
            hash.Add(element.Foreground, StringComparer.Ordinal);
            hash.Add(element.Background, StringComparer.Ordinal);
            hash.Add(element.Border, StringComparer.Ordinal);
            hash.Add(element.CustomStyleCode, StringComparer.Ordinal);
        }
        return new MainMenuStageStamp(
            selectedElementId,
            design.Elements.Count,
            hash.ToHashCode());
    }

    private void RefreshElementList()
    {
        var stamp = CreateElementListStamp(_design.Elements);
        _refreshingElementList = true;
        try
        {
            if (_elementListStamp != stamp
                || !ReferenceEquals(_elementList.ItemsSource, _design.Elements))
            {
                if (ReferenceEquals(_elementList.ItemsSource, _design.Elements))
                {
                    _elementList.Items.Refresh();
                }
                else
                {
                    _elementList.DisplayMemberPath = nameof(MainMenuElement.Text);
                    _elementList.ItemsSource = _design.Elements;
                }
                _elementListStamp = stamp;
            }

            if (_selected is not null)
            {
                _elementList.SelectedItem = _selected;
            }
        }
        finally
        {
            _refreshingElementList = false;
        }
    }

    internal static MainMenuElementListStamp CreateElementListStamp(
        IEnumerable<MainMenuElement> elements)
    {
        var hash = new HashCode();
        var count = 0;
        foreach (var element in elements)
        {
            count++;
            hash.Add(element.Id, StringComparer.Ordinal);
            hash.Add(element.Text, StringComparer.Ordinal);
        }
        return new MainMenuElementListStamp(count, hash.ToHashCode());
    }

    private FrameworkElement CreateElementVisual(MainMenuElement element)
    {
        var style = MainMenuElementStyle.From(
            element,
            Color.FromArgb(208, 37, 53, 74),
            Color.FromRgb(80, 101, 127));
        var root = new Border
        {
            Tag = element,
            Width = element.Width,
            Height = element.Height,
            BorderThickness = new Thickness(1),
            BorderBrush = style.Border,
            Background = style.Background,
            CornerRadius = style.CornerRadius,
            Opacity = style.Opacity,
            Child = CreateElementContent(element, style),
        };
        if (ReferenceEquals(element, _selected))
        {
            root.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 190, 80));
            root.BorderThickness = new Thickness(2);
        }
        root.MouseLeftButtonDown += Element_MouseLeftButtonDown;
        root.MouseMove += Element_MouseMove;
        root.MouseLeftButtonUp += Element_MouseLeftButtonUp;
        return root;
    }

    private UIElement CreateElementContent(MainMenuElement element, MainMenuElementStyle style)
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

    private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: MainMenuElement element } visual)
        {
            return;
        }
        SelectElement(element);
        _dragVisual = visual;
        _dragStart = e.GetPosition(_stage);
        _dragElementStartX = element.X;
        _dragElementStartY = element.Y;
        visual.CaptureMouse();
        e.Handled = true;
    }

    private void Element_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragVisual?.Tag is not MainMenuElement element
            || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }
        var position = e.GetPosition(_stage);
        var nextPosition = CalculateDraggedElementPosition(
            element,
            _dragStart,
            new Point(_dragElementStartX, _dragElementStartY),
            position);
        if (!HasMeaningfulPositionChange(element, nextPosition))
        {
            return;
        }

        element.X = nextPosition.X;
        element.Y = nextPosition.Y;
        Canvas.SetLeft(_dragVisual, element.X);
        Canvas.SetTop(_dragVisual, element.Y);
        RefreshDraggedPositionProperties(element);
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragVisual?.ReleaseMouseCapture();
        _dragVisual = null;
    }

    private void SelectElement(MainMenuElement? element)
    {
        if (!ReferenceEquals(_selected, element))
        {
            FlushPendingPropertyChanges();
        }

        _selected = element;
        _elementList.SelectedItem = element;
        RefreshStage();
        RefreshProperties();
    }

    private static MainMenuElement? FirstElement(IReadOnlyList<MainMenuElement> elements)
    {
        if (elements.Count == 0)
        {
            return null;
        }

        return elements[0];
    }

    private void ScheduleApplyProperties()
    {
        if (_refreshingProperties)
        {
            return;
        }

        _propertyApplyTimer.Stop();
        _propertyApplyTimer.Start();
    }

    private void FlushPendingPropertyChanges()
    {
        _propertyApplyTimer.Stop();
        ApplyProperties();
    }

    private void RefreshProperties()
    {
        var stamp = CreatePropertyPanelStamp(_selected);
        if (_propertyPanelStamp == stamp)
        {
            return;
        }

        _propertyPanelStamp = stamp;
        _refreshingProperties = true;
        try
        {
            var enabled = _selected is not null;
            SetPropertyControlsEnabled(enabled);
            if (_selected is null)
            {
                return;
            }

            _textBox.Text = _selected.Text;
            _imageBox.Text = _selected.Image;
            _actionBox.SelectedItem = _selected.Action;
            _xBox.Text = Number(_selected.X);
            _yBox.Text = Number(_selected.Y);
            _widthBox.Text = Number(_selected.Width);
            _heightBox.Text = Number(_selected.Height);
            _fontSizeBox.Text = Number(_selected.FontSize);
            _foregroundBox.Text = _selected.Foreground;
            _backgroundBox.Text = _selected.Background;
            _borderBox.Text = _selected.Border;
            _styleCodeBox.Text = _selected.CustomStyleCode;
        }
        finally
        {
            _refreshingProperties = false;
        }
    }

    private void RefreshDraggedPositionProperties(MainMenuElement element)
    {
        if (!ReferenceEquals(_selected, element))
        {
            return;
        }

        _refreshingProperties = true;
        try
        {
            _xBox.Text = Number(element.X);
            _yBox.Text = Number(element.Y);
            _propertyPanelStamp = CreatePropertyPanelStamp(element);
        }
        finally
        {
            _refreshingProperties = false;
        }
    }

    private void SetPropertyControlsEnabled(bool enabled)
    {
        _textBox.IsEnabled = enabled;
        _imageBox.IsEnabled = enabled;
        _actionBox.IsEnabled = enabled;
        _xBox.IsEnabled = enabled;
        _yBox.IsEnabled = enabled;
        _widthBox.IsEnabled = enabled;
        _heightBox.IsEnabled = enabled;
        _fontSizeBox.IsEnabled = enabled;
        _foregroundBox.IsEnabled = enabled;
        _backgroundBox.IsEnabled = enabled;
        _borderBox.IsEnabled = enabled;
        _styleCodeBox.IsEnabled = enabled;
    }

    private void ApplyProperties()
    {
        if (_refreshingProperties || _selected is null)
        {
            return;
        }
        var text = _textBox.Text;
        var image = _imageBox.Text.Trim();
        var action = _actionBox.SelectedItem is MainMenuAction selectedAction
            ? selectedAction
            : _selected.Action;
        var x = ParseDouble(_xBox.Text, _selected.X);
        var y = ParseDouble(_yBox.Text, _selected.Y);
        var width = Math.Max(8, ParseDouble(_widthBox.Text, _selected.Width));
        var height = Math.Max(8, ParseDouble(_heightBox.Text, _selected.Height));
        var fontSize = Math.Max(6, ParseDouble(_fontSizeBox.Text, _selected.FontSize));
        var foreground = _foregroundBox.Text.Trim();
        var background = _backgroundBox.Text.Trim();
        var border = _borderBox.Text.Trim();
        var customStyleCode = _styleCodeBox.Text;
        if (!HasElementPropertyChanges(
                _selected,
                text,
                image,
                action,
                x,
                y,
                width,
                height,
                fontSize,
                foreground,
                background,
                border,
                customStyleCode))
        {
            return;
        }

        var refreshStage = HasRenderedElementPropertyChanges(
            _selected,
            text,
            image,
            x,
            y,
            width,
            height,
            fontSize,
            foreground,
            background,
            border,
            customStyleCode);

        _selected.Text = text;
        _selected.Image = image;
        _selected.Action = action;
        _selected.X = x;
        _selected.Y = y;
        _selected.Width = width;
        _selected.Height = height;
        _selected.FontSize = fontSize;
        _selected.Foreground = foreground;
        _selected.Background = background;
        _selected.Border = border;
        _selected.CustomStyleCode = customStyleCode;
        _propertyPanelStamp = CreatePropertyPanelStamp(_selected);
        if (refreshStage)
        {
            RefreshStage();
        }
    }

    private void BrowseElementImage()
    {
        if (_selected is null)
        {
            return;
        }
        var dialog = new OpenFileDialog
        {
            Title = "Выберите изображение элемента меню",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif|Все файлы|*.*",
            InitialDirectory = _assetDirectory,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _imageBox.Text = MakeRelative(dialog.FileName);
        }
    }

    private string MakeRelative(string path)
    {
        if (!Path.IsPathRooted(path))
        {
            return path;
        }
        return Path.GetRelativePath(_assetDirectory, path);
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
        if (resolved.Length > 0 && !Path.IsPathRooted(resolved))
        {
            resolved = Path.GetFullPath(Path.Combine(_assetDirectory, resolved));
        }
        _resolvedAssetCache.Set(path, resolved);
        return resolved;
    }

    private ImageSource? LoadBitmapCached(string path)
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

    private static ImageSource? LoadBitmap(string path)
    {
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

    private static double ParseDouble(string text, double fallback) =>
        double.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : fallback;

    private static string Number(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static Point CalculateDraggedElementPosition(
        MainMenuElement element,
        Point dragStart,
        Point elementStart,
        Point cursorPosition) =>
        new(
            Math.Clamp(
                elementStart.X + cursorPosition.X - dragStart.X,
                0,
                StageWidth - element.Width),
            Math.Clamp(
                elementStart.Y + cursorPosition.Y - dragStart.Y,
                0,
                StageHeight - element.Height));

    internal static bool HasMeaningfulPositionChange(
        MainMenuElement element,
        Point nextPosition) =>
        Math.Abs(element.X - nextPosition.X) >= DragUpdateEpsilon
            || Math.Abs(element.Y - nextPosition.Y) >= DragUpdateEpsilon;

    internal static bool HasElementPropertyChanges(
        MainMenuElement element,
        string text,
        string image,
        MainMenuAction action,
        double x,
        double y,
        double width,
        double height,
        double fontSize,
        string foreground,
        string background,
        string border,
        string customStyleCode) =>
        element.Action != action
            || HasRenderedElementPropertyChanges(
                element,
                text,
                image,
                x,
                y,
                width,
                height,
                fontSize,
                foreground,
                background,
                border,
                customStyleCode);

    internal static bool HasRenderedElementPropertyChanges(
        MainMenuElement element,
        string text,
        string image,
        double x,
        double y,
        double width,
        double height,
        double fontSize,
        string foreground,
        string background,
        string border,
        string customStyleCode) =>
        !element.Text.Equals(text, StringComparison.Ordinal)
            || !element.Image.Equals(image, StringComparison.Ordinal)
            || element.X != x
            || element.Y != y
            || element.Width != width
            || element.Height != height
            || element.FontSize != fontSize
            || !element.Foreground.Equals(foreground, StringComparison.Ordinal)
            || !element.Background.Equals(background, StringComparison.Ordinal)
            || !element.Border.Equals(border, StringComparison.Ordinal)
            || !element.CustomStyleCode.Equals(customStyleCode, StringComparison.Ordinal);

    internal static MainMenuPropertyPanelStamp CreatePropertyPanelStamp(
        MainMenuElement? element)
    {
        if (element is null)
        {
            return new MainMenuPropertyPanelStamp(null, 0);
        }

        var hash = new HashCode();
        hash.Add(element.Id, StringComparer.Ordinal);
        hash.Add(element.Text, StringComparer.Ordinal);
        hash.Add(element.Image, StringComparer.Ordinal);
        hash.Add(element.Action);
        hash.Add(element.X);
        hash.Add(element.Y);
        hash.Add(element.Width);
        hash.Add(element.Height);
        hash.Add(element.FontSize);
        hash.Add(element.Foreground, StringComparer.Ordinal);
        hash.Add(element.Background, StringComparer.Ordinal);
        hash.Add(element.Border, StringComparer.Ordinal);
        hash.Add(element.CustomStyleCode, StringComparer.Ordinal);
        return new MainMenuPropertyPanelStamp(element.Id, hash.ToHashCode());
    }

    internal readonly record struct MainMenuElementListStamp(
        int Count,
        int Hash);

    internal readonly record struct MainMenuStageStamp(
        string? SelectedElementId,
        int ElementCount,
        int Hash);

    internal readonly record struct MainMenuPropertyPanelStamp(
        string? SelectedElementId,
        int Hash);
}
