using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class CodeEditorControl : RichTextBox
{
    private const int HistoryLimit = 200;

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

    private static readonly Brush ErrorForeground =
        new SolidColorBrush(Color.FromRgb(255, 135, 135));
    private static readonly Brush ErrorBackground =
        new SolidColorBrush(Color.FromArgb(60, 220, 72, 72));

    private readonly Popup _completionPopup;
    private readonly ListBox _completionList;
    private readonly TextBlock _completionDescription;
    private ProjectLanguageCompletionContext? _completionContext;
    private IReadOnlyList<ProjectLanguageScopeSpan> _scopeSpans = [];
    private ScopeGuideAdorner? _scopeAdorner;
    private bool _scopeGuideInvalidateQueued;
    private readonly List<EditorSnapshot> _undoHistory = [];
    private readonly List<EditorSnapshot> _redoHistory = [];
    private EditorSnapshot _currentSnapshot = new(string.Empty, 0);
    private bool _updatingDocument;
    private bool _restoringHistory;

    public CodeEditorControl()
    {
        AcceptsReturn = true;
        AcceptsTab = true;
        IsUndoEnabled = false;
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
        AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) => InvalidateScopeGuides()));
        Loaded += (_, _) => EnsureScopeAdorner();
        Unloaded += (_, _) => RemoveScopeAdorner();

        _completionDescription = new TextBlock
        {
            Background = new SolidColorBrush(Color.FromRgb(13, 20, 30)),
            Foreground = new SolidColorBrush(Color.FromRgb(130, 146, 168)),
            Padding = new Thickness(10, 7, 10, 7),
            TextWrapping = TextWrapping.Wrap,
        };
        _completionList = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            DisplayMemberPath = nameof(ProjectLanguageCompletion.Label),
            Foreground = new SolidColorBrush(Color.FromRgb(232, 238, 246)),
            MaxHeight = 250,
            MinWidth = 390,
            Padding = new Thickness(4),
        };
        _completionList.SelectionChanged += (_, _) =>
        {
            _completionDescription.Text =
                (_completionList.SelectedItem as ProjectLanguageCompletion)
                ?.Description ?? string.Empty;
        };
        _completionList.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_completionList.SelectedItem is ProjectLanguageCompletion)
            {
                AcceptCompletion();
            }
        };

        var panel = new DockPanel();
        DockPanel.SetDock(_completionDescription, Dock.Bottom);
        panel.Children.Add(_completionDescription);
        panel.Children.Add(_completionList);
        _completionPopup = new Popup
        {
            AllowsTransparency = true,
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(24, 34, 49)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(58, 80, 108)),
                BorderThickness = new Thickness(1),
                Child = panel,
                CornerRadius = new CornerRadius(4),
            },
            Placement = PlacementMode.RelativePoint,
            PlacementTarget = this,
            PopupAnimation = PopupAnimation.Fade,
            StaysOpen = true,
        };
    }

    public Func<string, int, ProjectLanguageCompletionContext>?
        CompletionProvider { get; set; }

    public string SourceText
    {
        get
        {
            var text = NormalizeText(
                new TextRange(Document.ContentStart, Document.ContentEnd).Text);
            return text.EndsWith('\n') ? text[..^1] : text;
        }
        set
        {
            CloseCompletions();
            ReplaceDocument(value, [], null, 0);
            SetCaretOffset(0);
            ResetHistory(value, 0);
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
        var source = SourceText;
        var caretOffset = Math.Clamp(SourceCaretOffset, 0, source.Length);
        ReplaceDocument(source, spans, errorStart, errorLength);
        SetCaretOffset(caretOffset);
    }

    public void SelectSourceRange(int start, int length)
    {
        CloseCompletions();
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

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        if (_updatingDocument)
        {
            return;
        }
        RecordUserChange();
        Dispatcher.BeginInvoke(
            () => ShowCompletions(force: false),
            DispatcherPriority.Background);
    }

    protected override void OnSelectionChanged(RoutedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (_updatingDocument || _restoringHistory)
        {
            return;
        }
        _currentSnapshot = new EditorSnapshot(
            _currentSnapshot.Source,
            Math.Clamp(SourceCaretOffset, 0, _currentSnapshot.Source.Length));
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UndoUserChange();
            e.Handled = true;
            return;
        }
        if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
            || e.Key == Key.Z
                && Keyboard.Modifiers
                    == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RedoUserChange();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowCompletions(force: true);
            e.Handled = true;
            return;
        }
        if (_completionPopup.IsOpen)
        {
            if (e.Key == Key.Down)
            {
                MoveCompletionSelection(1);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Up)
            {
                MoveCompletionSelection(-1);
                e.Handled = true;
                return;
            }
            if (e.Key is Key.Enter or Key.Tab)
            {
                AcceptCompletion();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Escape)
            {
                CloseCompletions();
                e.Handled = true;
                return;
            }
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnLostKeyboardFocus(
        KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        if (!_completionPopup.IsKeyboardFocusWithin)
        {
            CloseCompletions();
        }
    }

    private void DrawScopeGuides(DrawingContext drawingContext)
    {
        if (_scopeSpans.Count == 0)
        {
            return;
        }

        drawingContext.PushClip(
            new RectangleGeometry(
                new Rect(0, 0, ActualWidth, ActualHeight)));
        var visibleRange = GetVisibleSourceRange();
        var visibleStart = Math.Max(0, (visibleRange?.Start ?? 0) - 2_000);
        var visibleEnd = visibleRange is null
            ? int.MaxValue
            : visibleRange.Value.End + 2_000;

        foreach (var scope in _scopeSpans)
        {
            if (scope.CloseBraceOffset < visibleStart
                || scope.OpenBraceOffset > visibleEnd)
            {
                continue;
            }

            var open = GetPosition(scope.OpenBraceOffset);
            var close = GetPosition(scope.CloseBraceOffset);
            if (open is null || close is null)
            {
                continue;
            }

            var openRect = open.GetCharacterRect(LogicalDirection.Forward);
            var closeRect = close.GetCharacterRect(LogicalDirection.Forward);
            var top = openRect.Bottom + 2;
            var bottom = closeRect.Top - 2;
            if (bottom <= top || bottom < 0 || top > ActualHeight)
            {
                continue;
            }

            var color = (scope.Depth % 3) switch
            {
                0 => Color.FromArgb(170, 100, 218, 183),
                1 => Color.FromArgb(155, 116, 174, 255),
                _ => Color.FromArgb(145, 211, 159, 255),
            };
            var pen = new Pen(new SolidColorBrush(color), 1.25);
            pen.Freeze();
            var x = closeRect.Left + 3.5;
            var visibleTop = Math.Max(0, top);
            var visibleBottom = Math.Min(ActualHeight, bottom);
            drawingContext.DrawLine(
                pen,
                new Point(x, visibleTop),
                new Point(x, visibleBottom));
            if (top >= 0)
            {
                drawingContext.DrawLine(
                    pen,
                    new Point(x, top),
                    new Point(x + 8, top));
            }
            if (bottom <= ActualHeight)
            {
                drawingContext.DrawLine(
                    pen,
                    new Point(x, bottom),
                    new Point(x + 8, bottom));
            }
        }
        drawingContext.Pop();
    }

    private (int Start, int End)? GetVisibleSourceRange()
    {
        var top = GetPositionFromPoint(new Point(0, 0), snapToText: true);
        var bottom = GetPositionFromPoint(
            new Point(
                Math.Max(0, ActualWidth - 1),
                Math.Max(0, ActualHeight - 1)),
            snapToText: true);
        if (top is null || bottom is null)
        {
            return null;
        }

        var start = GetSourceOffset(top);
        var end = GetSourceOffset(bottom);
        return start <= end ? (start, end) : (end, start);
    }

    private int GetSourceOffset(TextPointer position) =>
        NormalizeText(new TextRange(Document.ContentStart, position).Text).Length;

    private void ShowCompletions(bool force)
    {
        if (CompletionProvider is null || !IsKeyboardFocusWithin)
        {
            CloseCompletions();
            return;
        }

        var source = SourceText;
        var caretOffset = Math.Clamp(SourceCaretOffset, 0, source.Length);
        var context = CompletionProvider(source, caretOffset);
        if (context.Items.Count == 0
            || !force && context.ReplacementLength == 0)
        {
            CloseCompletions();
            return;
        }

        _completionContext = context;
        _completionList.ItemsSource = context.Items;
        _completionList.SelectedIndex = 0;
        var caretRect = CaretPosition.GetCharacterRect(LogicalDirection.Forward);
        _completionPopup.HorizontalOffset = Math.Max(0, caretRect.Left);
        _completionPopup.VerticalOffset = Math.Max(0, caretRect.Bottom + 4);
        _completionPopup.IsOpen = true;
    }

    private void MoveCompletionSelection(int delta)
    {
        if (_completionList.Items.Count == 0)
        {
            return;
        }
        var index = _completionList.SelectedIndex;
        index = (index + delta + _completionList.Items.Count)
            % _completionList.Items.Count;
        _completionList.SelectedIndex = index;
        _completionList.ScrollIntoView(_completionList.SelectedItem);
    }

    private void AcceptCompletion()
    {
        if (_completionContext is null
            || _completionList.SelectedItem
                is not ProjectLanguageCompletion completion)
        {
            return;
        }

        var source = SourceText;
        var start = _completionContext.ReplacementStart;
        var length = _completionContext.ReplacementLength;
        var updated = source.Remove(start, length)
            .Insert(start, completion.InsertText);
        var caret = start + (completion.CaretOffset < 0
            ? completion.InsertText.Length
            : Math.Min(completion.CaretOffset, completion.InsertText.Length));
        CloseCompletions();
        ApplyUserSnapshot(new EditorSnapshot(updated, caret));
        Focus();
    }

    private void CloseCompletions()
    {
        _completionPopup.IsOpen = false;
        _completionList.ItemsSource = null;
        _completionContext = null;
    }

    private void RecordUserChange()
    {
        if (_restoringHistory)
        {
            return;
        }
        var source = SourceText;
        if (source == _currentSnapshot.Source)
        {
            return;
        }
        PushHistory(_undoHistory, _currentSnapshot);
        _redoHistory.Clear();
        _currentSnapshot = new EditorSnapshot(
            source,
            Math.Clamp(SourceCaretOffset, 0, source.Length));
    }

    private void UndoUserChange()
    {
        if (_undoHistory.Count == 0)
        {
            return;
        }
        CloseCompletions();
        PushHistory(_redoHistory, CaptureSnapshot());
        var snapshot = PopHistory(_undoHistory);
        RestoreSnapshot(snapshot);
    }

    private void RedoUserChange()
    {
        if (_redoHistory.Count == 0)
        {
            return;
        }
        CloseCompletions();
        PushHistory(_undoHistory, CaptureSnapshot());
        var snapshot = PopHistory(_redoHistory);
        RestoreSnapshot(snapshot);
    }

    private void ApplyUserSnapshot(EditorSnapshot snapshot)
    {
        PushHistory(_undoHistory, CaptureSnapshot());
        _redoHistory.Clear();
        RestoreSnapshot(snapshot);
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        _restoringHistory = true;
        try
        {
            ReplaceDocument(snapshot.Source, [], null, 0);
            SetCaretOffset(snapshot.CaretOffset);
            _currentSnapshot = snapshot;
        }
        finally
        {
            _restoringHistory = false;
        }
    }

    private EditorSnapshot CaptureSnapshot()
    {
        var source = SourceText;
        return new EditorSnapshot(
            source,
            Math.Clamp(SourceCaretOffset, 0, source.Length));
    }

    private void ResetHistory(string source, int caretOffset)
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _currentSnapshot = new EditorSnapshot(
            source,
            Math.Clamp(caretOffset, 0, source.Length));
    }

    private static void PushHistory(
        List<EditorSnapshot> history,
        EditorSnapshot snapshot)
    {
        if (history.Count > 0 && history[^1] == snapshot)
        {
            return;
        }
        history.Add(snapshot);
        if (history.Count > HistoryLimit)
        {
            history.RemoveAt(0);
        }
    }

    private static EditorSnapshot PopHistory(List<EditorSnapshot> history)
    {
        var index = history.Count - 1;
        var snapshot = history[index];
        history.RemoveAt(index);
        return snapshot;
    }

    private void ReplaceDocument(
        string source,
        IReadOnlyList<ProjectLanguageSyntaxSpan> spans,
        int? errorStart,
        int errorLength)
    {
        _updatingDocument = true;
        try
        {
            _scopeSpans = ProjectLanguage.GetScopeSpans(source);
            Document.Blocks.Clear();
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
            };
            Document.Blocks.Add(paragraph);

            var boundaries = new SortedSet<int> { 0, source.Length };
            foreach (var span in spans)
            {
                boundaries.Add(Math.Clamp(span.Start, 0, source.Length));
                boundaries.Add(
                    Math.Clamp(span.Start + span.Length, 0, source.Length));
            }
            var errorEnd = errorStart.HasValue
                ? Math.Clamp(
                    errorStart.Value + Math.Max(1, errorLength),
                    0,
                    source.Length)
                : 0;
            if (errorStart.HasValue)
            {
                boundaries.Add(Math.Clamp(errorStart.Value, 0, source.Length));
                boundaries.Add(errorEnd);
            }

            var positions = boundaries.ToArray();
            for (var index = 0; index < positions.Length - 1; index++)
            {
                var start = positions[index];
                var end = positions[index + 1];
                if (end <= start)
                {
                    continue;
                }
                var syntax = spans.FirstOrDefault(
                    span => start >= span.Start
                        && start < span.Start + span.Length);
                var isError = errorStart.HasValue
                    && start >= errorStart.Value
                    && start < errorEnd;
                AppendText(
                    paragraph,
                    source[start..end],
                    syntax?.Kind,
                    isError);
            }
            Document.PageWidth = 100_000;
            InvalidateScopeGuides();
        }
        finally
        {
            _updatingDocument = false;
        }
    }

    private static void AppendText(
        Paragraph paragraph,
        string text,
        ProjectLanguageSyntaxKind? syntax,
        bool isError)
    {
        if (text.Length == 0)
        {
            return;
        }
        paragraph.Inlines.Add(
            new Run(text)
            {
                Foreground = isError
                    ? ErrorForeground
                    : syntax.HasValue
                        ? SyntaxBrushes[syntax.Value]
                        : (Brush)Application.Current.Resources["TextBrush"],
                FontWeight = syntax == ProjectLanguageSyntaxKind.Keyword
                    ? FontWeights.SemiBold
                    : FontWeights.Normal,
                Background = isError
                    ? ErrorBackground
                    : Brushes.Transparent,
                TextDecorations = isError
                    ? TextDecorations.Underline
                    : null,
            });
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
                if (offset + text.Length >= sourceOffset)
                {
                    return navigator.GetPositionAtOffset(
                        Math.Clamp(sourceOffset - offset, 0, text.Length));
                }
                offset += text.Length;
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

    private void EnsureScopeAdorner()
    {
        if (_scopeAdorner is not null)
        {
            return;
        }
        var layer = AdornerLayer.GetAdornerLayer(this);
        if (layer is null)
        {
            return;
        }
        _scopeAdorner = new ScopeGuideAdorner(this);
        layer.Add(_scopeAdorner);
    }

    private void RemoveScopeAdorner()
    {
        if (_scopeAdorner is null)
        {
            return;
        }
        AdornerLayer.GetAdornerLayer(this)?.Remove(_scopeAdorner);
        _scopeAdorner = null;
    }

    private void InvalidateScopeGuides()
    {
        if (_scopeAdorner is null || _scopeGuideInvalidateQueued)
        {
            return;
        }

        _scopeGuideInvalidateQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _scopeGuideInvalidateQueued = false;
                _scopeAdorner?.InvalidateVisual();
            },
            DispatcherPriority.Render);
    }

    private sealed class ScopeGuideAdorner : Adorner
    {
        private readonly CodeEditorControl _editor;

        public ScopeGuideAdorner(CodeEditorControl editor)
            : base(editor)
        {
            _editor = editor;
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            _editor.DrawScopeGuides(drawingContext);
        }
    }

    private sealed record EditorSnapshot(string Source, int CaretOffset);
}
