using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class VisualScriptBlocksWindow : Window
{
    private readonly List<VisualScriptBlock> _blocks;
    private readonly ListBox _blockList;
    private readonly TextBox _previewBox;
    private readonly TextBlock _summaryText;
    private readonly Button _editButton;
    private readonly Button _duplicateButton;
    private readonly Button _deleteButton;
    private readonly Button _moveUpButton;
    private readonly Button _moveDownButton;

    public VisualScriptBlocksWindow(
        IEnumerable<VisualScriptBlock> blocks,
        string title)
    {
        _blocks = blocks.Select(block => block.Clone()).ToList();
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
        panel.Children.Add(CreateButton("+ add", () => AddBlock(VisualScriptBlockKind.AddVariable)));
        panel.Children.Add(CreateButton("+ unset", () => AddBlock(VisualScriptBlockKind.UnsetVariable)));
        panel.Children.Add(CreateButton("+ comment", () => AddBlock(VisualScriptBlockKind.Comment)));
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

    private static StackPanel CreateButtonPanel() =>
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
            Value = kind == VisualScriptBlockKind.UnsetVariable
                || kind == VisualScriptBlockKind.Comment
                    ? string.Empty
                    : "true",
            Text = kind == VisualScriptBlockKind.Comment ? "Комментарий" : string.Empty,
        };
        var dialog = new VisualScriptBlockEditorWindow(block)
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

        var dialog = new VisualScriptBlockEditorWindow(_blocks[index])
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
            var script = VisualScriptCompiler.Compile([block]);
            return script.Length == 0 ? $"{block.Id}: пустой комментарий" : script;
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
}

public sealed class VisualScriptBlockEditorWindow : Window
{
    private readonly string _blockId;
    private readonly ComboBox _kindBox;
    private readonly TextBox _variableBox;
    private readonly TextBox _valueBox;
    private readonly TextBox _commentBox;

    public VisualScriptBlockEditorWindow(VisualScriptBlock block)
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
        _variableBox = DialogUi.TextBox(block.VariableName);
        _valueBox = DialogUi.TextBox(block.Value);
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
        Value = _valueBox.Text.Trim(),
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
        panel.Children.Add(_valueBox);
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
            or VisualScriptBlockKind.AddVariable;
        _variableBox.IsEnabled = !isComment;
        _valueBox.IsEnabled = needsValue;
        _commentBox.IsEnabled = isComment;
    }

    private void Save()
    {
        try
        {
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
        new(VisualScriptBlockKind.AddVariable, "Прибавить к переменной"),
        new(VisualScriptBlockKind.UnsetVariable, "Удалить переменную"),
        new(VisualScriptBlockKind.Comment, "Комментарий"),
    ];

    private sealed record BlockKindChoice(
        VisualScriptBlockKind Kind,
        string Label);
}
