using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class ConditionBuilderWindow : Window
{
    private readonly ComboBox _modeBox;
    private readonly ComboBox _operatorBox;
    private readonly ComboBox _variableBox;
    private readonly ScriptLiteralEditorControl _valueEditor;
    private readonly StackPanel _basicPanel;
    private readonly StackPanel _groupPanel;
    private readonly ListBox _childrenList;
    private readonly Button _editChildButton;
    private readonly Button _deleteChildButton;
    private readonly Button _moveChildUpButton;
    private readonly Button _moveChildDownButton;
    private readonly TextBlock _previewText;
    private readonly IReadOnlyList<string> _knownVariables;
    private readonly List<VisualConditionExpression> _groupChildren = [];
    private string _rawFallbackCondition = string.Empty;
    private VisualConditionExpression? _rawFallbackExpression;

    public ConditionBuilderWindow(
        string condition,
        IEnumerable<string>? knownVariables = null,
        VisualConditionExpression? expression = null)
    {
        Title = "Собрать условие";
        Width = 560;
        Height = 540;
        MinWidth = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _knownVariables = NormalizeVariables(knownVariables);

        _modeBox = new ComboBox
        {
            ItemsSource = ModeChoices,
            DisplayMemberPath = nameof(ModeChoice.Label),
            Margin = new Thickness(0, 4, 0, 12),
        };
        _operatorBox = new ComboBox
        {
            ItemsSource = Operators,
            Margin = new Thickness(0, 4, 0, 12),
        };
        _variableBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = _knownVariables,
            Margin = new Thickness(0, 4, 0, 12),
        };
        _valueEditor = new ScriptLiteralEditorControl();
        _basicPanel = new StackPanel();
        _groupPanel = new StackPanel
        {
            Visibility = Visibility.Collapsed,
        };
        _childrenList = new ListBox
        {
            DisplayMemberPath = nameof(ConditionChildView.Text),
            Height = 150,
            Margin = new Thickness(0, 4, 0, 8),
        };
        _editChildButton = CreateChildButton("Изменить", EditChild);
        _deleteChildButton = CreateChildButton("Удалить", DeleteChild);
        _moveChildUpButton = CreateChildButton("Выше", MoveChildUp);
        _moveChildDownButton = CreateChildButton("Ниже", MoveChildDown);
        _previewText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };
        _previewText.SetResourceReference(ForegroundProperty, "MutedBrush");

        _modeBox.SelectionChanged += (_, _) => UpdateFields();
        _operatorBox.SelectionChanged += (_, _) => UpdatePreview();
        _variableBox.SelectionChanged += (_, _) => UpdatePreview();
        _variableBox.KeyUp += (_, _) => UpdatePreview();
        _valueEditor.LiteralChanged += (_, _) => UpdatePreview();
        _childrenList.SelectionChanged += (_, _) => UpdateChildButtons();
        _childrenList.MouseDoubleClick += (_, _) => EditChild();
        _childrenList.PreviewKeyDown += (_, e) => HandleChildrenListKey(e);

        Content = CreateContent();
        if (expression is null)
        {
            LoadCondition(condition);
        }
        else
        {
            LoadExpression(expression);
        }
        UpdateFields();
    }

    public string Condition => BuildCondition();
    public VisualConditionExpression Expression => BuildExpression().Clone();

    private UIElement CreateContent()
    {
        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Тип условия"));
        panel.Children.Add(_modeBox);
        _basicPanel.Children.Add(DialogUi.Label("Переменная"));
        _basicPanel.Children.Add(_variableBox);
        _basicPanel.Children.Add(DialogUi.Label("Оператор"));
        _basicPanel.Children.Add(_operatorBox);
        _basicPanel.Children.Add(DialogUi.Label("Значение"));
        _basicPanel.Children.Add(_valueEditor);
        panel.Children.Add(_basicPanel);
        _groupPanel.Children.Add(DialogUi.Label("Условия группы"));
        _groupPanel.Children.Add(_childrenList);
        _groupPanel.Children.Add(CreateChildButtons());
        panel.Children.Add(_groupPanel);
        panel.Children.Add(_previewText);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        return panel;
    }

    private static Button CreateChildButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 95,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private UIElement CreateChildButtons()
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 8),
        };
        var add = CreateChildButton("+ Условие", AddChild);
        panel.Children.Add(add);
        panel.Children.Add(_editChildButton);
        panel.Children.Add(_deleteChildButton);
        panel.Children.Add(_moveChildUpButton);
        panel.Children.Add(_moveChildDownButton);
        return panel;
    }

    private void LoadCondition(string condition)
    {
        try
        {
            var expression = VisualConditionCompiler.Parse(condition);
            LoadExpression(expression);
        }
        catch (InvalidDataException)
        {
            _rawFallbackCondition = condition.Trim();
            _rawFallbackExpression = null;
            SelectMode(VisualConditionKind.Comparison);
            _operatorBox.SelectedItem = "==";
            _valueEditor.LoadLiteral(_rawFallbackCondition);
        }
    }

    private void LoadExpression(VisualConditionExpression expression)
    {
        if (!ModeChoices.Any(choice => choice.Mode == expression.Kind))
        {
            _rawFallbackExpression = expression.Clone();
            _rawFallbackCondition = VisualConditionCompiler.Compile(expression);
            SelectMode(VisualConditionKind.Comparison);
            _operatorBox.SelectedItem = "==";
            _valueEditor.LoadLiteral(_rawFallbackCondition);
            return;
        }

        _rawFallbackExpression = null;
        SelectMode(expression.Kind);
        if (IsGroupMode(expression.Kind))
        {
            _groupChildren.Clear();
            _groupChildren.AddRange(
                expression.Children.Select(child => child.Clone()));
            _variableBox.Text = string.Empty;
            _operatorBox.SelectedItem = "==";
            _valueEditor.LoadLiteral(string.Empty);
            RefreshChildrenList();
            return;
        }

        _groupChildren.Clear();
        RefreshChildrenList();
        _variableBox.Text = expression.VariableName;
        _operatorBox.SelectedItem = expression.Operator;
        _valueEditor.LoadLiteral(expression.Value);
    }

    private void SelectMode(VisualConditionKind mode) =>
        _modeBox.SelectedItem = ModeChoices.First(choice => choice.Mode == mode);

    private void UpdateFields()
    {
        var mode = SelectedMode;
        var groupMode = IsGroupMode(mode);
        _basicPanel.Visibility = groupMode ? Visibility.Collapsed : Visibility.Visible;
        _groupPanel.Visibility = groupMode ? Visibility.Visible : Visibility.Collapsed;
        _variableBox.IsEnabled = mode is not VisualConditionKind.Always;
        _operatorBox.IsEnabled = mode is VisualConditionKind.Comparison;
        _valueEditor.IsEnabled = mode is VisualConditionKind.Comparison;
        UpdateChildButtons();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (IsRawFallbackSelected)
        {
            _previewText.Text =
                $"Текущее условие не входит в визуальные формы: {_rawFallbackCondition}";
            return;
        }

        try
        {
            var condition = VisualConditionCompiler.Compile(BuildExpression());
            _previewText.Text = condition.Length == 0
                ? IsGroupMode(SelectedMode)
                    ? "Добавьте хотя бы одно условие"
                    : "Показывать всегда"
                : $"Условие: {condition}";
        }
        catch (InvalidDataException error)
        {
            _previewText.Text = error.Message;
        }
    }

    private string BuildCondition()
    {
        try
        {
            return VisualConditionCompiler.Compile(BuildExpression());
        }
        catch (InvalidDataException)
        {
            return SelectedMode is VisualConditionKind.Comparison
                && VariableName.Length == 0
                    ? _rawFallbackCondition
                    : string.Empty;
        }
    }

    private VisualConditionKind SelectedMode =>
        (_modeBox.SelectedItem as ModeChoice)?.Mode
        ?? VisualConditionKind.Always;

    private void Save()
    {
        if (SelectedMode is VisualConditionKind.Comparison
            && !IsRawFallbackSelected
            && !_valueEditor.TryValidate(this, "Собрать условие"))
        {
            return;
        }

        try
        {
            VisualConditionCompiler.Validate(BuildExpression());
            DialogResult = true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Собрать условие",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AddChild()
    {
        var dialog = new ConditionBuilderWindow(string.Empty, _knownVariables)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _groupChildren.Add(dialog.Expression);
        RefreshChildrenList(_groupChildren.Count - 1);
    }

    private void EditChild()
    {
        var index = _childrenList.SelectedIndex;
        if (index < 0 || index >= _groupChildren.Count)
        {
            return;
        }

        var child = _groupChildren[index];
        var dialog = new ConditionBuilderWindow(
            VisualConditionCompiler.Compile(child),
            _knownVariables,
            child)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _groupChildren[index] = dialog.Expression;
        RefreshChildrenList(index);
    }

    private void DeleteChild()
    {
        var index = _childrenList.SelectedIndex;
        if (index < 0 || index >= _groupChildren.Count)
        {
            return;
        }

        _groupChildren.RemoveAt(index);
        RefreshChildrenList(Math.Min(index, _groupChildren.Count - 1));
    }

    private void MoveChildUp()
    {
        var index = _childrenList.SelectedIndex;
        if (index <= 0 || index >= _groupChildren.Count)
        {
            return;
        }

        (_groupChildren[index - 1], _groupChildren[index]) =
            (_groupChildren[index], _groupChildren[index - 1]);
        RefreshChildrenList(index - 1);
    }

    private void MoveChildDown()
    {
        var index = _childrenList.SelectedIndex;
        if (index < 0 || index >= _groupChildren.Count - 1)
        {
            return;
        }

        (_groupChildren[index + 1], _groupChildren[index]) =
            (_groupChildren[index], _groupChildren[index + 1]);
        RefreshChildrenList(index + 1);
    }

    private void HandleChildrenListKey(KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EditChild();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            DeleteChild();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Alt)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveChildUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveChildDown();
            e.Handled = true;
        }
    }

    private void RefreshChildrenList(int selectedIndex = -1)
    {
        _childrenList.ItemsSource = _groupChildren
            .Select((child, index) => new ConditionChildView(
                index + 1,
                DescribeChild(child)))
            .ToList();
        if (selectedIndex >= 0 && selectedIndex < _groupChildren.Count)
        {
            _childrenList.SelectedIndex = selectedIndex;
        }
        UpdateChildButtons();
        UpdatePreview();
    }

    private void UpdateChildButtons()
    {
        var selected = _childrenList.SelectedIndex >= 0
            && _childrenList.SelectedIndex < _groupChildren.Count;
        _editChildButton.IsEnabled = selected;
        _deleteChildButton.IsEnabled = selected;
        _moveChildUpButton.IsEnabled = selected && _childrenList.SelectedIndex > 0;
        _moveChildDownButton.IsEnabled =
            selected && _childrenList.SelectedIndex < _groupChildren.Count - 1;
    }

    private static string DescribeChild(VisualConditionExpression expression)
    {
        try
        {
            return VisualConditionCompiler.Compile(expression);
        }
        catch (InvalidDataException error)
        {
            return error.Message;
        }
    }

    private static readonly IReadOnlyList<ModeChoice> ModeChoices =
    [
        new(VisualConditionKind.Always, "Показывать всегда"),
        new(VisualConditionKind.VariableTrue, "Переменная истинна"),
        new(VisualConditionKind.VariableFalse, "Переменная ложна"),
        new(VisualConditionKind.Comparison, "Сравнение"),
        new(VisualConditionKind.All, "Все условия (И)"),
        new(VisualConditionKind.Any, "Любое условие (ИЛИ)"),
    ];

    private static readonly IReadOnlyList<string> Operators =
        VisualConditionCompiler.ComparisonOperators;

    private string VariableName => _variableBox.Text.Trim();

    private bool IsRawFallbackSelected =>
        _rawFallbackCondition.Length > 0
        && SelectedMode is VisualConditionKind.Comparison
        && VariableName.Length == 0;

    private VisualConditionExpression BuildExpression()
    {
        if (IsRawFallbackSelected && _rawFallbackExpression is not null)
        {
            return _rawFallbackExpression.Clone();
        }

        if (IsGroupMode(SelectedMode))
        {
            return new()
            {
                Kind = SelectedMode,
                Children = _groupChildren.Select(child => child.Clone()).ToList(),
            };
        }

        return new()
        {
            Kind = SelectedMode,
            VariableName = VariableName,
            Operator = Convert.ToString(_operatorBox.SelectedItem) ?? "==",
            Value = _valueEditor.Literal,
        };
    }

    private static IReadOnlyList<string> NormalizeVariables(
        IEnumerable<string>? variables) =>
        variables?
            .Where(variable => !string.IsNullOrWhiteSpace(variable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];

    private static bool IsGroupMode(VisualConditionKind mode) =>
        mode is VisualConditionKind.All or VisualConditionKind.Any;

    private sealed record ModeChoice(VisualConditionKind Mode, string Label);

    private sealed record ConditionChildView(int Number, string Condition)
    {
        public string Text => $"{Number}. {Condition}";
    }
}
