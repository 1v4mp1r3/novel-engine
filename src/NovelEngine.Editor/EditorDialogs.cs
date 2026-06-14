using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class OutputEditorWindow : Window
{
    private readonly TextBox _labelBox;
    private readonly TextBox _conditionBox;
    private readonly TextBox _scriptBox;

    public OutputEditorWindow(NodeOutput output)
    {
        Title = "Вариант ответа";
        Width = 560;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _labelBox = DialogUi.TextBox(output.Label);
        _conditionBox = DialogUi.TextBox(output.Condition);
        _scriptBox = DialogUi.TextBox(output.Script, multiline: true);

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Текст варианта"));
        panel.Children.Add(_labelBox);
        panel.Children.Add(DialogUi.Label("Условие показа, например: score >= 3"));
        panel.Children.Add(_conditionBox);
        panel.Children.Add(DialogUi.Label("Скрипт при выборе"));
        _scriptBox.Height = 150;
        panel.Children.Add(_scriptBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string OutputLabel => _labelBox.Text.Trim();
    public string Condition => _conditionBox.Text.Trim();
    public string Script => _scriptBox.Text.Trim();

    public void ApplyTo(NodeOutput output)
    {
        output.Label = OutputLabel;
        output.Condition = Condition;
        output.Script = Script;
    }

    private void Save()
    {
        if (OutputLabel.Length == 0)
        {
            MessageBox.Show(
                this,
                "Текст варианта не может быть пустым.",
                "Вариант ответа",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

public sealed class CharacterEditorWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly TextBox _spriteBox;
    private readonly TextBox _voiceBox;
    private readonly TextBox _voicePitchBox;
    private readonly TextBox _voiceEveryBox;
    private readonly ComboBox _positionBox;
    private readonly string _assetDirectory;

    public CharacterEditorWindow(CharacterPlacement? character, string assetDirectory)
    {
        _assetDirectory = assetDirectory;
        Title = character is null ? "Добавить персонажа" : "Изменить персонажа";
        Width = 560;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _nameBox = DialogUi.TextBox(character?.Name ?? string.Empty);
        _spriteBox = DialogUi.TextBox(character?.Sprite ?? string.Empty);
        _voiceBox = DialogUi.TextBox(character?.VoiceSound ?? string.Empty);
        _voicePitchBox = DialogUi.TextBox(
            (character?.VoicePitch ?? 1).ToString(CultureInfo.InvariantCulture));
        _voiceEveryBox = DialogUi.TextBox(
            (character?.VoiceEveryNthCharacter ?? 2).ToString(CultureInfo.InvariantCulture));
        _positionBox = new ComboBox
        {
            ItemsSource = new[] { "Слева", "По центру", "Справа" },
            SelectedIndex = character?.Position switch
            {
                CharacterPosition.Left => 0,
                CharacterPosition.Right => 2,
                _ => 1,
            },
            Margin = new Thickness(0, 4, 0, 12),
        };

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Имя персонажа"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(DialogUi.Label("Спрайт"));
        var spritePanel = new DockPanel();
        var browse = new Button
        {
            Content = "...",
            Width = 42,
            Margin = new Thickness(7, 4, 0, 12),
        };
        browse.Click += (_, _) => BrowseSprite();
        DockPanel.SetDock(browse, Dock.Right);
        _spriteBox.Margin = new Thickness(0, 4, 0, 12);
        spritePanel.Children.Add(browse);
        spritePanel.Children.Add(_spriteBox);
        panel.Children.Add(spritePanel);
        panel.Children.Add(DialogUi.Label("Позиция"));
        panel.Children.Add(_positionBox);
        panel.Children.Add(DialogUi.Label("Голос персонажа"));
        var voicePanel = new DockPanel();
        var browseVoice = new Button
        {
            Content = "...",
            Width = 42,
            Margin = new Thickness(7, 4, 0, 12),
        };
        browseVoice.Click += (_, _) => BrowseVoice();
        DockPanel.SetDock(browseVoice, Dock.Right);
        _voiceBox.Margin = new Thickness(0, 4, 0, 12);
        voicePanel.Children.Add(browseVoice);
        voicePanel.Children.Add(_voiceBox);
        panel.Children.Add(voicePanel);
        panel.Children.Add(DialogUi.Label("Pitch голоса (0.25 - 4)"));
        panel.Children.Add(_voicePitchBox);
        panel.Children.Add(DialogUi.Label("Озвучивать каждый N-й символ (1 - 12)"));
        panel.Children.Add(_voiceEveryBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string CharacterName => _nameBox.Text.Trim();
    public string Sprite => _spriteBox.Text.Trim();
    public string VoiceSound => _voiceBox.Text.Trim();
    public double VoicePitch => double.TryParse(
        _voicePitchBox.Text,
        NumberStyles.Float,
        CultureInfo.InvariantCulture,
        out var value)
            ? value
            : 1;
    public int VoiceEveryNthCharacter => int.TryParse(
        _voiceEveryBox.Text,
        NumberStyles.Integer,
        CultureInfo.InvariantCulture,
        out var value)
            ? value
            : 2;
    public CharacterPosition Position => _positionBox.SelectedIndex switch
    {
        0 => CharacterPosition.Left,
        2 => CharacterPosition.Right,
        _ => CharacterPosition.Center,
    };

    private void BrowseSprite()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите спрайт персонажа",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Все файлы|*.*",
            InitialDirectory = Directory.Exists(_assetDirectory) ? _assetDirectory : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _spriteBox.Text = dialog.FileName;
        }
    }

    private void BrowseVoice()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите звук голоса персонажа",
            Filter = "Аудио|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.ogg;*.flac|Все файлы|*.*",
            InitialDirectory = Directory.Exists(_assetDirectory) ? _assetDirectory : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _voiceBox.Text = dialog.FileName;
        }
    }

    private void Save()
    {
        if (CharacterName.Length == 0)
        {
            MessageBox.Show(
                this,
                "Укажите имя персонажа.",
                "Персонаж",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (VoicePitch is < 0.25 or > 4)
        {
            MessageBox.Show(
                this,
                "Pitch голоса должен быть от 0.25 до 4.",
                "Персонаж",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (VoiceEveryNthCharacter is < 1 or > 12)
        {
            MessageBox.Show(
                this,
                "Частота голоса должна быть от 1 до 12.",
                "Персонаж",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

public sealed class TransitionEditorWindow : Window
{
    private readonly TextBox _soundBox;
    private readonly TextBox _durationBox;
    private readonly string _assetDirectory;

    public TransitionEditorWindow(NodeOutput output, string assetDirectory)
    {
        _assetDirectory = assetDirectory;
        Title = "Настройка перехода";
        Width = 560;
        Height = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _soundBox = DialogUi.TextBox(output.TransitionSound);
        _durationBox = DialogUi.TextBox(output.FadeDurationMs.ToString());

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Звук перехода"));
        var soundPanel = new DockPanel();
        var browse = new Button
        {
            Content = "...",
            Width = 42,
            Margin = new Thickness(7, 4, 0, 12),
        };
        browse.Click += (_, _) => BrowseSound();
        DockPanel.SetDock(browse, Dock.Right);
        _soundBox.Margin = new Thickness(0, 4, 0, 12);
        soundPanel.Children.Add(browse);
        soundPanel.Children.Add(_soundBox);
        panel.Children.Add(soundPanel);
        panel.Children.Add(DialogUi.Label("Длительность затухания, мс"));
        panel.Children.Add(_durationBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string TransitionSound => _soundBox.Text.Trim();
    public int FadeDurationMs { get; private set; }

    private void BrowseSound()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите звук перехода",
            Filter = "Аудио|*.wav;*.mp3;*.wma;*.aac;*.m4a|Все файлы|*.*",
            InitialDirectory = Directory.Exists(_assetDirectory) ? _assetDirectory : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _soundBox.Text = dialog.FileName;
        }
    }

    private void Save()
    {
        if (!int.TryParse(_durationBox.Text, out var duration)
            || duration is < 0 or > 10_000)
        {
            MessageBox.Show(
                this,
                "Укажите длительность от 0 до 10000 мс.",
                "Переход",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        FadeDurationMs = duration;
        DialogResult = true;
    }
}

public sealed class AssetIdEditorWindow : Window
{
    private readonly TextBox _idBox;

    public AssetIdEditorWindow(string assetId)
    {
        Title = "Переименовать ассет";
        Width = 480;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _idBox = DialogUi.TextBox(assetId);
        _idBox.SelectAll();

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Имя для ссылки @имя"));
        panel.Children.Add(_idBox);
        panel.Children.Add(
            new TextBlock
            {
                Text = "Допустимы буквы, цифры, _, -. Имя не может начинаться с цифры.",
                Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
                TextWrapping = TextWrapping.Wrap,
            });
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string AssetId => _idBox.Text.Trim();

    private void Save()
    {
        if (!AssetReference.IsValidId(AssetId))
        {
            MessageBox.Show(
                this,
                "Укажите корректное имя ассета.",
                "Переименование ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

public sealed class AssetFolderEditorWindow : Window
{
    private readonly TextBox _nameBox;

    public AssetFolderEditorWindow(string title, string initialName = "")
    {
        Title = title;
        Width = 480;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _nameBox = DialogUi.TextBox(initialName);
        _nameBox.SelectAll();

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Имя папки"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string FolderName => _nameBox.Text.Trim();

    private void Save()
    {
        try
        {
            var folder = ProjectAssets.NormalizeFolder(FolderName);
            if (folder.Length == 0 || folder.Contains('/'))
            {
                throw new InvalidDataException(
                    "Укажите одно корректное имя папки.");
            }
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}

public sealed class AssetFolderPickerWindow : Window
{
    private readonly ComboBox _folderBox;

    public AssetFolderPickerWindow(
        IEnumerable<string> folders,
        string currentFolder)
    {
        Title = "Переместить ассет";
        Width = 520;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var values = folders
            .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _folderBox = new ComboBox
        {
            ItemsSource = values,
            SelectedItem = values.FirstOrDefault(
                folder => folder.Equals(
                    currentFolder,
                    StringComparison.OrdinalIgnoreCase)),
            Margin = new Thickness(0, 4, 0, 12),
        };

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Целевая папка"));
        panel.Children.Add(_folderBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string SelectedFolder =>
        _folderBox.SelectedItem as string ?? string.Empty;

    private void Save()
    {
        if (_folderBox.SelectedItem is not string)
        {
            MessageBox.Show(
                this,
                "Выберите папку.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

internal static class DialogUi
{
    public static StackPanel Panel() =>
        new()
        {
            Margin = new Thickness(20),
        };

    public static TextBlock Label(string text) =>
        new()
        {
            Text = text,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
            Margin = new Thickness(0, 6, 0, 0),
        };

    public static TextBox TextBox(string text, bool multiline = false) =>
        new()
        {
            Text = text,
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            VerticalScrollBarVisibility = multiline
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Hidden,
            Margin = new Thickness(0, 4, 0, 12),
        };

    public static FrameworkElement Buttons(Action save, Window window)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = new Button
        {
            Content = "Отмена",
            MinWidth = 100,
            IsCancel = true,
        };
        var confirm = new Button
        {
            Content = "Сохранить",
            MinWidth = 110,
            IsDefault = true,
        };
        confirm.Click += (_, _) => save();
        cancel.Click += (_, _) => window.DialogResult = false;
        panel.Children.Add(cancel);
        panel.Children.Add(confirm);
        return panel;
    }
}
