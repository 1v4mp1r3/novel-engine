using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class CodeEditorControl : RichTextBox
{
    private static readonly IReadOnlyDictionary<ProjectLanguageSyntaxKind, Brush>
        SyntaxBrushes = new Dictionary<ProjectLanguageSyntaxKind, Brush>
        {
            [ProjectLanguageSyntaxKind.Keyword] =
                new SolidColorBrush(Color.FromRgb(116, 214, 255)),
            [ProjectLanguageSyntaxKind.Declaration] =
                new SolidColorBrush(Color.FromRgb(232, 221, 143)),
            [ProjectLanguageSyntaxKind.String] =
                new SolidColorBrush(Color.FromRgb(173, 220, 151)),
            [ProjectLanguageSyntaxKind.Number] =
                new SolidColorBrush(Color.FromRgb(211, 159, 255)),
            [ProjectLanguageSyntaxKind.Comment] =
                new SolidColorBrush(Color.FromRgb(104, 126, 148)),
            [ProjectLanguageSyntaxKind.AssetReference] =
                new SolidColorBrush(Color.FromRgb(255, 190, 92)),
            [ProjectLanguageSyntaxKind.Punctuation] =
                new SolidColorBrush(Color.FromRgb(157, 177, 200)),
        };

    public CodeEditorControl()
    {
        AcceptsReturn = true;
        AcceptsTab = true;
        SetResourceReference(BackgroundProperty, "FieldBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        BorderThickness = new Thickness(0);
        CaretBrush = Brushes.White;
        FontFamily = new FontFamily("Cascadia Mono, Consolas");
        FontSize = 14;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        SpellCheck.SetIsEnabled(this, false);
        Document.PageWidth = 100_000;
        Document.PagePadding = new Thickness(0);
    }

    public string SourceText
    {
        get
        {
            var text = new TextRange(
                Document.ContentStart,
                Document.ContentEnd).Text.Replace("\r\n", "\n");
            return text.EndsWith('\n') ? text[..^1] : text;
        }
        set
        {
            Document.Blocks.Clear();
            Document.Blocks.Add(
                new Paragraph(new Run(value))
                {
                    Margin = new Thickness(0),
                });
            Document.PageWidth = 100_000;
        }
    }

    public int SourceCaretOffset =>
        NormalizeText(
            new TextRange(Document.ContentStart, CaretPosition).Text).Length;

    public void ApplySyntax(
        IReadOnlyList<ProjectLanguageSyntaxSpan> spans,
        int? errorStart = null,
        int errorLength = 0)
    {
        var caretOffset = SourceCaretOffset;
        var fullRange = new TextRange(Document.ContentStart, Document.ContentEnd);
        fullRange.ApplyPropertyValue(
            TextElement.ForegroundProperty,
            Application.Current.Resources["TextBrush"]);
        fullRange.ApplyPropertyValue(
            TextElement.FontWeightProperty,
            FontWeights.Normal);
        fullRange.ApplyPropertyValue(
            TextElement.BackgroundProperty,
            Brushes.Transparent);
        fullRange.ApplyPropertyValue(
            Inline.TextDecorationsProperty,
            new TextDecorationCollection());

        foreach (var span in spans)
        {
            var range = CreateRange(span.Start, span.Length);
            if (range is null)
            {
                continue;
            }
            range.ApplyPropertyValue(
                TextElement.ForegroundProperty,
                SyntaxBrushes[span.Kind]);
            if (span.Kind == ProjectLanguageSyntaxKind.Keyword)
            {
                range.ApplyPropertyValue(
                    TextElement.FontWeightProperty,
                    FontWeights.SemiBold);
            }
        }

        if (errorStart.HasValue)
        {
            var range = CreateRange(
                errorStart.Value,
                Math.Max(1, errorLength));
            if (range is not null)
            {
                range.ApplyPropertyValue(
                    TextElement.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(60, 220, 72, 72)));
                range.ApplyPropertyValue(
                    Inline.TextDecorationsProperty,
                    TextDecorations.Underline);
                range.ApplyPropertyValue(
                    TextElement.ForegroundProperty,
                    new SolidColorBrush(Color.FromRgb(255, 135, 135)));
            }
        }

        SetCaretOffset(caretOffset);
    }

    public void SelectSourceRange(int start, int length)
    {
        var range = CreateRange(start, length);
        if (range is null)
        {
            return;
        }
        Selection.Select(range.Start, range.End);
        CaretPosition = range.End;
        CaretPosition.Paragraph?.BringIntoView();
        Focus();
    }

    private TextRange? CreateRange(int start, int length)
    {
        var rangeStart = GetPosition(start);
        var rangeEnd = GetPosition(start + length);
        return rangeStart is null || rangeEnd is null
            ? null
            : new TextRange(rangeStart, rangeEnd);
    }

    private void SetCaretOffset(int offset)
    {
        var position = GetPosition(offset);
        if (position is not null)
        {
            CaretPosition = position;
        }
    }

    private TextPointer? GetPosition(int sourceOffset)
    {
        sourceOffset = Math.Max(0, sourceOffset);
        var navigator = Document.ContentStart;
        var offset = 0;
        while (navigator is not null)
        {
            var context = navigator.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                var text = navigator.GetTextInRun(LogicalDirection.Forward);
                var normalized = NormalizeText(text);
                if (offset + normalized.Length >= sourceOffset)
                {
                    var distance = Math.Clamp(
                        sourceOffset - offset,
                        0,
                        text.Length);
                    return navigator.GetPositionAtOffset(distance);
                }
                offset += normalized.Length;
            }
            else if (context == TextPointerContext.ElementStart
                && navigator.GetAdjacentElement(LogicalDirection.Forward)
                    is LineBreak)
            {
                if (offset >= sourceOffset)
                {
                    return navigator;
                }
                offset++;
            }

            navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
        }
        return Document.ContentEnd;
    }

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n");
}
