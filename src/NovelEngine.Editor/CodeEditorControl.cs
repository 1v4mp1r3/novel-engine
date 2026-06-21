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
                CreateFrozenBrush(116, 214, 255),
            [ProjectLanguageSyntaxKind.Declaration] =
                CreateFrozenBrush(232, 221, 143),
            [ProjectLanguageSyntaxKind.String] =
                CreateFrozenBrush(173, 220, 151),
            [ProjectLanguageSyntaxKind.Number] =
                CreateFrozenBrush(211, 159, 255),
            [ProjectLanguageSyntaxKind.Comment] =
                CreateFrozenBrush(104, 126, 148),
            [ProjectLanguageSyntaxKind.AssetReference] =
                CreateFrozenBrush(255, 190, 92),
            [ProjectLanguageSyntaxKind.Punctuation] =
                CreateFrozenBrush(157, 177, 200),
        };

    private static readonly Brush ErrorForeground =
        CreateFrozenBrush(255, 135, 135);
    private static readonly Brush ErrorBackground =
        CreateFrozenBrush(60, 220, 72, 72);

    private readonly Popup _completionPopup;
    private readonly ListBox _completionList;
    private readonly TextBlock _completionDescription;
    private readonly DispatcherTimer _completionTimer;
    private readonly DispatcherTimer _historyTimer;
    private ProjectLanguageCompletionContext? _completionContext;
    private readonly List<EditorSnapshot> _undoHistory = [];
    private readonly List<EditorSnapshot> _redoHistory = [];
    private EditorSnapshot _currentSnapshot = new(string.Empty, 0);
    private string? _sourceTextCache;
    private string? _renderedSource;
    private IReadOnlyList<ProjectLanguageSyntaxSpan> _renderedSpans = [];
    private int? _renderedErrorStart;
    private int _renderedErrorLength;
    private bool _updatingDocument;
    private bool _restoringHistory;
    private bool _historyRecordingPending;
    private bool _historySuspendedForSize;
    private int? _caretOffsetCache;
    private TextPointer? _caretPointerCache;
    private int _estimatedSourceLength;

    private static Brush CreateFrozenBrush(byte red, byte green, byte blue) =>
        CreateFrozenBrush(255, red, green, blue);

    private static Brush CreateFrozenBrush(
        byte alpha,
        byte red,
        byte green,
        byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

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
        _completionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _completionTimer.Tick += (_, _) =>
        {
            _completionTimer.Stop();
            ShowCompletions(force: false);
        };
        _historyTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(420),
        };
        _historyTimer.Tick += (_, _) =>
        {
            _historyTimer.Stop();
            CommitPendingUserChange();
        };
    }

    public Func<string, int, ProjectLanguageCompletionContext>?
        CompletionProvider { get; set; }

    public string SourceText
    {
        get
        {
            if (_sourceTextCache is null)
            {
                _sourceTextCache = ReadSourceText();
                _estimatedSourceLength = _sourceTextCache.Length;
            }
            return _sourceTextCache;
        }
        set
        {
            CloseCompletions();
            ClearPendingUserChange();
            ReplaceDocument(value, [], null, 0);
            SetCaretOffset(0);
            ResetHistory(value, 0);
        }
    }

    public int SourceCaretOffset =>
        _caretOffsetCache ?? RefreshCaretOffsetCache();

    public int EstimatedSourceLength => _estimatedSourceLength;

    internal bool HasCachedSourceText(string source) =>
        _sourceTextCache is not null
        && _sourceTextCache.Equals(source, StringComparison.Ordinal);

    public void ApplySyntax(
        IReadOnlyList<ProjectLanguageSyntaxSpan> spans,
        int? errorStart = null,
        int errorLength = 0)
    {
        var source = SourceText;
        if (IsRendered(source, spans, errorStart, errorLength))
        {
            return;
        }

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
        _sourceTextCache = null;
        var insertedText = UpdateEstimatedSourceLength(e);
        _caretOffsetCache = null;
        _caretPointerCache = null;
        InvalidateRenderedSyntax();
        if (ShouldScheduleHistoryRecord(_estimatedSourceLength))
        {
            ScheduleUserChangeRecord();
        }
        else
        {
            ClearPendingUserChange();
            SuspendHistoryForOversizedDocument();
        }

        if (ShouldScheduleAutomaticCompletion(_estimatedSourceLength, insertedText))
        {
            _completionTimer.Stop();
            _completionTimer.Start();
        }
        else
        {
            CloseCompletions();
        }
    }

    protected override void OnSelectionChanged(RoutedEventArgs e)
    {
        if (_updatingDocument || _restoringHistory)
        {
            base.OnSelectionChanged(e);
            return;
        }
        if (!CodeEditorPerformancePolicy.ShouldTrackLiveCaret(
                _estimatedSourceLength))
        {
            _caretOffsetCache = null;
            _caretPointerCache = null;
            base.OnSelectionChanged(e);
            return;
        }
        var caretOffset = RefreshCaretOffsetCache();
        _currentSnapshot = new EditorSnapshot(
            _currentSnapshot.Source,
            Math.Clamp(caretOffset, 0, _currentSnapshot.Source.Length));
        base.OnSelectionChanged(e);
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

    private void ShowCompletions(bool force)
    {
        if (CompletionProvider is null || !IsKeyboardFocusWithin)
        {
            CloseCompletions();
            return;
        }

        if (!CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
                _estimatedSourceLength,
                force))
        {
            CloseCompletions();
            return;
        }

        var source = SourceText;
        if (!CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
                source.Length,
                force))
        {
            CloseCompletions();
            return;
        }

        var caretOffset = Math.Clamp(SourceCaretOffset, 0, source.Length);
        if (!force && !ShouldAutoComplete(source, caretOffset))
        {
            CloseCompletions();
            return;
        }

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
        CommitPendingUserChange();
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
        _completionTimer.Stop();
        if (!ShouldResetCompletionPopup(
                _completionPopup.IsOpen,
                _completionContext is not null,
                _completionList.ItemsSource is not null))
        {
            return;
        }

        _completionPopup.IsOpen = false;
        _completionList.ItemsSource = null;
        _completionContext = null;
    }

    private bool UpdateEstimatedSourceLength(TextChangedEventArgs e)
    {
        var length = _sourceTextCache?.Length ?? _estimatedSourceLength;
        var insertedText = false;
        foreach (var change in e.Changes)
        {
            length += change.AddedLength - change.RemovedLength;
            insertedText |= change.AddedLength > 0;
        }
        _estimatedSourceLength = Math.Max(0, length);
        return insertedText;
    }

    private static bool ShouldAutoComplete(string source, int caretOffset)
    {
        if (caretOffset == 0)
        {
            return false;
        }

        var previous = source[caretOffset - 1];
        return previous == '@' || IsSyntaxIdentifierPart(previous);
    }

    private static bool IsSyntaxIdentifierPart(char character) =>
        character is '_' or '-' || char.IsLetterOrDigit(character);

    internal static bool ShouldScheduleAutomaticCompletion(int estimatedSourceLength) =>
        CodeEditorPerformancePolicy.ShouldRunAutomaticCompletions(estimatedSourceLength);

    internal static bool ShouldScheduleAutomaticCompletion(
        int estimatedSourceLength,
        bool insertedText) =>
        insertedText && ShouldScheduleAutomaticCompletion(estimatedSourceLength);

    internal static bool ShouldScheduleHistoryRecord(int estimatedSourceLength) =>
        CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(estimatedSourceLength);

    internal static bool ShouldResetCompletionPopup(
        bool isOpen,
        bool hasContext,
        bool hasItemsSource) =>
        isOpen || hasContext || hasItemsSource;

    private void ScheduleUserChangeRecord()
    {
        if (_restoringHistory)
        {
            return;
        }

        _historyRecordingPending = true;
        _historyTimer.Stop();
        _historyTimer.Start();
    }

    private void CommitPendingUserChange()
    {
        if (!_historyRecordingPending)
        {
            return;
        }

        _historyTimer.Stop();
        _historyRecordingPending = false;
        RecordUserChange();
    }

    private void ClearPendingUserChange()
    {
        _historyTimer.Stop();
        _historyRecordingPending = false;
    }

    private void RecordUserChange()
    {
        if (_restoringHistory)
        {
            return;
        }
        if (!CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(
                _estimatedSourceLength))
        {
            SuspendHistoryForOversizedDocument();
            return;
        }

        var source = SourceText;
        if (!CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(
                source.Length))
        {
            SuspendHistoryForOversizedDocument();
            return;
        }

        if (_historySuspendedForSize)
        {
            ResetHistory(source, Math.Clamp(SourceCaretOffset, 0, source.Length));
            return;
        }

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
        CommitPendingUserChange();
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
        CommitPendingUserChange();
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
        if (CanRecordHistoryForSnapshot(snapshot))
        {
            PushHistory(_undoHistory, CaptureSnapshot());
            _redoHistory.Clear();
        }
        else
        {
            SuspendHistoryForOversizedDocument();
        }
        RestoreSnapshot(snapshot);
    }

    private void RestoreSnapshot(EditorSnapshot snapshot)
    {
        ClearPendingUserChange();
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
        ClearPendingUserChange();
        _undoHistory.Clear();
        _redoHistory.Clear();
        _currentSnapshot = new EditorSnapshot(
            source,
            Math.Clamp(caretOffset, 0, source.Length));
        _historySuspendedForSize =
            !CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(source.Length);
    }

    private bool CanRecordHistoryForSnapshot(EditorSnapshot snapshot) =>
        CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(
            _estimatedSourceLength)
        && CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(
            snapshot.Source.Length);

    private void SuspendHistoryForOversizedDocument()
    {
        _undoHistory.Clear();
        _redoHistory.Clear();
        _historySuspendedForSize = true;
        _currentSnapshot = new EditorSnapshot(string.Empty, 0);
    }

    private static void PushHistory(
        List<EditorSnapshot> history,
        EditorSnapshot snapshot)
    {
        if (!CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(
                snapshot.Source.Length))
        {
            history.Clear();
            return;
        }

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
            Document.Blocks.Clear();
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0),
            };
            Document.Blocks.Add(paragraph);

            if (spans.Count == 0 && !errorStart.HasValue)
            {
                AppendText(paragraph, source, null, isError: false);
            }
            else
            {
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
                var orderedSpans = spans
                    .OrderBy(span => span.Start)
                    .ThenByDescending(span => span.Length)
                    .ToArray();
                var spanIndex = 0;
                for (var index = 0; index < positions.Length - 1; index++)
                {
                    var start = positions[index];
                    var end = positions[index + 1];
                    if (end <= start)
                    {
                        continue;
                    }
                    while (spanIndex < orderedSpans.Length
                        && start >= orderedSpans[spanIndex].Start
                            + orderedSpans[spanIndex].Length)
                    {
                        spanIndex++;
                    }
                    var syntax = spanIndex < orderedSpans.Length
                        && start >= orderedSpans[spanIndex].Start
                        && start < orderedSpans[spanIndex].Start
                            + orderedSpans[spanIndex].Length
                        ? orderedSpans[spanIndex]
                        : null;
                    var isError = errorStart.HasValue
                        && start >= errorStart.Value
                        && start < errorEnd;
                    AppendText(
                        paragraph,
                        source[start..end],
                        syntax?.Kind,
                        isError);
                }
            }
            Document.PageWidth = 100_000;
            _sourceTextCache = source;
            _estimatedSourceLength = source.Length;
            _caretPointerCache = null;
            _renderedSource = source;
            _renderedSpans = spans.ToArray();
            _renderedErrorStart = errorStart;
            _renderedErrorLength = errorLength;
        }
        finally
        {
            _updatingDocument = false;
        }
    }

    private void InvalidateRenderedSyntax()
    {
        _renderedSource = null;
        _renderedSpans = [];
        _renderedErrorStart = null;
        _renderedErrorLength = 0;
    }

    private bool IsRendered(
        string source,
        IReadOnlyList<ProjectLanguageSyntaxSpan> spans,
        int? errorStart,
        int errorLength)
    {
        if (_renderedSource != source
            || _renderedErrorStart != errorStart
            || _renderedErrorLength != errorLength
            || _renderedSpans.Count != spans.Count)
        {
            return false;
        }

        for (var index = 0; index < spans.Count; index++)
        {
            if (_renderedSpans[index] != spans[index])
            {
                return false;
            }
        }
        return true;
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
        var sourceLength = _sourceTextCache?.Length ?? ReadSourceText().Length;
        var normalizedOffset = Math.Clamp(offset, 0, sourceLength);
        var position = GetPosition(normalizedOffset);
        if (position is not null)
        {
            _caretOffsetCache = normalizedOffset;
            CaretPosition = position;
            _caretPointerCache = CaretPosition;
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

    private string ReadSourceText()
    {
        var text = NormalizeText(
            new TextRange(Document.ContentStart, Document.ContentEnd).Text);
        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private int RefreshCaretOffsetCache()
    {
        var offset = TryReadCaretOffsetFromCachedPointer(out var cachedOffset)
            ? cachedOffset
            : ReadCaretOffsetFromDocumentStart();
        var sourceLength = _sourceTextCache?.Length;
        if (sourceLength.HasValue)
        {
            offset = Math.Clamp(offset, 0, sourceLength.Value);
        }
        _caretOffsetCache = offset;
        _caretPointerCache = CaretPosition;
        return offset;
    }

    private bool TryReadCaretOffsetFromCachedPointer(out int offset)
    {
        offset = 0;
        if (!_caretOffsetCache.HasValue || _caretPointerCache is null)
        {
            return false;
        }

        try
        {
            var comparison = _caretPointerCache.CompareTo(CaretPosition);
            if (comparison == 0)
            {
                offset = _caretOffsetCache.Value;
                return true;
            }

            var range = comparison < 0
                ? new TextRange(_caretPointerCache, CaretPosition)
                : new TextRange(CaretPosition, _caretPointerCache);
            var delta = NormalizeText(range.Text).Length;
            offset = comparison < 0
                ? _caretOffsetCache.Value + delta
                : _caretOffsetCache.Value - delta;
            return true;
        }
        catch (Exception error) when (
            error is InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private int ReadCaretOffsetFromDocumentStart() =>
        NormalizeText(
            new TextRange(Document.ContentStart, CaretPosition).Text).Length;

    private static string NormalizeText(string text) =>
        text.Replace("\r\n", "\n");

    private sealed record EditorSnapshot(string Source, int CaretOffset);
}
