using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace NovelEngine.Editor;

internal enum ProjectStartupAction
{
    Create,
    Open,
}

internal sealed class ProjectStartupWindow : Window
{
    public ProjectStartupWindow()
    {
        Title = "Novel Engine";
        Width = 520;
        Height = 320;
        MinWidth = 460;
        MinHeight = 280;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(12, 18, 26));
        Foreground = Brushes.White;
        Content = CreateContent();
    }

    public ProjectStartupAction SelectedAction { get; private set; }

    public string? SelectedDirectory { get; private set; }

    private UIElement CreateContent()
    {
        var root = new Grid
        {
            Margin = new Thickness(28),
        };
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
            Margin = new Thickness(0, 34, 0, 0),
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

        return root;
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

        SelectedAction = action;
        SelectedDirectory = dialog.FolderName;
        DialogResult = true;
    }
}
