using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace NovelEngine.Editor;

public sealed class ScriptLiteralEditorControl : StackPanel
{
    private readonly ComboBox _kindBox;
    private readonly TextBox _valueBox;

    public ScriptLiteralEditorControl()
    {
        Orientation = Orientation.Vertical;

        _kindBox = new ComboBox
        {
            ItemsSource = KindChoices,
            DisplayMemberPath = nameof(LiteralKindChoice.Label),
            SelectedItem = KindChoices.First(choice => choice.Kind == ScriptLiteralKind.Raw),
            Margin = new Thickness(0, 4, 0, 8),
        };
        _valueBox = DialogUi.TextBox(string.Empty);
        _valueBox.Margin = new Thickness(0, 0, 0, 12);

        _kindBox.SelectionChanged += (_, _) =>
        {
            UpdateValueBox();
            LiteralChanged?.Invoke(this, EventArgs.Empty);
        };
        _valueBox.TextChanged += (_, _) =>
            LiteralChanged?.Invoke(this, EventArgs.Empty);
        IsEnabledChanged += (_, _) => UpdateValueBox();

        Children.Add(_kindBox);
        Children.Add(_valueBox);
        UpdateValueBox();
    }

    public string Literal =>
        SelectedKind switch
        {
            ScriptLiteralKind.Text =>
                JsonSerializer.Serialize(_valueBox.Text),
            ScriptLiteralKind.Number =>
                _valueBox.Text.Trim(),
            ScriptLiteralKind.True =>
                "true",
            ScriptLiteralKind.False =>
                "false",
            ScriptLiteralKind.Null =>
                "null",
            _ => _valueBox.Text.Trim(),
        };

    public event EventHandler? LiteralChanged;

    public bool TryValidate(Window owner, string title)
    {
        if (SelectedKind is ScriptLiteralKind.Number
            && !double.TryParse(
                _valueBox.Text.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out _))
        {
            MessageBox.Show(
                owner,
                "Число нужно писать через точку, например 3 или 2.5.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (SelectedKind is ScriptLiteralKind.Raw
            && _valueBox.Text.Trim().Length == 0)
        {
            MessageBox.Show(
                owner,
                "Укажите значение или выберите true, false, null, текст либо число.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    public void LoadLiteral(string literal)
    {
        literal = literal.Trim();
        if (literal.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            SelectKind(ScriptLiteralKind.True);
            return;
        }
        if (literal.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            SelectKind(ScriptLiteralKind.False);
            return;
        }
        if (literal.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            SelectKind(ScriptLiteralKind.Null);
            return;
        }
        if (double.TryParse(
            literal,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _))
        {
            SelectKind(ScriptLiteralKind.Number);
            _valueBox.Text = literal;
            return;
        }
        if (TryReadJsonString(literal, out var text))
        {
            SelectKind(ScriptLiteralKind.Text);
            _valueBox.Text = text;
            return;
        }

        SelectKind(ScriptLiteralKind.Raw);
        _valueBox.Text = literal;
    }

    private void SelectKind(ScriptLiteralKind kind)
    {
        _kindBox.SelectedItem = KindChoices.First(choice => choice.Kind == kind);
        UpdateValueBox();
    }

    private ScriptLiteralKind SelectedKind =>
        (_kindBox.SelectedItem as LiteralKindChoice)?.Kind
        ?? ScriptLiteralKind.Raw;

    private void UpdateValueBox()
    {
        _kindBox.IsEnabled = IsEnabled;
        var showsValue = SelectedKind is ScriptLiteralKind.Text
            or ScriptLiteralKind.Number
            or ScriptLiteralKind.Raw;
        _valueBox.Visibility = showsValue ? Visibility.Visible : Visibility.Collapsed;
        _valueBox.IsEnabled = IsEnabled && showsValue;
    }

    private static bool TryReadJsonString(string literal, out string text)
    {
        text = string.Empty;
        if (literal.Length < 2 || literal[0] != '"' || literal[^1] != '"')
        {
            return false;
        }

        try
        {
            text = JsonSerializer.Deserialize<string>(literal) ?? string.Empty;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly IReadOnlyList<LiteralKindChoice> KindChoices =
    [
        new(ScriptLiteralKind.Text, "Текст"),
        new(ScriptLiteralKind.Number, "Число"),
        new(ScriptLiteralKind.True, "Да / true"),
        new(ScriptLiteralKind.False, "Нет / false"),
        new(ScriptLiteralKind.Null, "Пусто / null"),
        new(ScriptLiteralKind.Raw, "Как в скрипте"),
    ];

    private sealed record LiteralKindChoice(ScriptLiteralKind Kind, string Label);

    private enum ScriptLiteralKind
    {
        Text,
        Number,
        True,
        False,
        Null,
        Raw,
    }
}
