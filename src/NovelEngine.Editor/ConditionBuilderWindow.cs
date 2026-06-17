using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed partial class ConditionBuilderWindow : Window
{
    private readonly ComboBox _modeBox;
    private readonly ComboBox _operatorBox;
    private readonly TextBox _variableBox;
    private readonly ScriptLiteralEditorControl _valueEditor;
    private readonly TextBlock _previewText;

    public ConditionBuilderWindow(string condition)
    {
        Title = "Собрать условие";
        Width = 480;
        Height = 380;
        MinWidth = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

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
        _variableBox = DialogUi.TextBox(string.Empty);
        _valueEditor = new ScriptLiteralEditorControl();
        _previewText = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };
        _previewText.SetResourceReference(ForegroundProperty, "MutedBrush");

        _modeBox.SelectionChanged += (_, _) => UpdateFields();
        _operatorBox.SelectionChanged += (_, _) => UpdatePreview();
        _variableBox.TextChanged += (_, _) => UpdatePreview();
        _valueEditor.LiteralChanged += (_, _) => UpdatePreview();

        Content = CreateContent();
        LoadCondition(condition);
        UpdateFields();
    }

    public string Condition => BuildCondition();

    private UIElement CreateContent()
    {
        var panel = DialogUi.Panel();
        panel.Children.Add(DialogUi.Label("Тип условия"));
        panel.Children.Add(_modeBox);
        panel.Children.Add(DialogUi.Label("Переменная"));
        panel.Children.Add(_variableBox);
        panel.Children.Add(DialogUi.Label("Оператор"));
        panel.Children.Add(_operatorBox);
        panel.Children.Add(DialogUi.Label("Значение"));
        panel.Children.Add(_valueEditor);
        panel.Children.Add(_previewText);
        panel.Children.Add(DialogUi.Buttons(Save, this));
        return panel;
    }

    private void LoadCondition(string condition)
    {
        condition = condition.Trim();
        if (condition.Length == 0)
        {
            SelectMode(ConditionMode.Always);
            _operatorBox.SelectedItem = "==";
            return;
        }

        if (Identifier().IsMatch(condition))
        {
            SelectMode(ConditionMode.VariableTrue);
            _variableBox.Text = condition;
            _operatorBox.SelectedItem = "==";
            return;
        }

        if (condition.StartsWith('!')
            && Identifier().IsMatch(condition[1..]))
        {
            SelectMode(ConditionMode.VariableFalse);
            _variableBox.Text = condition[1..];
            _operatorBox.SelectedItem = "==";
            return;
        }

        var match = Comparison().Match(condition);
        if (match.Success)
        {
            SelectMode(ConditionMode.Comparison);
            _variableBox.Text = match.Groups["name"].Value;
            _operatorBox.SelectedItem = match.Groups["operator"].Value;
            _valueEditor.LoadLiteral(match.Groups["value"].Value.Trim());
            return;
        }

        SelectMode(ConditionMode.Comparison);
        _operatorBox.SelectedItem = "==";
        _valueEditor.LoadLiteral(condition);
    }

    private void SelectMode(ConditionMode mode) =>
        _modeBox.SelectedItem = ModeChoices.First(choice => choice.Mode == mode);

    private void UpdateFields()
    {
        var mode = SelectedMode;
        _variableBox.IsEnabled = mode is not ConditionMode.Always;
        _operatorBox.IsEnabled = mode is ConditionMode.Comparison;
        _valueEditor.IsEnabled = mode is ConditionMode.Comparison;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var condition = BuildCondition();
        _previewText.Text = condition.Length == 0
            ? "Показывать всегда"
            : $"Условие: {condition}";
    }

    private string BuildCondition()
    {
        var variable = _variableBox.Text.Trim();
        return SelectedMode switch
        {
            ConditionMode.Always => string.Empty,
            ConditionMode.VariableTrue => variable,
            ConditionMode.VariableFalse => variable.Length == 0
                ? string.Empty
                : $"!{variable}",
            ConditionMode.Comparison => variable.Length == 0
                ? _valueEditor.Literal
                : $"{variable} {_operatorBox.SelectedItem ?? "=="} {_valueEditor.Literal}",
            _ => string.Empty,
        };
    }

    private ConditionMode SelectedMode =>
        (_modeBox.SelectedItem as ModeChoice)?.Mode
        ?? ConditionMode.Always;

    private void Save()
    {
        var condition = Condition;
        if (SelectedMode is not ConditionMode.Always
            && !Identifier().IsMatch(_variableBox.Text.Trim()))
        {
            MessageBox.Show(
                this,
                "Имя переменной должно начинаться с латинской буквы или _ и содержать только латиницу, цифры и _.",
                "Собрать условие",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }
        if (SelectedMode is ConditionMode.Comparison
            && !_valueEditor.TryValidate(this, "Собрать условие"))
        {
            return;
        }

        try
        {
            _ = NovelScript.Evaluate(condition, new ScriptState());
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

    private static readonly IReadOnlyList<ModeChoice> ModeChoices =
    [
        new(ConditionMode.Always, "Показывать всегда"),
        new(ConditionMode.VariableTrue, "Переменная истинна"),
        new(ConditionMode.VariableFalse, "Переменная ложна"),
        new(ConditionMode.Comparison, "Сравнение"),
    ];

    private static readonly IReadOnlyList<string> Operators =
        ["==", "!=", ">=", "<=", ">", "<"];

    private sealed record ModeChoice(ConditionMode Mode, string Label);

    private enum ConditionMode
    {
        Always,
        VariableTrue,
        VariableFalse,
        Comparison,
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    [GeneratedRegex(
        "^(?<name>[A-Za-z_][A-Za-z0-9_]*)\\s*(?<operator>==|!=|>=|<=|>|<)\\s*(?<value>.+)$")]
    private static partial Regex Comparison();
}
