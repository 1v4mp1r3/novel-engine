using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class MainMenuEditorWindow : Window
{
    private const double StageWidth = 960;
    private const double StageHeight = 540;
    private const double DragUpdateEpsilon = 0.5;
    private const int MaxCachedMainMenuBitmaps = 48;

    private readonly NovelProject _project;
    private readonly string _assetDirectory;
    private readonly MainMenuDesign _design;
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

        Content = BuildLayout();
        RefreshStage();
        SelectElement(_design.Elements.FirstOrDefault());
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
            textBox.TextChanged += (_, _) => ApplyProperties();
        }
        _actionBox.SelectionChanged += (_, _) => ApplyProperties();

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
        var count = _design.Elements.Count(element => element.Kind == kind) + 1;
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

    private void DeleteSelected()
    {
        if (_selected is null)
        {
            return;
        }
        _design.Elements.Remove(_selected);
        RefreshStage();
        SelectElement(_design.Elements.FirstOrDefault());
    }

    private void RefreshStage()
    {
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
        RefreshProperties();
    }

    private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragVisual?.ReleaseMouseCapture();
        _dragVisual = null;
    }

    private void SelectElement(MainMenuElement? element)
    {
        _selected = element;
        _elementList.SelectedItem = element;
        RefreshStage();
        RefreshProperties();
    }

    private void RefreshProperties()
    {
        _refreshingProperties = true;
        try
        {
            var enabled = _selected is not null;
            foreach (var control in new Control[]
            {
                _textBox,
                _imageBox,
                _actionBox,
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
                control.IsEnabled = enabled;
            }
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

    private void ApplyProperties()
    {
        if (_refreshingProperties || _selected is null)
        {
            return;
        }
        _selected.Text = _textBox.Text;
        _selected.Image = _imageBox.Text.Trim();
        if (_actionBox.SelectedItem is MainMenuAction action)
        {
            _selected.Action = action;
        }
        _selected.X = ParseDouble(_xBox.Text, _selected.X);
        _selected.Y = ParseDouble(_yBox.Text, _selected.Y);
        _selected.Width = Math.Max(8, ParseDouble(_widthBox.Text, _selected.Width));
        _selected.Height = Math.Max(8, ParseDouble(_heightBox.Text, _selected.Height));
        _selected.FontSize = Math.Max(6, ParseDouble(_fontSizeBox.Text, _selected.FontSize));
        _selected.Foreground = _foregroundBox.Text.Trim();
        _selected.Background = _backgroundBox.Text.Trim();
        _selected.Border = _borderBox.Text.Trim();
        _selected.CustomStyleCode = _styleCodeBox.Text;
        RefreshStage();
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
        path = _project.ResolveAssetReference(path);
        if (path.Length == 0 || Path.IsPathRooted(path))
        {
            return path;
        }
        return Path.GetFullPath(Path.Combine(_assetDirectory, path));
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

    internal readonly record struct MainMenuElementListStamp(
        int Count,
        int Hash);
}
