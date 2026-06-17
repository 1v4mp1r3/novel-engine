using System.IO;
using System.Windows;
using System.Windows.Controls;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class ConditionBuilderWindow : Window
{
    private readonly ComboBox _modeBox;
    private readonly ComboBox _operatorBox;
    private readonly ComboBox _variableBox;
    private readonly ScriptLiteralEditorControl _valueEditor;
    private readonly TextBlock _previewText;
    private string _rawFallbackCondition = string.Empty;

    public ConditionBuilderWindow(
        string condition,
        IEnumerable<string>? knownVariables = null)
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
        _variableBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = NormalizeVariables(knownVariables),
            Margin = new Thickness(0, 4, 0, 12),
        };
        _valueEditor = new ScriptLiteralEditorControl();
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
        try
        {
            var expression = VisualConditionCompiler.Parse(condition);
            SelectMode(expression.Kind);
            _variableBox.Text = expression.VariableName;
            _operatorBox.SelectedItem = expression.Operator;
            _valueEditor.LoadLiteral(expression.Value);
        }
        catch (InvalidDataException)
        {
            _rawFallbackCondition = condition.Trim();
            SelectMode(VisualConditionKind.Comparison);
            _operatorBox.SelectedItem = "==";
            _valueEditor.LoadLiteral(_rawFallbackCondition);
        }
    }

    private void SelectMode(VisualConditionKind mode) =>
        _modeBox.SelectedItem = ModeChoices.First(choice => choice.Mode == mode);

    private void UpdateFields()
    {
        var mode = SelectedMode;
        _variableBox.IsEnabled = mode is not VisualConditionKind.Always;
        _operatorBox.IsEnabled = mode is VisualConditionKind.Comparison;
        _valueEditor.IsEnabled = mode is VisualConditionKind.Comparison;
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

    private static readonly IReadOnlyList<ModeChoice> ModeChoices =
    [
        new(VisualConditionKind.Always, "Показывать всегда"),
        new(VisualConditionKind.VariableTrue, "Переменная истинна"),
        new(VisualConditionKind.VariableFalse, "Переменная ложна"),
        new(VisualConditionKind.Comparison, "Сравнение"),
    ];

    private static readonly IReadOnlyList<string> Operators =
        VisualConditionCompiler.ComparisonOperators;

    private string VariableName => _variableBox.Text.Trim();

    private VisualConditionExpression BuildExpression() =>
        new()
        {
            Kind = SelectedMode,
            VariableName = VariableName,
            Operator = Convert.ToString(_operatorBox.SelectedItem) ?? "==",
            Value = _valueEditor.Literal,
        };

    private static IReadOnlyList<string> NormalizeVariables(
        IEnumerable<string>? variables) =>
        variables?
            .Where(variable => !string.IsNullOrWhiteSpace(variable))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? [];

    private sealed record ModeChoice(VisualConditionKind Mode, string Label);
}
