using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelEngine.Editor;

public sealed class GameSettingsWindow : Window
{
    private readonly Slider _musicSlider;
    private readonly Slider _effectsSlider;
    private readonly Slider _voiceSlider;
    private readonly Slider _textDelaySlider;

    public GameSettingsWindow(GameRuntimeSettings settings)
    {
        Title = "Настройки игры";
        Width = 440;
        Height = 360;
        MinWidth = 420;
        MinHeight = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["WindowBrush"];
        Foreground = (Brush)Application.Current.Resources["TextBrush"];

        _musicSlider = CreateSlider(0, 100, settings.MusicVolume * 100);
        _effectsSlider = CreateSlider(0, 100, settings.EffectsVolume * 100);
        _voiceSlider = CreateSlider(0, 100, settings.VoiceVolume * 100);
        _textDelaySlider = CreateSlider(0, 80, settings.TextDelayMs);

        Content = BuildLayout();
    }

    public GameRuntimeSettings Settings =>
        new()
        {
            MusicVolume = _musicSlider.Value / 100d,
            EffectsVolume = _effectsSlider.Value / 100d,
            VoiceVolume = _voiceSlider.Value / 100d,
            TextDelayMs = (int)Math.Round(_textDelaySlider.Value),
        };

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16),
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(
            new Button
            {
                Content = "Отмена",
                MinWidth = 96,
                IsCancel = true,
            });
        var apply = new Button
        {
            Content = "Применить",
            MinWidth = 110,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(48, 91, 85)),
        };
        apply.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };
        footer.Children.Add(apply);
        root.Children.Add(footer);

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
        };
        root.Children.Add(panel);

        panel.Children.Add(CreateSliderRow("Музыка", _musicSlider, "%"));
        panel.Children.Add(CreateSliderRow("Звуки", _effectsSlider, "%"));
        panel.Children.Add(CreateSliderRow("Голоса", _voiceSlider, "%"));
        panel.Children.Add(CreateSliderRow("Задержка текста", _textDelaySlider, " мс"));
        return root;
    }

    private static Slider CreateSlider(double min, double max, double value) =>
        new()
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = max <= 80 ? 5 : 10,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 8, 12, 18),
        };

    private static UIElement CreateSliderRow(string label, Slider slider, string suffix)
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
        };
        Grid.SetColumn(title, 0);
        root.Children.Add(title);

        var value = new TextBlock
        {
            MinWidth = 62,
            TextAlignment = TextAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["MutedBrush"],
            Text = FormatSliderValue(slider.Value, suffix),
        };
        Grid.SetColumn(value, 1);
        root.Children.Add(value);

        slider.ValueChanged += (_, _) =>
        {
            value.Text = FormatSliderValue(slider.Value, suffix);
        };
        Grid.SetRow(slider, 1);
        Grid.SetColumnSpan(slider, 2);
        root.Children.Add(slider);
        return root;
    }

    private static string FormatSliderValue(double value, string suffix) =>
        $"{Math.Round(value)}{suffix}";
}
