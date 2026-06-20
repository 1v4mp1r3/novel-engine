using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class OutputEditorWindow : Window
{
    internal const string EditorTitle = "Выход";
    internal const string LabelCaption = "Текст выхода";
    internal const string ScriptCaption = "Скрипт выхода";
    internal const string ScriptBlocksTitle = "Блоки скрипта выхода";
    internal const string EmptyLabelMessage = "Текст выхода не может быть пустым.";

    private readonly TextBox _labelBox;
    private readonly TextBox _conditionBox;
    private readonly TextBox _scriptBox;
    private readonly IReadOnlyList<string> _knownVariables;
    private readonly List<VisualScriptBlock> _scriptBlocks;
    private readonly Button _scriptBlocksButton;
    private VisualConditionExpression? _conditionExpression;

    public OutputEditorWindow(
        NodeOutput output,
        IEnumerable<string>? knownVariables = null)
    {
        Title = EditorTitle;
        Width = 560;
        Height = 535;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _labelBox = DialogUi.TextBox(output.Label);
        _conditionExpression = output.ConditionExpression?.Clone();
        _conditionBox = DialogUi.TextBox(
            _conditionExpression is null
                ? output.Condition
                : VisualConditionCompiler.Compile(_conditionExpression));
        _conditionBox.TextChanged += (_, _) =>
        {
            if (_conditionExpression is not null
                && !_conditionBox.Text.Trim().Equals(
                    VisualConditionCompiler.Compile(_conditionExpression),
                    StringComparison.Ordinal))
            {
                _conditionExpression = null;
            }
        };
        _scriptBox = DialogUi.TextBox(output.Script, multiline: true);
        _knownVariables = knownVariables?
            .Where(variable => !string.IsNullOrWhiteSpace(variable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()
            ?? [];
        _scriptBlocks = output.ScriptBlocks
            .Select(block => block.Clone())
            .ToList();
        _scriptBlocksButton = new Button
        {
            MinWidth = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _scriptBlocksButton.Click += (_, _) => EditScriptBlocks();

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label(LabelCaption));
        panel.Children.Add(_labelBox);
        panel.Children.Add(DialogUi.Label("Условие показа, например: score >= 3"));
        panel.Children.Add(_conditionBox);
        panel.Children.Add(CreateConditionButtons());
        panel.Children.Add(DialogUi.Label(ScriptCaption));
        _scriptBox.Height = 150;
        panel.Children.Add(_scriptBox);
        panel.Children.Add(_scriptBlocksButton);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
        RefreshScriptBlocksButton();
    }

    private UIElement CreateConditionButtons()
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        var conditionButton = new Button
        {
            Content = "Собрать условие...",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 150,
        };
        conditionButton.Click += (_, _) => BuildCondition();
        panel.Children.Add(conditionButton);

        var clearButton = new Button
        {
            Content = "Сбросить условие",
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 150,
        };
        clearButton.Click += (_, _) => ClearCondition();
        panel.Children.Add(clearButton);

        return panel;
    }

    public string OutputLabel => _labelBox.Text.Trim();
    public string Condition => _conditionBox.Text.Trim();
    public VisualConditionExpression? ConditionExpression =>
        _conditionExpression?.Clone();
    public string Script => _scriptBox.Text.Trim();
    public IReadOnlyList<VisualScriptBlock> ScriptBlocks =>
        _scriptBlocks.Select(block => block.Clone()).ToList();

    public void ApplyTo(NodeOutput output)
    {
        output.Label = OutputLabel;
        output.Condition = Condition;
        output.ConditionExpression =
            _conditionExpression is not null
            && VisualConditionCompiler.Compile(_conditionExpression)
                .Equals(Condition, StringComparison.Ordinal)
                ? _conditionExpression.Clone()
                : null;
        output.Script = Script;
        output.ScriptBlocks.Clear();
        output.ScriptBlocks.AddRange(ScriptBlocks);
    }

    private void EditScriptBlocks()
    {
        var dialog = new VisualScriptBlocksWindow(
            _scriptBlocks,
            ScriptBlocksTitle,
            _knownVariables,
            _scriptBox.Text)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _scriptBlocks.Clear();
        _scriptBlocks.AddRange(dialog.Blocks.Select(block => block.Clone()));
        if (dialog.ClearImportedScript)
        {
            _scriptBox.Text = string.Empty;
        }
        RefreshScriptBlocksButton();
    }

    private void BuildCondition()
    {
        var dialog = new ConditionBuilderWindow(
            _conditionBox.Text,
            _knownVariables,
            _conditionExpression)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            _conditionExpression = dialog.Expression;
            _conditionBox.Text = dialog.Condition;
        }
    }

    private void ClearCondition()
    {
        _conditionExpression = null;
        _conditionBox.Text = string.Empty;
    }

    private void RefreshScriptBlocksButton() =>
        _scriptBlocksButton.Content = $"Блоки скрипта ({_scriptBlocks.Count})";

    private void Save()
    {
        if (OutputLabel.Length == 0)
        {
            MessageBox.Show(
                this,
                EmptyLabelMessage,
                EditorTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }
}

public sealed class CharacterEditorWindow : Window
{
    private readonly bool _requiresSprite;
    private readonly TextBox _nameBox;
    private readonly ComboBox _spriteBox;
    private readonly ListBox _voiceList;
    private readonly TextBox _voicePitchBox;
    private readonly TextBox _voiceEveryBox;
    private readonly ComboBox _positionBox;

    public CharacterEditorWindow(
        CharacterPlacement? character,
        IEnumerable<NovelAsset> spriteAssets,
        IEnumerable<NovelAsset> voiceAssets,
        string? suggestedName = null,
        string? suggestedSprite = null,
        IEnumerable<string>? suggestedVoices = null,
        bool requireSprite = false)
    {
        _requiresSprite = requireSprite;
        Title = character is null ? "Добавить персонажа" : "Изменить персонажа";
        Width = 560;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        _nameBox = DialogUi.TextBox(
            character?.Name ?? suggestedName ?? string.Empty);
        _spriteBox = CreateAssetBox(
            spriteAssets,
            character?.Sprite ?? suggestedSprite ?? string.Empty,
            requireSprite ? "Выберите спрайт" : "Без спрайта");
        var voiceReferences = character is null
            ? suggestedVoices?.ToList() ?? []
            : CharacterVoiceReferences(character);
        _voiceList = CreateAssetList(
            voiceAssets,
            voiceReferences);
        _voicePitchBox = DialogUi.TextBox(
            (character?.VoicePitch ?? 1).ToString(CultureInfo.InvariantCulture));
        _voiceEveryBox = DialogUi.TextBox(
            (character?.VoiceEveryNthCharacter ?? 1).ToString(CultureInfo.InvariantCulture));
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
        panel.Children.Add(DialogUi.Label(
            requireSprite
                ? "Спрайт из files/characters (обязательно)"
                : "Спрайт из files/characters"));
        panel.Children.Add(_spriteBox);
        panel.Children.Add(DialogUi.Label("Позиция"));
        panel.Children.Add(_positionBox);
        panel.Children.Add(DialogUi.Label("Voice-блипы из files/voices (можно несколько)"));
        panel.Children.Add(_voiceList);
        panel.Children.Add(DialogUi.Label("Pitch голоса (0.25 - 4)"));
        panel.Children.Add(_voicePitchBox);
        panel.Children.Add(DialogUi.Label("Озвучивать каждый N-й символ (1 - 12)"));
        panel.Children.Add(_voiceEveryBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public string CharacterName => _nameBox.Text.Trim();
    public string Sprite => (_spriteBox.SelectedItem as AssetChoice)?.Reference ?? string.Empty;
    public IReadOnlyList<string> VoiceSounds => _voiceList.SelectedItems
        .OfType<AssetChoice>()
        .Select(choice => choice.Reference)
        .Where(reference => reference.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
    public string VoiceSound => VoiceSounds.FirstOrDefault() ?? string.Empty;
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
            : 1;
    public CharacterPosition Position => _positionBox.SelectedIndex switch
    {
        0 => CharacterPosition.Left,
        2 => CharacterPosition.Right,
        _ => CharacterPosition.Center,
    };

    private static IReadOnlyList<string> CharacterVoiceReferences(
        CharacterPlacement? character) =>
        character?.GetVoiceSounds() ?? [];

    private static ListBox CreateAssetList(
        IEnumerable<NovelAsset> assets,
        IReadOnlyList<string> currentReferences)
    {
        var selected = currentReferences
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var values = assets
            .OrderBy(asset => asset.Id, StringComparer.CurrentCultureIgnoreCase)
            .Select(asset => new AssetChoice(
                AssetReference.Create(asset.Id),
                $"{asset.Id}  ·  {Path.GetFileName(asset.Path)}"))
            .ToList();
        foreach (var reference in selected.Where(reference =>
            !values.Any(value => value.Reference.Equals(
                reference,
                StringComparison.OrdinalIgnoreCase))))
        {
            values.Add(new AssetChoice(
                reference,
                $"Текущее значение: {reference}"));
        }

        var list = new ListBox
        {
            ItemsSource = values,
            ItemTemplate = CreateCheckedAssetTemplate(),
            SelectionMode = SelectionMode.Multiple,
            Height = 92,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["FieldBrush"],
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (System.Windows.Media.Brush)Application.Current.Resources["BorderBrush"],
            Margin = new Thickness(0, 4, 0, 12),
        };
        foreach (var item in values.Where(value => selected.Contains(
            value.Reference,
            StringComparer.OrdinalIgnoreCase)))
        {
            list.SelectedItems.Add(item);
        }
        return list;
    }

    private static DataTemplate CreateCheckedAssetTemplate()
    {
        var checkbox = new FrameworkElementFactory(typeof(CheckBox));
        checkbox.SetValue(UIElement.IsHitTestVisibleProperty, false);
        checkbox.SetValue(Control.ForegroundProperty, Application.Current.Resources["TextBrush"]);
        checkbox.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 3, 2, 3));
        checkbox.SetBinding(
            ContentControl.ContentProperty,
            new Binding(nameof(AssetChoice.Name)));
        checkbox.SetBinding(
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(ListBoxItem.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(ListBoxItem),
                    1),
            });
        return new DataTemplate { VisualTree = checkbox };
    }

    private static ComboBox CreateAssetBox(
        IEnumerable<NovelAsset> assets,
        string currentReference,
        string emptyLabel)
    {
        var values = assets
            .OrderBy(asset => asset.Id, StringComparer.CurrentCultureIgnoreCase)
            .Select(asset => new AssetChoice(
                AssetReference.Create(asset.Id),
                $"{asset.Id}  ·  {Path.GetFileName(asset.Path)}"))
            .Prepend(new AssetChoice(string.Empty, emptyLabel))
            .ToList();
        if (currentReference.Length > 0
            && !values.Any(value => value.Reference.Equals(
                currentReference,
                StringComparison.OrdinalIgnoreCase)))
        {
            values.Add(new AssetChoice(
                currentReference,
                $"Текущее значение: {currentReference}"));
        }
        return new ComboBox
        {
            ItemsSource = values,
            DisplayMemberPath = nameof(AssetChoice.Name),
            SelectedItem = values.FirstOrDefault(value =>
                    value.Reference.Equals(
                        currentReference,
                        StringComparison.OrdinalIgnoreCase))
                ?? values[0],
            Margin = new Thickness(0, 4, 0, 12),
        };
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
        if (_requiresSprite && Sprite.Length == 0)
        {
            MessageBox.Show(
                this,
                "Выберите спрайт из files/characters. Если список пустой, импортируйте изображение в папку characters через менеджер файлов.",
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

    private sealed record AssetChoice(string Reference, string Name);
}

public sealed class CharacterLibraryPickerWindow : Window
{
    private readonly ListBox _characterList;

    public CharacterLibraryPickerWindow(IEnumerable<CharacterPlacement> characters)
    {
        Title = "Персонаж из библиотеки";
        Width = 520;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var choices = characters
            .OrderBy(character => character.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(character => new CharacterChoice(
                character,
                $"{DisplayName(character)}  ·  {character.Id}  ·  "
                + $"блипы: {character.GetVoiceSounds().Count}"))
            .ToList();
        _characterList = new ListBox
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(CharacterChoice.Name),
            Height = 260,
            Margin = new Thickness(0, 4, 0, 12),
        };
        if (choices.Count > 0)
        {
            _characterList.SelectedIndex = 0;
        }
        _characterList.MouseDoubleClick += (_, _) => Save();

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Выберите персонажа"));
        panel.Children.Add(_characterList);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        Content = panel;
    }

    public CharacterPlacement? SelectedCharacter =>
        (_characterList.SelectedItem as CharacterChoice)?.Character;

    private void Save()
    {
        if (SelectedCharacter is null)
        {
            MessageBox.Show(
                this,
                "Выберите персонажа из библиотеки.",
                "Библиотека персонажей",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private static string DisplayName(CharacterPlacement character) =>
        string.IsNullOrWhiteSpace(character.Name)
            ? character.Id
            : character.Name;

    private sealed record CharacterChoice(CharacterPlacement Character, string Name);
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

public sealed class AssetUsageWindow : Window
{
    private readonly DataGrid? _usageGrid;

    public AssetUsageWindow(NovelAsset asset, IReadOnlyList<AssetUsage> usages)
    {
        Title = $"Где используется @{asset.Id}";
        Width = 680;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var panel = DialogUi.Panel();
        panel.Children.Add(
            new TextBlock
            {
                Text = $"{AssetKindName(asset.Kind)}  @{asset.Id}",
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["TextBrush"],
                Margin = new Thickness(0, 0, 0, 10),
            });

        if (usages.Count == 0)
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = "Ассет сейчас нигде не используется.",
                    Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 12),
                });
        }
        else
        {
            panel.Children.Add(
                new TextBlock
                {
                    Text = $"Найдено мест: {usages.Count}. Двойной клик — перейти.",
                    Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                    Margin = new Thickness(0, 0, 0, 8),
                });
            _usageGrid = CreateUsageGrid(usages);
            _usageGrid.MouseDoubleClick += (_, _) => ConfirmNavigation();
            panel.Children.Add(_usageGrid);
        }

        panel.Children.Add(CreateButtons(this, usages.Count > 0));
        Content = panel;
    }

    public AssetUsage? SelectedUsage =>
        (_usageGrid?.SelectedItem as AssetUsageView)?.Usage;

    private void ConfirmNavigation()
    {
        if (SelectedUsage is not null)
        {
            DialogResult = true;
        }
    }

    private static DataGrid CreateUsageGrid(IReadOnlyList<AssetUsage> usages)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = usages
                .Select(usage => new AssetUsageView(
                    usage,
                    usage.Location,
                    AssetKindName(usage.ExpectedKind),
                    usage.Reference))
                .ToList(),
            Height = 250,
            Margin = new Thickness(0, 0, 0, 8),
            Background = (Brush)Application.Current.Resources["PanelBrush"],
            Foreground = (Brush)Application.Current.Resources["TextBrush"],
            BorderBrush = (Brush)Application.Current.Resources["BorderBrush"],
            RowBackground = (Brush)Application.Current.Resources["PanelBrush"],
            AlternatingRowBackground =
                (Brush)Application.Current.Resources["PanelBrush"],
        };
        grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Место",
                Binding = new System.Windows.Data.Binding(nameof(AssetUsageView.Location)),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
        grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Тип",
                Binding = new System.Windows.Data.Binding(nameof(AssetUsageView.Kind)),
                Width = 120,
            });
        grid.Columns.Add(
            new DataGridTextColumn
            {
                Header = "Ссылка",
                Binding = new System.Windows.Data.Binding(nameof(AssetUsageView.Reference)),
                Width = 130,
            });
        return grid;
    }

    private static FrameworkElement CreateButtons(Window window, bool canNavigate)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var navigate = new Button
        {
            Content = "Перейти",
            MinWidth = 110,
            IsDefault = canNavigate,
            IsEnabled = canNavigate,
        };
        var close = new Button
        {
            Content = "Закрыть",
            MinWidth = 110,
            IsCancel = true,
        };
        navigate.Click += (_, _) => window.DialogResult = true;
        close.Click += (_, _) => window.Close();
        panel.Children.Add(navigate);
        panel.Children.Add(close);
        return panel;
    }

    private static string AssetKindName(AssetKind kind) =>
        kind switch
        {
            AssetKind.Image => "Изображение",
            AssetKind.Audio => "Аудио",
            _ => "Файл",
        };

    private sealed record AssetUsageView(
        AssetUsage Usage,
        string Location,
        string Kind,
        string Reference);
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

public sealed class VoiceBlipEditorWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly Slider _pitchSlider;
    private readonly Slider _volumeSlider;
    private readonly Slider _genderSlider;
    private readonly Slider _toneSlider;
    private readonly Slider _speedSlider;
    private readonly Slider _randomSlider;
    private readonly MediaPlayer _previewPlayer = new();
    private string? _previewPath;

    public VoiceBlipEditorWindow()
    {
        Title = "Создать voice-блип";
        Width = 560;
        Height = 620;
        MinWidth = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _nameBox = DialogUi.TextBox("voice_blip");
        _nameBox.SelectAll();
        _pitchSlider = CreateSlider(50);
        _volumeSlider = CreateSlider(75);
        _genderSlider = CreateSlider(50);
        _toneSlider = CreateSlider(50);
        _speedSlider = CreateSlider(50);
        _randomSlider = CreateSlider(20);

        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Имя блипа"));
        panel.Children.Add(_nameBox);
        panel.Children.Add(
            new TextBlock
            {
                Text = "Тестовый генератор WAV-блипов. Настройки повторяют базовые ручки Dialogue Engine: Pitch, Volume, Gender, Tone, Speed, Random.",
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
            });
        panel.Children.Add(CreateSliderRow("Pitch", _pitchSlider));
        panel.Children.Add(CreateSliderRow("Volume", _volumeSlider));
        panel.Children.Add(CreateSliderRow("Gender", _genderSlider));
        panel.Children.Add(CreateSliderRow("Tone", _toneSlider));
        panel.Children.Add(CreateSliderRow("Speed", _speedSlider));
        panel.Children.Add(CreateSliderRow("Random", _randomSlider));
        panel.Children.Add(CreateButtons());
        Content = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        Closed += (_, _) => CleanupPreview();
    }

    public string BlipName => _nameBox.Text.Trim();

    public VoiceBlipOptions Options => new()
    {
        Pitch = _pitchSlider.Value,
        Volume = _volumeSlider.Value,
        Gender = _genderSlider.Value,
        Tone = _toneSlider.Value,
        Speed = _speedSlider.Value,
        Randomness = _randomSlider.Value,
    };

    private static Slider CreateSlider(double value) =>
        new()
        {
            Minimum = 0,
            Maximum = 100,
            Value = value,
            TickFrequency = 5,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 12, 0),
        };

    private static FrameworkElement CreateSliderRow(string label, Slider slider)
    {
        var valueText = new TextBlock
        {
            Width = 42,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture),
        };
        slider.ValueChanged += (_, _) =>
        {
            valueText.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture);
        };

        var grid = new Grid { Margin = new Thickness(0, 8, 0, 12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.Children.Add(
            new TextBlock
            {
                Text = label,
                Foreground = (Brush)Application.Current.Resources["MutedBrush"],
                VerticalAlignment = VerticalAlignment.Center,
            });
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(valueText, 2);
        grid.Children.Add(valueText);
        return grid;
    }

    private FrameworkElement CreateButtons()
    {
        var panel = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(CreateButton("По умолчанию", ResetDefaults));
        left.Children.Add(CreateButton("Random", Randomize));
        left.Children.Add(CreateButton("Прослушать", Preview));

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = CreateButton("Отмена", () => DialogResult = false);
        cancel.IsCancel = true;
        var save = CreateButton("Создать", Save);
        save.IsDefault = true;
        save.MinWidth = 110;
        right.Children.Add(cancel);
        right.Children.Add(save);

        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        panel.Children.Add(right);
        panel.Children.Add(left);
        return panel;
    }

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 92,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void ResetDefaults()
    {
        _pitchSlider.Value = 50;
        _volumeSlider.Value = 75;
        _genderSlider.Value = 50;
        _toneSlider.Value = 50;
        _speedSlider.Value = 50;
        _randomSlider.Value = 20;
    }

    private void Randomize()
    {
        _pitchSlider.Value = Random.Shared.Next(28, 78);
        _volumeSlider.Value = Random.Shared.Next(55, 91);
        _genderSlider.Value = Random.Shared.Next(25, 76);
        _toneSlider.Value = Random.Shared.Next(15, 90);
        _speedSlider.Value = Random.Shared.Next(30, 86);
        _randomSlider.Value = Random.Shared.Next(5, 58);
    }

    private void Preview()
    {
        try
        {
            CleanupPreview();
            _previewPath = Path.Combine(
                Path.GetTempPath(),
                $"novel-engine-blip-preview-{Guid.NewGuid():N}.wav");
            VoiceBlipGenerator.WriteWaveFile(_previewPath, Options);
            _previewPlayer.Open(new Uri(_previewPath, UriKind.Absolute));
            _previewPlayer.Play();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Предпрослушивание voice-блипа",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void CleanupPreview()
    {
        _previewPlayer.Stop();
        _previewPlayer.Close();
        if (_previewPath is null)
        {
            return;
        }
        try
        {
            if (File.Exists(_previewPath))
            {
                File.Delete(_previewPath);
            }
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException)
        {
        }
        _previewPath = null;
    }

    private void Save()
    {
        if (BlipName.Length == 0)
        {
            MessageBox.Show(
                this,
                "Укажите имя блипа.",
                "Создать voice-блип",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        try
        {
            VoiceBlipGenerator.CreateWaveBytes(Options);
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Создать voice-блип",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
