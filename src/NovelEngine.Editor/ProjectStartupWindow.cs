using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace NovelEngine.Editor;

internal enum ProjectStartupAction
{
    Create,
    Open,
    OpenRecent,
}

internal sealed class ProjectStartupWindow : Window
{
    private readonly IReadOnlyList<RecentProjectEntry> _recentProjects;

    public ProjectStartupWindow(IEnumerable<RecentProjectEntry>? recentProjects = null)
    {
        _recentProjects = CreateRecentProjectList(recentProjects);
        Title = "Novel Engine";
        Width = 640;
        Height = 560;
        MinWidth = 460;
        MinHeight = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(12, 18, 26));
        Foreground = Brushes.White;
        Content = CreateContent();
    }

    private static IReadOnlyList<RecentProjectEntry> CreateRecentProjectList(
        IEnumerable<RecentProjectEntry>? recentProjects)
    {
        if (recentProjects is null)
        {
            return [];
        }

        if (recentProjects is IReadOnlyList<RecentProjectEntry> list)
        {
            return list;
        }

        var entries = new List<RecentProjectEntry>();
        foreach (var entry in recentProjects)
        {
            entries.Add(entry);
        }

        return entries;
    }

    public ProjectStartupAction SelectedAction { get; private set; }

    public string? SelectedDirectory { get; private set; }

    public string? SelectedPath { get; private set; }

    private UIElement CreateContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(28),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var title = new TextBlock
        {
            Text = "Novel Engine",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        };
        root.Children.Add(title);

        var subtitle = new TextBlock
        {
            Text = "Создайте новый проект или откройте папку существующего.",
            Foreground = new SolidColorBrush(Color.FromRgb(151, 171, 196)),
            TextWrapping = TextWrapping.Wrap,
        };
        Grid.SetRow(subtitle, 1);
        root.Children.Add(subtitle);

        var actions = new Grid
        {
            Margin = new Thickness(0, 28, 0, 24),
        };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);

        var create = CreateActionButton(
            "Создать проект",
            "Выбрать папку и построить структуру files.",
            () => ChooseDirectory(ProjectStartupAction.Create));
        actions.Children.Add(create);

        var open = CreateActionButton(
            "Выбрать папку",
            "Открыть директорию с .novel.json и ассетами.",
            () => ChooseDirectory(ProjectStartupAction.Open));
        Grid.SetColumn(open, 2);
        actions.Children.Add(open);

        var recentPanel = CreateRecentProjectsPanel();
        Grid.SetRow(recentPanel, 3);
        root.Children.Add(recentPanel);

        return root;
    }

    private UIElement CreateRecentProjectsPanel()
    {
        var panel = new DockPanel();
        var heading = new TextBlock
        {
            Text = "Последние проекты",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);

        if (_recentProjects.Count == 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = "Здесь появятся проекты после открытия или создания.",
                    Foreground = new SolidColorBrush(Color.FromRgb(151, 171, 196)),
                    TextWrapping = TextWrapping.Wrap,
                });
            return panel;
        }

        var list = new StackPanel();
        foreach (var entry in _recentProjects)
        {
            list.Children.Add(CreateRecentProjectButton(entry));
        }
        panel.Children.Add(
            new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            });
        return panel;
    }

    private static Button CreateActionButton(
        string title,
        string description,
        Action action)
    {
        var panel = new StackPanel();
        panel.Children.Add(
            new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8),
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 187, 209)),
                TextWrapping = TextWrapping.Wrap,
            });

        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 41, 57)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(65, 90, 119)),
            BorderThickness = new Thickness(1),
            Content = panel,
            Padding = new Thickness(16),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button CreateRecentProjectButton(RecentProjectEntry entry)
    {
        var panel = new StackPanel();
        panel.Children.Add(
            new TextBlock
            {
                Text = entry.DisplayName,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
            });
        panel.Children.Add(
            new TextBlock
            {
                Text = entry.Path,
                Foreground = new SolidColorBrush(Color.FromRgb(151, 171, 196)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(17, 25, 36)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(52, 73, 98)),
            BorderThickness = new Thickness(1),
            Content = panel,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 9, 12, 9),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        button.Click += (_, _) => SelectRecent(entry.Path);
        return button;
    }

    private void ChooseDirectory(ProjectStartupAction action)
    {
        var dialog = new OpenFolderDialog
        {
            Title = action == ProjectStartupAction.Create
                ? "Выберите папку для нового проекта"
                : "Выберите папку с проектом",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        CompleteSelection(action, dialog.FolderName);
    }

    internal void SelectDirectoryForSmoke(ProjectStartupAction action, string directory)
    {
        if (action == ProjectStartupAction.OpenRecent)
        {
            throw new ArgumentException(
                "Use SelectRecent for recent project selections.",
                nameof(action));
        }

        CompleteSelection(action, directory);
    }

    private void CompleteSelection(ProjectStartupAction action, string path)
    {
        SelectedAction = action;
        SelectedDirectory = path;
        SelectedPath = path;
        DialogResult = true;
    }

    private void SelectRecent(string path)
    {
        CompleteSelection(ProjectStartupAction.OpenRecent, path);
    }
}
