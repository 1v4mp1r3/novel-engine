using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class VisualScriptBlocksWindow : Window
{
    private readonly List<VisualScriptBlock> _blocks;
    private readonly IReadOnlyList<string> _knownVariables;
    private readonly string _importScript;
    private readonly ListBox _blockList;
    private readonly TextBox _previewBox;
    private readonly TextBlock _summaryText;
    private readonly Button _editButton;
    private readonly Button _duplicateButton;
    private readonly Button _deleteButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;
    private bool _clearImportedScript;

    public VisualScriptBlocksWindow(
        IEnumerable<VisualScriptBlock> blocks,
        string title,
        IEnumerable<string>? knownVariables = null,
        string importScript = "")
    {
        _blocks = blocks.Select(block => block.Clone()).ToList();
        _knownVariables = NormalizeVariables(knownVariables);
        _importScript = importScript;
        Title = title;
        Width = 720;
        Height = 620;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _summaryText = new TextBlock
        {
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["MutedBrush"],
            Margin = new Thickness(0, 0, 0, 8),
        };
        _blockList = new ListBox
        {
            Height = 220,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _blockList.MouseDoubleClick += (_, _) => EditSelectedBlock();
        _blockList.SelectionChanged += (_, _) => UpdateButtons();

        _previewBox = DialogUi.TextBox(string.Empty, multiline: true);
        _previewBox.Height = 120;
        _previewBox.IsReadOnly = true;
        _previewBox.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono");

        _editButton = CreateButton("Изменить", EditSelectedBlock);
        _duplicateButton = CreateButton("Дублировать", DuplicateSelectedBlock);
        _deleteButton = CreateButton("Удалить", DeleteSelectedBlock);
        _moveUpButton = CreateButton("Выше", () => MoveSelectedBlock(-1));
        _moveDownButton = CreateButton("Ниже", () => MoveSelectedBlock(1));

        Content = CreateContent();
        RefreshList();
    }

    public IReadOnlyList<VisualScriptBlock> Blocks =>
        _blocks.Select(block => block.Clone()).ToList();

    public bool ClearImportedScript => _clearImportedScript;

    private UIElement CreateContent()
    {
        var panel = DialogUi.Panel();
        panel.Children.Add(_summaryText);
        panel.Children.Add(_blockList);
        panel.Children.Add(CreateAddButtons());
        panel.Children.Add(CreateEditButtons());
        panel.Children.Add(DialogUi.Label("Скомпилированный NovelScript"));
        panel.Children.Add(_previewBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        return panel;
    }

    private UIElement CreateAddButtons()
    {
        var panel = CreateButtonPanel();
        panel.Children.Add(CreateButton("+ set", () => AddBlock(VisualScriptBlockKind.SetVariable)));
        panel.Children.Add(CreateButton("+ flag on", () => AddBlock(VisualScriptBlockKind.SetFlagTrue)));
        panel.Children.Add(CreateButton("+ flag off", () => AddBlock(VisualScriptBlockKind.SetFlagFalse)));
        panel.Children.Add(CreateButton("+ add", () => AddBlock(VisualScriptBlockKind.AddVariable)));
        panel.Children.Add(CreateButton("+ subtract", () => AddBlock(VisualScriptBlockKind.SubtractVariable)));
        panel.Children.Add(CreateButton("+ multiply", () => AddBlock(VisualScriptBlockKind.MultiplyVariable)));
        panel.Children.Add(CreateButton("+ divide", () => AddBlock(VisualScriptBlockKind.DivideVariable)));
        panel.Children.Add(CreateButton("+ unset", () => AddBlock(VisualScriptBlockKind.UnsetVariable)));
        panel.Children.Add(CreateButton("+ toggle", () => AddBlock(VisualScriptBlockKind.ToggleVariable)));
        panel.Children.Add(CreateButton("+ comment", () => AddBlock(VisualScriptBlockKind.Comment)));
        panel.Children.Add(CreateButton("Импорт из скрипта", ImportFromScript));
        return panel;
    }

    private UIElement CreateEditButtons()
    {
        var panel = CreateButtonPanel();
        panel.Children.Add(_editButton);
        panel.Children.Add(_duplicateButton);
        panel.Children.Add(_moveUpButton);
        panel.Children.Add(_moveDownButton);
        panel.Children.Add(_deleteButton);
        return panel;
    }

    private static WrapPanel CreateButtonPanel() =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8),
        };

    private static Button CreateButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 86,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddBlock(VisualScriptBlockKind kind)
    {
        var block = new VisualScriptBlock
        {
            Id = $"block-{Guid.NewGuid():N}",
            Kind = kind,
            VariableName = kind == VisualScriptBlockKind.Comment ? string.Empty : "flag",
            Value = DefaultValueFor(kind),
            Text = kind == VisualScriptBlockKind.Comment ? "Комментарий" : string.Empty,
        };
        var dialog = new VisualScriptBlockEditorWindow(block, _knownVariables)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _blocks.Add(dialog.Block);
        _blockList.SelectedIndex = _blocks.Count - 1;
        RefreshList();
    }

    private void EditSelectedBlock()
    {
        var index = _blockList.SelectedIndex;
        if (index < 0 || index >= _blocks.Count)
        {
            return;
        }

        var dialog = new VisualScriptBlockEditorWindow(
            _blocks[index],
            _knownVariables)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _blocks[index] = dialog.Block;
        RefreshList(index);
    }

    private void DuplicateSelectedBlock()
    {
        var index = _blockList.SelectedIndex;
        if (index < 0 || index >= _blocks.Count)
        {
            return;
        }

        var duplicate = _blocks[index].Clone();
        duplicate.Id = $"block-{Guid.NewGuid():N}";
        _blocks.Insert(index + 1, duplicate);
        RefreshList(index + 1);
    }

    private void DeleteSelectedBlock()
    {
        var index = _blockList.SelectedIndex;
        if (index < 0 || index >= _blocks.Count)
        {
            return;
        }

        _blocks.RemoveAt(index);
        RefreshList(Math.Min(index, _blocks.Count - 1));
    }

    private void MoveSelectedBlock(int direction)
    {
        var index = _blockList.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _blocks.Count)
        {
            return;
        }

        (_blocks[index], _blocks[target]) = (_blocks[target], _blocks[index]);
        RefreshList(target);
    }

    private void ImportFromScript()
    {
        if (string.IsNullOrWhiteSpace(_importScript))
        {
            MessageBox.Show(
                this,
                "В текстовом скрипте сейчас нет команд для импорта.",
                "Импорт из скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        IReadOnlyList<VisualScriptBlock> imported;
        try
        {
            imported = VisualScriptCompiler.ParseScript(_importScript);
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Импорт из скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (imported.Count == 0)
        {
            MessageBox.Show(
                this,
                "В текстовом скрипте нет поддерживаемых команд.",
                "Импорт из скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_blocks.Count > 0)
        {
            var action = MessageBox.Show(
                this,
                "Заменить текущие блоки импортированными?\n\nДа — заменить, Нет — добавить в конец.",
                "Импорт из скрипта",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (action == MessageBoxResult.Cancel)
            {
                return;
            }
            if (action == MessageBoxResult.Yes)
            {
                _blocks.Clear();
            }
        }

        _blocks.AddRange(imported.Select(block => block.Clone()));
        _clearImportedScript = MessageBox.Show(
            this,
            "Очистить текстовый скрипт после импорта, чтобы команды не выполнились дважды?",
            "Импорт из скрипта",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes) == MessageBoxResult.Yes;
        RefreshList(Math.Max(0, _blocks.Count - imported.Count));
    }


    private void RefreshList(int selectedIndex = -1)
    {
        _blockList.ItemsSource = _blocks
            .Select((block, index) => new BlockView(
                index + 1,
                DisplayBlock(block)))
            .ToList();
        if (selectedIndex >= 0 && selectedIndex < _blocks.Count)
        {
            _blockList.SelectedIndex = selectedIndex;
        }
        UpdatePreview();
        UpdateButtons();
    }

    private void UpdatePreview()
    {
        _summaryText.Text = $"Блоков: {_blocks.Count}";
        try
        {
            _previewBox.Text = VisualScriptCompiler.Compile(_blocks);
        }
        catch (InvalidDataException error)
        {
            _previewBox.Text = error.Message;
        }
    }

    private void UpdateButtons()
    {
        var index = _blockList.SelectedIndex;
        var selected = index >= 0 && index < _blocks.Count;
        _editButton.IsEnabled = selected;
        _duplicateButton.IsEnabled = selected;
        _deleteButton.IsEnabled = selected;
        _moveUpButton.IsEnabled = selected && index > 0;
        _moveDownButton.IsEnabled = selected && index < _blocks.Count - 1;
    }

    private void Save()
    {
        try
        {
            VisualScriptCompiler.Validate(_blocks);
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Блоки скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string DisplayBlock(VisualScriptBlock block)
    {
        try
        {
            return VisualScriptCompiler.Describe(block);
        }
        catch (InvalidDataException error)
        {
            return $"{block.Id}: {error.Message}";
        }
    }

    private sealed record BlockView(int Index, string Text)
    {
        public override string ToString() => $"{Index}. {Text}";
    }

    private static string DefaultValueFor(VisualScriptBlockKind kind) =>
        kind switch
        {
            VisualScriptBlockKind.SetVariable => "true",
            VisualScriptBlockKind.AddVariable
                or VisualScriptBlockKind.SubtractVariable
                or VisualScriptBlockKind.MultiplyVariable
                or VisualScriptBlockKind.DivideVariable => "1",
            _ => string.Empty,
        };

    private static IReadOnlyList<string> NormalizeVariables(
        IEnumerable<string>? variables) =>
        variables?
            .Where(variable => !string.IsNullOrWhiteSpace(variable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];
}

public sealed class VisualScriptBlockEditorWindow : Window
{
    private readonly string _blockId;
    private readonly ComboBox _kindBox;
    private readonly ComboBox _variableBox;
    private readonly ScriptLiteralEditorControl _valueEditor;
    private readonly TextBox _commentBox;

    public VisualScriptBlockEditorWindow(
        VisualScriptBlock block,
        IEnumerable<string>? knownVariables = null)
    {
        _blockId = string.IsNullOrWhiteSpace(block.Id)
            ? $"block-{Guid.NewGuid():N}"
            : block.Id;
        Title = "Блок скрипта";
        Width = 460;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _kindBox = new ComboBox
        {
            ItemsSource = KindChoices,
            DisplayMemberPath = nameof(BlockKindChoice.Label),
            SelectedItem = KindChoices.First(choice => choice.Kind == block.Kind),
            Margin = new Thickness(0, 4, 0, 12),
        };
        _kindBox.SelectionChanged += (_, _) => UpdateFields();
        _variableBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = NormalizeVariables(knownVariables),
            Text = block.VariableName,
            Margin = new Thickness(0, 4, 0, 12),
        };
        _valueEditor = new ScriptLiteralEditorControl();
        _valueEditor.LoadLiteral(block.Value);
        _commentBox = DialogUi.TextBox(block.Text, multiline: true);
        _commentBox.Height = 80;

        Content = CreateContent();
        UpdateFields();
    }

    public VisualScriptBlock Block => new()
    {
        Id = _blockId,
        Kind = SelectedKind,
        VariableName = _variableBox.Text.Trim(),
        Value = _valueEditor.Literal,
        Text = _commentBox.Text.Trim(),
    };

    private VisualScriptBlockKind SelectedKind =>
        (_kindBox.SelectedItem as BlockKindChoice)?.Kind
        ?? VisualScriptBlockKind.SetVariable;

    private UIElement CreateContent()
    {
        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Тип блока"));
        panel.Children.Add(_kindBox);
        panel.Children.Add(DialogUi.Label("Переменная"));
        panel.Children.Add(_variableBox);
        panel.Children.Add(DialogUi.Label("Значение"));
        panel.Children.Add(_valueEditor);
        panel.Children.Add(DialogUi.Label("Комментарий"));
        panel.Children.Add(_commentBox);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        return panel;
    }

    private void UpdateFields()
    {
        var kind = SelectedKind;
        var isComment = kind == VisualScriptBlockKind.Comment;
        var needsValue = kind is VisualScriptBlockKind.SetVariable
            or VisualScriptBlockKind.AddVariable
            or VisualScriptBlockKind.SubtractVariable
            or VisualScriptBlockKind.MultiplyVariable
            or VisualScriptBlockKind.DivideVariable;
        _variableBox.IsEnabled = !isComment;
        _valueEditor.IsEnabled = needsValue;
        _commentBox.IsEnabled = isComment;
    }

    private void Save()
    {
        try
        {
            if ((SelectedKind is VisualScriptBlockKind.SetVariable
                    or VisualScriptBlockKind.AddVariable
                    or VisualScriptBlockKind.SubtractVariable
                    or VisualScriptBlockKind.MultiplyVariable
                    or VisualScriptBlockKind.DivideVariable)
                && !_valueEditor.TryValidate(this, "Блок скрипта"))
            {
                return;
            }
            VisualScriptCompiler.Validate([Block]);
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Блок скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static readonly IReadOnlyList<BlockKindChoice> KindChoices =
    [
        new(VisualScriptBlockKind.SetVariable, "Установить переменную"),
        new(VisualScriptBlockKind.SetFlagTrue, "Включить флаг"),
        new(VisualScriptBlockKind.SetFlagFalse, "Выключить флаг"),
        new(VisualScriptBlockKind.AddVariable, "Прибавить к переменной"),
        new(VisualScriptBlockKind.SubtractVariable, "Уменьшить переменную"),
        new(VisualScriptBlockKind.MultiplyVariable, "Умножить переменную"),
        new(VisualScriptBlockKind.DivideVariable, "Разделить переменную"),
        new(VisualScriptBlockKind.UnsetVariable, "Удалить переменную"),
        new(VisualScriptBlockKind.ToggleVariable, "Переключить флаг"),
        new(VisualScriptBlockKind.Comment, "Комментарий"),
    ];

    private sealed record BlockKindChoice(
        VisualScriptBlockKind Kind,
        string Label);

    private static IReadOnlyList<string> NormalizeVariables(
        IEnumerable<string>? variables) =>
        variables?
            .Where(variable => !string.IsNullOrWhiteSpace(variable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];
}
