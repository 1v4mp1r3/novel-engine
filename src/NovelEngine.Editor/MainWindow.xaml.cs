using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public partial class MainWindow : Window
{
    private const int MaxProjectHistoryEntries = 100;
    private const int MaxHighlightedCodeLength = 40_000;
    private const int MaxHighlightedSyntaxSpans = 2_500;

    private NovelProject _project = NovelProject.CreateDefault();
    private string? _projectPath;
    private bool _dirty;
    private bool _syncingSelection;
    private bool _refreshingProperties;
    private bool _syncingCode;
    private bool _codeHasPendingChanges;
    private bool _refreshingAssetFolders;
    private bool _refreshingAssetPickers;
    private bool _buildInProgress;
    private string? _selectedAssetFolder;
    private string? _workspaceDirectory;
    private bool _workspaceNeedsProjectFile;
    private Process? _gameProcess;
    private FileSystemWatcher? _filesWatcher;
    private readonly MediaPlayer _assetPreviewPlayer = new();
    private readonly DispatcherTimer _codeAnalysisTimer;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _filesRefreshTimer;
    private readonly DispatcherTimer _diagnosticsTimer;
    private string _codeCursorSource = string.Empty;
    private int[] _codeLineStarts = [0];
    private int _lastCodeCursorOffset = -1;
    private bool _codeCursorCacheDirty = true;
    private string? _codeSyntaxSource;
    private IReadOnlyList<ProjectLanguageSyntaxSpan> _codeSyntaxSpans = [];
    private string? _parsedCodeSource;
    private NovelProject? _parsedCodeProject;
    private bool _codeRefreshPending = true;
    private bool _codeRefreshUseStoredSource = true;
    private bool _syncingFilesFromDisk;
    private readonly List<ProjectHistoryEntry> _projectHistory = [];
    private int _projectHistoryIndex = -1;
    private bool _restoringProjectHistory;
    private string _savedProjectSnapshot = string.Empty;
    private string? _assetPreviewAudioPath;
    private readonly Dictionary<string, string> _assetSizeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? startupProjectPath)
    {
        InitializeComponent();
        CodeEditor.CompletionProvider = ProjectLanguage.GetCompletions;
        AssetsGrid.ContextMenu = new ContextMenu();

        Graph.SelectionChanged += (_, _) => HandleGraphSelection();
        Graph.ProjectChanged += (_, _) => MarkDirty();
        Graph.AddChoiceRequested += nodeId => AddOutput(nodeId);
        Graph.PreviewNodeRequested += PreviewNode;
        Graph.EditNodeSceneRequested += EditNodeScene;
        Graph.EditMainMenuRequested += EditMainMenu;
        Graph.OpenNodeCodeRequested += NavigateToNodeCode;
        Graph.TransitionSettingsRequested += EditTransition;
        _assetPreviewPlayer.MediaEnded += (_, _) =>
        {
            AssetPreviewStopButton.IsEnabled = false;
            StatusText.Text = "Прослушивание завершено";
        };
        Closing += MainWindow_Closing;
        Loaded += (_, _) => Graph.CenterGraph();

        _codeAnalysisTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280),
        };
        _codeAnalysisTimer.Tick += (_, _) =>
        {
            _codeAnalysisTimer.Stop();
            AnalyzeCode();
        };
        _autoSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5),
        };
        _autoSaveTimer.Tick += (_, _) => AutoSaveProject();
        _autoSaveTimer.Start();
        _filesRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350),
        };
        _filesRefreshTimer.Tick += (_, _) =>
        {
            _filesRefreshTimer.Stop();
            _assetSizeCache.Clear();
            if (ReferenceEquals(WorkspaceTabs.SelectedItem, FilesTab))
            {
                RefreshAssets();
            }
            else
            {
                SyncFilesFromDisk(refreshCode: false);
            }
            RequestDiagnosticsRefresh();
        };
        _diagnosticsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450),
        };
        _diagnosticsTimer.Tick += (_, _) =>
        {
            _diagnosticsTimer.Stop();
            RefreshProjectDiagnostics(showPanel: false);
        };

        SetProject(_project, null);
        if (startupProjectPath is not null)
        {
            OpenStartupProject(startupProjectPath);
        }
    }

    internal void SelectPreviewNode(NodeKind kind)
    {
        var node = _project.Nodes.FirstOrDefault(candidate => candidate.Kind == kind);
        Graph.SelectNode(node?.Id);
    }

    internal void SelectCodeWorkspace() =>
        WorkspaceTabs.SelectedItem = CodeTab;

    internal void SelectFilesWorkspace() =>
        WorkspaceTabs.SelectedItem = FilesTab;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _ = RunCompiledGameAsync(debugMode: false);
            e.Handled = true;
        }
        else if (e.Key == Key.F6 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _ = RunCompiledGameAsync(debugMode: true);
            e.Handled = true;
        }
        else if (e.Key == Key.F5
            && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            StopGameProcess();
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PreviewNode(Graph.SelectedNodeId);
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            NewProject();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenProject_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.Z
            && Keyboard.Modifiers == ModifierKeys.Control
            && !IsTextEditing())
        {
            UndoProject();
            e.Handled = true;
        }
        else if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control
                || e.Key == Key.Z
                    && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            && !IsTextEditing())
        {
            RedoProject();
            e.Handled = true;
        }
        else if (e.Key == Key.S
            && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SaveProjectAs();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && !IsTextEditing())
        {
            Graph.DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.D
            && Keyboard.Modifiers == ModifierKeys.Control
            && !IsTextEditing())
        {
            Graph.DuplicateSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Home && !IsTextEditing())
        {
            Graph.CenterGraph();
            e.Handled = true;
        }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SaveProject();
            e.Handled = true;
        }
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _ = CompileGameAsync(debugSymbols: false, reportSuccess: true);
            e.Handled = true;
        }
    }

    private static bool IsTextEditing() =>
        Keyboard.FocusedElement is TextBox or RichTextBox;

    private void SetProject(
        NovelProject project,
        string? path,
        string? workspaceDirectory = null,
        bool saveStructureChanges = true)
    {
        _project = project;
        _projectPath = path;
        _workspaceDirectory = NormalizeWorkspaceDirectory(
            workspaceDirectory
            ?? (_projectPath is null
                ? _workspaceDirectory
                : Path.GetDirectoryName(_projectPath)));
        _workspaceNeedsProjectFile = path is null && workspaceDirectory is not null;
        _dirty = false;
        _selectedAssetFolder = null;
        var structureChanges = EnsureWorkspaceStructure();
        var filesChanged = SyncFilesFromDisk(refreshCode: false);
        if (saveStructureChanges && structureChanges > 0 && _projectPath is not null)
        {
            ProjectSerializer.Save(_project, _projectPath);
        }
        ConfigureFilesWatcher();
        Graph.SetProject(project);
        RefreshExplorer();
        RefreshProperties();
        RefreshAssets(syncFromDisk: false);
        RequestCodeRefresh(useStoredSource: !filesChanged);
        RefreshWindowTitle();
        StatusText.Text = path is null
            ? _workspaceDirectory is null
                ? "Новый проект"
                : $"Новый проект в папке {Path.GetFileName(_workspaceDirectory)}"
            : $"Открыт {Path.GetFileName(path)}";
        ResetProjectHistory();
        RefreshProjectDiagnostics(showPanel: false);
    }

    private void HandleGraphSelection()
    {
        RefreshExplorer();
        RefreshProperties();
    }

    private void RefreshExplorer()
    {
        _syncingSelection = true;
        try
        {
            var query = ProjectSearchBox.Text.Trim();
            var matchedNodes = FilteredNodes(query).ToList();
            ProjectTree.Items.Clear();
            var root = new TreeViewItem
            {
                Header = query.Length == 0
                    ? _project.Title
                    : $"{_project.Title} · найдено {matchedNodes.Count}",
                IsExpanded = true,
            };
            ProjectTree.Items.Add(root);
            AddNodeGroup(root, "Старт", NodeKind.Start, query, matchedNodes);
            AddNodeGroup(root, "Сцены", NodeKind.Scene, query, matchedNodes);
            AddNodeGroup(root, "Диалоги", NodeKind.Dialogue, query, matchedNodes);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void AddNodeGroup(
        TreeViewItem root,
        string title,
        NodeKind kind,
        string query,
        IReadOnlyList<NovelNode> matchedNodes)
    {
        var nodes = matchedNodes
            .Where(node => node.Kind == kind)
            .ToList();
        var group = new TreeViewItem
        {
            Header = query.Length == 0 ? title : $"{title} ({nodes.Count})",
            IsExpanded = true,
        };
        root.Items.Add(group);
        foreach (var node in nodes)
        {
            var item = new TreeViewItem
            {
                Header = node.Title,
                Tag = node.Id,
                IsSelected = node.Id == Graph.SelectedNodeId,
            };
            group.Items.Add(item);
        }
    }

    private IEnumerable<NovelNode> FilteredNodes(string query) =>
        _project.Nodes.Where(node => NodeMatchesSearch(node, query));

    private static bool NodeMatchesSearch(NovelNode node, string query)
    {
        if (query.Length == 0)
        {
            return true;
        }
        return Contains(node.Title, query)
            || Contains(node.Id, query)
            || Contains(node.Speaker, query)
            || Contains(node.Text, query);
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    private void RefreshProperties()
    {
        _refreshingProperties = true;
        try
        {
            var node = _project.FindNode(Graph.SelectedNodeId);
            var enabled = node is not null;
            foreach (var control in PropertyControls())
            {
                control.IsEnabled = enabled;
            }

            if (node is null)
            {
                TitleBox.Clear();
                KindText.Text = "-";
                SpeakerBox.Clear();
                BodyTextBox.Clear();
                BackgroundBox.Clear();
                MusicBox.Clear();
                RefreshNodeAssetPickers(null);
                ScriptBox.Clear();
                CharactersGrid.ItemsSource = null;
                OutputsGrid.ItemsSource = null;
                SetCharacterButtons(false);
                SetOutputButtons(false, false);
                return;
            }

            TitleBox.Text = node.Title;
            KindText.Text = KindName(node.Kind);
            SpeakerBox.Text = node.Speaker;
            SpeakerBox.IsEnabled = node.Kind == NodeKind.Dialogue;
            BodyTextBox.Text = node.Text;
            BackgroundBox.Text = node.Background;
            InheritBackgroundCheck.IsChecked = node.InheritBackground;
            InheritBackgroundCheck.IsEnabled = node.Kind != NodeKind.Start;
            BackgroundBox.IsEnabled = node.Kind == NodeKind.Start || !node.InheritBackground;
            MusicBox.Text = node.Music;
            InheritMusicCheck.IsChecked = node.InheritMusic;
            InheritMusicCheck.IsEnabled = node.Kind != NodeKind.Start;
            MusicBox.IsEnabled = node.Kind == NodeKind.Start || !node.InheritMusic;
            RefreshNodeAssetPickers(node);
            InheritCharactersCheck.IsChecked = node.InheritCharacters;
            InheritCharactersCheck.IsEnabled = node.Kind != NodeKind.Start;
            ScriptBox.Text = node.Script;
            CharactersGrid.ItemsSource = node.Characters
                .Select(character => new CharacterView(character))
                .ToList();
            OutputsGrid.ItemsSource = node.Outputs
                .Select(output => new OutputView(
                    output,
                    _project.FindNode(output.TargetNodeId)?.Title ?? "не подключено"))
                .ToList();

            SetCharacterButtons(node.Kind == NodeKind.Start || !node.InheritCharacters);
            SetOutputButtons(
                node.Kind == NodeKind.Dialogue,
                OutputsGrid.SelectedItem is OutputView);
        }
        finally
        {
            _refreshingProperties = false;
        }
    }

    private IEnumerable<Control> PropertyControls()
    {
        yield return TitleBox;
        yield return SpeakerBox;
        yield return BodyTextBox;
        yield return BackgroundBox;
        yield return BackgroundFolderBox;
        yield return BackgroundAssetBox;
        yield return InheritBackgroundCheck;
        yield return MusicBox;
        yield return MusicFolderBox;
        yield return MusicAssetBox;
        yield return InheritMusicCheck;
        yield return InheritCharactersCheck;
        yield return CharactersGrid;
        yield return ScriptBox;
        yield return OutputsGrid;
    }

    private bool ApplyProperties()
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null)
        {
            return true;
        }

        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            MessageBox.Show(
                this,
                "Название ноды не может быть пустым.",
                "Свойства ноды",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        try
        {
            NovelScript.Execute(ScriptBox.Text, new ScriptState());
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Ошибка скрипта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var speaker = node.Kind == NodeKind.Dialogue
            ? SpeakerBox.Text.Trim()
            : string.Empty;
        var text = BodyTextBox.Text;
        var inheritBackground = node.Kind != NodeKind.Start
            && InheritBackgroundCheck.IsChecked == true;
        var background = BackgroundBox.Text.Trim();
        var inheritMusic = node.Kind != NodeKind.Start
            && InheritMusicCheck.IsChecked == true;
        var music = MusicBox.Text.Trim();
        var inheritCharacters = node.Kind != NodeKind.Start
            && InheritCharactersCheck.IsChecked == true;
        var script = ScriptBox.Text.Trim();

        MarkOverrideIfChanged(node, "title", node.Title, title);
        MarkOverrideIfChanged(node, "speaker", node.Speaker, speaker);
        MarkOverrideIfChanged(node, "text", node.Text, text);
        MarkOverrideIfChanged(
            node,
            "inheritBackground",
            node.InheritBackground,
            inheritBackground);
        MarkOverrideIfChanged(node, "background", node.Background, background);
        MarkOverrideIfChanged(node, "inheritMusic", node.InheritMusic, inheritMusic);
        MarkOverrideIfChanged(node, "music", node.Music, music);
        MarkOverrideIfChanged(
            node,
            "inheritCharacters",
            node.InheritCharacters,
            inheritCharacters);
        MarkOverrideIfChanged(node, "script", node.Script, script);

        node.Title = title;
        node.Speaker = speaker;
        node.Text = text;
        node.InheritBackground = inheritBackground;
        node.Background = background;
        node.InheritMusic = inheritMusic;
        node.Music = music;
        node.InheritCharacters = inheritCharacters;
        node.Script = script;
        Graph.RefreshGraph();
        MarkDirty();
        return true;
    }

    private static void MarkOverrideIfChanged<T>(
        NovelNode node,
        string property,
        T previous,
        T current)
    {
        if (node.UsesTypeDefaults
            && !EqualityComparer<T>.Default.Equals(previous, current))
        {
            node.PropertyOverrides.Add(property);
        }
    }

    private void MarkDirty()
    {
        if (!_codeHasPendingChanges)
        {
            RequestCodeRefresh(useStoredSource: false);
        }
        if (!_restoringProjectHistory)
        {
            RecordProjectHistorySnapshot(Graph.SelectedNodeId);
        }
        _dirty = !CurrentProjectMatchesSavedSnapshot();
        RefreshWindowTitle();
        RefreshExplorer();
        RefreshProperties();
        Graph.RefreshGraph();
        StatusText.Text = "Проект изменён";
        RequestDiagnosticsRefresh();
    }

    private void ResetProjectHistory()
    {
        _projectHistory.Clear();
        var snapshot = CaptureProjectSnapshot();
        _projectHistory.Add(new ProjectHistoryEntry(snapshot, Graph.SelectedNodeId));
        _projectHistoryIndex = 0;
        _savedProjectSnapshot = snapshot;
        UpdateHistoryControls();
    }

    private void RecordProjectHistorySnapshot(string? selectedNodeId)
    {
        var snapshot = CaptureProjectSnapshot();
        if (_projectHistoryIndex >= 0
            && _projectHistory[_projectHistoryIndex].Snapshot == snapshot)
        {
            _projectHistory[_projectHistoryIndex] =
                _projectHistory[_projectHistoryIndex] with { SelectedNodeId = selectedNodeId };
            UpdateHistoryControls();
            return;
        }

        if (_projectHistoryIndex < _projectHistory.Count - 1)
        {
            _projectHistory.RemoveRange(
                _projectHistoryIndex + 1,
                _projectHistory.Count - _projectHistoryIndex - 1);
        }

        _projectHistory.Add(new ProjectHistoryEntry(snapshot, selectedNodeId));
        if (_projectHistory.Count > MaxProjectHistoryEntries)
        {
            _projectHistory.RemoveAt(0);
        }
        _projectHistoryIndex = _projectHistory.Count - 1;
        UpdateHistoryControls();
    }

    private string CaptureProjectSnapshot()
    {
        PreservePendingSourceCode();
        return ProjectSerializer.ToJson(_project);
    }

    private bool CurrentProjectMatchesSavedSnapshot() =>
        _savedProjectSnapshot.Length > 0
        && CaptureProjectSnapshot() == _savedProjectSnapshot;

    private bool CanUndoProject => _projectHistoryIndex > 0;

    private bool CanRedoProject =>
        _projectHistoryIndex >= 0 && _projectHistoryIndex < _projectHistory.Count - 1;

    private void UndoProject()
    {
        if (!CanUndoProject)
        {
            StatusText.Text = "Нечего отменять";
            return;
        }
        RestoreProjectHistory(_projectHistoryIndex - 1, "Изменение отменено");
    }

    private void RedoProject()
    {
        if (!CanRedoProject)
        {
            StatusText.Text = "Нечего повторять";
            return;
        }
        RestoreProjectHistory(_projectHistoryIndex + 1, "Изменение повторено");
    }

    private void RestoreProjectHistory(int historyIndex, string statusText)
    {
        if (historyIndex < 0 || historyIndex >= _projectHistory.Count)
        {
            return;
        }

        _restoringProjectHistory = true;
        try
        {
            var entry = _projectHistory[historyIndex];
            _project = ProjectSerializer.FromJson(entry.Snapshot);
            _projectHistoryIndex = historyIndex;
            _codeHasPendingChanges = false;
            Graph.SetProject(_project);
            if (entry.SelectedNodeId is not null
                && _project.FindNode(entry.SelectedNodeId) is not null)
            {
                Graph.SelectNode(entry.SelectedNodeId);
            }
            RefreshExplorer();
            RefreshProperties();
            RefreshAssets(syncFromDisk: false);
            RequestCodeRefresh(useStoredSource: true);
            _dirty = !CurrentProjectMatchesSavedSnapshot();
            RefreshWindowTitle();
            RefreshProjectDiagnostics(showPanel: false);
            StatusText.Text = statusText;
        }
        finally
        {
            _restoringProjectHistory = false;
            UpdateHistoryControls();
        }
    }

    private void UpdateHistoryControls()
    {
        if (!IsInitialized)
        {
            return;
        }

        UndoMenuItem.IsEnabled = CanUndoProject;
        RedoMenuItem.IsEnabled = CanRedoProject;
    }

    private void RequestCodeRefresh(bool useStoredSource)
    {
        if (ReferenceEquals(WorkspaceTabs.SelectedItem, CodeTab))
        {
            RefreshCodeFromProject(useStoredSource);
            return;
        }

        var wasPending = _codeRefreshPending;
        _codeRefreshPending = true;
        _codeRefreshUseStoredSource = wasPending
            ? _codeRefreshUseStoredSource && useStoredSource
            : useStoredSource;
        if (!useStoredSource && !_codeHasPendingChanges)
        {
            _project.SourceCode = string.Empty;
        }
        CodeStatusText.Foreground = (Brush)FindResource("MutedBrush");
        CodeStatusText.Text = "Код обновится при открытии вкладки";
    }

    private void RefreshCodeFromProject(bool useStoredSource)
    {
        var code = useStoredSource && !string.IsNullOrWhiteSpace(_project.SourceCode)
            ? _project.SourceCode
            : ProjectLanguage.Format(_project);
        _project.SourceCode = code;

        _syncingCode = true;
        try
        {
            CodeEditor.SourceText = code;
            SetCodeCursorCache(code);
            _codeRefreshPending = false;
            _codeRefreshUseStoredSource = true;
            _codeHasPendingChanges = false;
            CodeStatusText.Foreground = (Brush)FindResource("MutedBrush");
            CodeStatusText.Text = "Код синхронизирован с графом";
        }
        finally
        {
            _syncingCode = false;
        }
        AnalyzeCode();
    }

    private bool EnsureCodeApplied() =>
        !_codeHasPendingChanges || TryApplyCode();

    private bool TryApplyCode()
    {
        try
        {
            var selectedNodeId = Graph.SelectedNodeId;
            var source = CodeEditor.SourceText;
            var compiled = GetParsedCodeProject(source);
            _parsedCodeSource = null;
            _parsedCodeProject = null;

            _project = compiled;
            _project.SourceCode = source;
            SetCodeCursorCache(source);
            _codeHasPendingChanges = false;
            Graph.SetProject(_project);
            if (_project.FindNode(selectedNodeId) is not null)
            {
                Graph.SelectNode(selectedNodeId);
            }
            RefreshExplorer();
            RefreshProperties();
            RefreshAssets(syncFromDisk: false);
            RecordProjectHistorySnapshot(selectedNodeId);
            _dirty = !CurrentProjectMatchesSavedSnapshot();
            RefreshWindowTitle();
            CodeStatusText.Foreground = (Brush)FindResource("AccentBrush");
            CodeStatusText.Text =
                $"Код применён: {_project.NodeTypes.Count} типов, {_project.Nodes.Count} нод";
            ApplyCodeHighlighting(source, null);
            StatusText.Text = "Код скомпилирован, граф обновлён";
            Graph.CenterGraph();
            RequestDiagnosticsRefresh();
            return true;
        }
        catch (ProjectLanguageException error)
        {
            ShowCodeError(error);
            return false;
        }
        catch (Exception error) when (
            error is InvalidDataException
            or InvalidOperationException)
        {
            CodeStatusText.Foreground = Brushes.IndianRed;
            CodeStatusText.Text = error.Message;
            WorkspaceTabs.SelectedItem = CodeTab;
            CodeEditor.Focus();
            return false;
        }
    }

    private void ShowCodeError(ProjectLanguageException error)
    {
        var source = CodeEditor.SourceText;
        var errorStart = ProjectLanguage.GetOffset(
            source,
            error.Line,
            error.Column);
        ApplyCodeHighlighting(source, error);
        CodeStatusText.Foreground = Brushes.IndianRed;
        CodeStatusText.Text = error.Message;
        WorkspaceTabs.SelectedItem = CodeTab;
        CodeEditor.SelectSourceRange(errorStart, ErrorTokenLength(source, errorStart));
    }

    private void ApplyCode_Click(object sender, RoutedEventArgs e) =>
        TryApplyCode();

    private void UpdateCodeFromGraph_Click(object sender, RoutedEventArgs e)
    {
        if (_codeHasPendingChanges)
        {
            var result = MessageBox.Show(
                this,
                "Неприменённый код будет заменён текущим состоянием графа.",
                "Обновить код из графа",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }
        RefreshCodeFromProject(useStoredSource: false);
        StatusText.Text = "Код обновлён из графа";
    }

    private void CodeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingCode)
        {
            return;
        }

        _codeHasPendingChanges = true;
        _codeCursorCacheDirty = true;
        _dirty = true;
        RefreshWindowTitle();
        CodeStatusText.Foreground = Brushes.Goldenrod;
        CodeStatusText.Text = "Проверка кода...";
        StatusText.Text = "Код проекта изменён";
        _codeAnalysisTimer.Stop();
        _codeAnalysisTimer.Start();
    }

    private void CodeEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var source = EnsureCodeCursorCache();
        if (source.Length > MaxHighlightedCodeLength)
        {
            if (_lastCodeCursorOffset != -2)
            {
                _lastCodeCursorOffset = -2;
                CodeCursorText.Text = "Большой файл: позиция курсора отключена";
            }
            return;
        }

        var offset = Math.Clamp(CodeEditor.SourceCaretOffset, 0, source.Length);
        if (offset == _lastCodeCursorOffset)
        {
            return;
        }
        _lastCodeCursorOffset = offset;

        var lineIndex = Array.BinarySearch(_codeLineStarts, offset);
        if (lineIndex < 0)
        {
            lineIndex = Math.Max(0, ~lineIndex - 1);
        }
        var lineStart = _codeLineStarts[Math.Min(lineIndex, _codeLineStarts.Length - 1)];
        CodeCursorText.Text =
            $"Строка {lineIndex + 1}, столбец {offset - lineStart + 1}";
    }

    private string EnsureCodeCursorCache()
    {
        if (!_codeCursorCacheDirty)
        {
            return _codeCursorSource;
        }

        SetCodeCursorCache(CodeEditor.SourceText);
        return _codeCursorSource;
    }

    private void SetCodeCursorCache(string source)
    {
        _codeCursorSource = source;
        _codeLineStarts = BuildLineStarts(source);
        _lastCodeCursorOffset = -1;
        _codeCursorCacheDirty = false;
    }

    private static int[] BuildLineStarts(string source)
    {
        var starts = new List<int> { 0 };
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\n' && index + 1 < source.Length)
            {
                starts.Add(index + 1);
            }
        }
        return starts.ToArray();
    }

    private void AnalyzeCode()
    {
        var source = CodeEditor.SourceText;
        SetCodeCursorCache(source);
        try
        {
            _ = GetParsedCodeProject(source);
            ApplyCodeHighlighting(source, null);
            CodeStatusText.Foreground = _codeHasPendingChanges
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("MutedBrush");
            CodeStatusText.Text = _codeHasPendingChanges
                ? "Ошибок нет — Ctrl+Enter применит изменения"
                : "Код синхронизирован с графом";
        }
        catch (ProjectLanguageException error)
        {
            ApplyCodeHighlighting(source, error);
            CodeStatusText.Foreground = Brushes.IndianRed;
            CodeStatusText.Text = error.Message;
        }
        catch (Exception error) when (
            error is InvalidDataException
            or InvalidOperationException)
        {
            ApplyCodeHighlighting(source, null);
            CodeStatusText.Foreground = Brushes.IndianRed;
            CodeStatusText.Text = error.Message;
        }
    }

    private void ApplyCodeHighlighting(
        string source,
        ProjectLanguageException? error)
    {
        var errorStart = error is null
            ? (int?)null
            : ProjectLanguage.GetOffset(source, error.Line, error.Column);
        var spans = ShouldApplyFullSyntaxHighlighting(source)
            ? GetCodeSyntaxSpans(source)
            : Array.Empty<ProjectLanguageSyntaxSpan>();
        if (error is null && spans.Count == 0 && source.Length > MaxHighlightedCodeLength)
        {
            return;
        }

        var wasSyncing = _syncingCode;
        _syncingCode = true;
        try
        {
            CodeEditor.ApplySyntax(
                spans,
                errorStart,
                errorStart.HasValue
                    ? ErrorTokenLength(source, errorStart.Value)
                    : 0);
        }
        finally
        {
            _syncingCode = wasSyncing;
        }
    }

    private static bool ShouldApplyFullSyntaxHighlighting(string source) =>
        source.Length <= MaxHighlightedCodeLength;

    private IReadOnlyList<ProjectLanguageSyntaxSpan> GetCodeSyntaxSpans(string source)
    {
        if (_codeSyntaxSource == source)
        {
            return _codeSyntaxSpans;
        }

        _codeSyntaxSource = source;
        _codeSyntaxSpans = ProjectLanguage.GetSyntaxSpans(source);
        if (_codeSyntaxSpans.Count > MaxHighlightedSyntaxSpans)
        {
            _codeSyntaxSpans = [];
        }
        return _codeSyntaxSpans;
    }

    private NovelProject GetParsedCodeProject(string source)
    {
        if (_parsedCodeSource == source && _parsedCodeProject is not null)
        {
            return _parsedCodeProject;
        }

        _parsedCodeSource = null;
        _parsedCodeProject = null;
        var parsed = ProjectLanguage.Parse(source);
        _parsedCodeSource = source;
        _parsedCodeProject = parsed;
        return _parsedCodeProject;
    }

    private static int ErrorTokenLength(string source, int start)
    {
        if (start >= source.Length)
        {
            return 1;
        }
        var end = start;
        while (end < source.Length
            && !char.IsWhiteSpace(source[end])
            && source[end] is not '{' and not '}')
        {
            end++;
        }
        return Math.Max(1, end - start);
    }

    private void NavigateToNodeCode(string nodeId)
    {
        if (!_codeHasPendingChanges && _codeRefreshPending)
        {
            RefreshCodeFromProject(false);
        }
        var location = ProjectLanguage.FindNodeDeclaration(
            CodeEditor.SourceText,
            nodeId);
        if (location is null)
        {
            StatusText.Text = $"Объявление ноды «{nodeId}» не найдено в коде";
            WorkspaceTabs.SelectedItem = CodeTab;
            return;
        }

        WorkspaceTabs.SelectedItem = CodeTab;
        CodeEditor.SelectSourceRange(location.Start, location.Length);
        StatusText.Text =
            $"Нода «{nodeId}»: строка {location.Line}, столбец {location.Column}";
    }

    private void CodeEditor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            TryApplyCode();
            e.Handled = true;
        }
    }

    private void WorkspaceTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, WorkspaceTabs))
        {
            return;
        }
        if (ReferenceEquals(WorkspaceTabs.SelectedItem, CodeTab)
            && !_codeHasPendingChanges)
        {
            if (_codeRefreshPending)
            {
                RefreshCodeFromProject(_codeRefreshUseStoredSource);
            }
        }
        else if (ReferenceEquals(WorkspaceTabs.SelectedItem, FilesTab))
        {
            if (_codeHasPendingChanges && !TryApplyCode())
            {
                return;
            }
            RefreshAssets();
        }
    }

    private void RefreshAssets(bool syncFromDisk = true)
    {
        if (syncFromDisk)
        {
            _assetSizeCache.Clear();
            SyncFilesFromDisk();
        }
        RefreshAssetFolders();
        RefreshAssetList();
    }

    private void RefreshNodeAssetPickers(NovelNode? node)
    {
        if (node is null)
        {
            BackgroundFolderBox.ItemsSource = Array.Empty<NodeAssetFolderOption>();
            BackgroundAssetBox.ItemsSource = Array.Empty<NodeAssetChoice>();
            MusicFolderBox.ItemsSource = Array.Empty<NodeAssetFolderOption>();
            MusicAssetBox.ItemsSource = Array.Empty<NodeAssetChoice>();
            return;
        }

        var backgroundEnabled = node.Kind == NodeKind.Start
            || InheritBackgroundCheck.IsChecked != true;
        var musicEnabled = node.Kind == NodeKind.Start
            || InheritMusicCheck.IsChecked != true;
        RefreshAssetPicker(
            BackgroundFolderBox,
            BackgroundAssetBox,
            AssetKind.Image,
            BackgroundBox.Text.Trim(),
            backgroundEnabled,
            ["backgrounds", "background", "bg", "images"]);
        RefreshAssetPicker(
            MusicFolderBox,
            MusicAssetBox,
            AssetKind.Audio,
            MusicBox.Text.Trim(),
            musicEnabled,
            ["music", "audio", "bgm", "sound"]);
    }

    private void RefreshAssetPicker(
        ComboBox folderBox,
        ComboBox assetBox,
        AssetKind kind,
        string currentReference,
        bool enabled,
        IReadOnlyList<string> preferredFolders)
    {
        var folderOptions = CreateFolderOptions(kind);
        folderBox.ItemsSource = folderOptions;
        var currentAsset = ResolveAssetChoice(currentReference);
        var selectedFolder =
            currentAsset?.Folder
            ?? PreferredFolder(folderOptions, preferredFolders)
            ?? string.Empty;
        folderBox.SelectedItem = folderOptions.FirstOrDefault(option =>
                option.Folder.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase))
            ?? folderOptions.First();
        RefreshAssetChoices(assetBox, kind, selectedFolder, currentReference);
        folderBox.IsEnabled = enabled && folderOptions.Count > 1;
        assetBox.IsEnabled = enabled;
    }

    private List<NodeAssetFolderOption> CreateFolderOptions(AssetKind kind)
    {
        var folders = _project.AssetFolders
            .Concat(_project.Assets.Select(asset => asset.Folder))
            .Select(ProjectAssets.NormalizeFolder)
            .Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return
        [
            new NodeAssetFolderOption(string.Empty, "Все папки"),
            .. folders.Select(folder => new NodeAssetFolderOption(
                folder,
                $"{folder} ({_project.Assets.Count(asset =>
                    asset.Kind == kind
                    && asset.Folder.Equals(folder, StringComparison.OrdinalIgnoreCase))})")),
        ];
    }

    private static string? PreferredFolder(
        IReadOnlyList<NodeAssetFolderOption> options,
        IReadOnlyList<string> preferredFolders)
    {
        foreach (var preferred in preferredFolders)
        {
            var found = options.FirstOrDefault(option =>
                option.Folder.Equals(preferred, StringComparison.OrdinalIgnoreCase)
                || option.Folder.EndsWith(
                    "/" + preferred,
                    StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return found.Folder;
            }
        }
        return null;
    }

    private NovelAsset? ResolveAssetChoice(string reference)
    {
        if (!reference.StartsWith("@", StringComparison.Ordinal))
        {
            return null;
        }
        return _project.FindAsset(reference[1..]);
    }

    private void RefreshAssetChoices(
        ComboBox assetBox,
        AssetKind kind,
        string folder,
        string currentReference)
    {
        _refreshingAssetPickers = true;
        try
        {
            var normalizedFolder = ProjectAssets.NormalizeFolder(folder);
            var choices = _project.Assets
                .Where(asset => asset.Kind == kind)
                .Where(asset =>
                    normalizedFolder.Length == 0
                    || asset.Folder.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                .OrderBy(asset => asset.Id, StringComparer.CurrentCultureIgnoreCase)
                .Select(asset => new NodeAssetChoice(
                    asset,
                    $"{asset.Id}  ·  {Path.GetFileName(asset.Path)}"))
                .Prepend(new NodeAssetChoice(null, "Не выбрано"))
                .ToList();
            assetBox.ItemsSource = choices;
            var currentAsset = ResolveAssetChoice(currentReference);
            assetBox.SelectedItem = currentAsset is null
                ? choices[0]
                : choices.FirstOrDefault(choice =>
                        ReferenceEquals(choice.Asset, currentAsset))
                    ?? choices[0];
        }
        finally
        {
            _refreshingAssetPickers = false;
        }
    }

    private bool SyncFilesFromDisk(bool refreshCode = true)
    {
        if (_projectPath is null || _syncingFilesFromDisk)
        {
            return false;
        }
        _syncingFilesFromDisk = true;
        try
        {
            var changes = ProjectAssets.SyncFromDisk(_project, _projectPath);
            if (changes == 0)
            {
                return false;
            }

            if (!_codeHasPendingChanges)
            {
                _project.SourceCode = string.Empty;
            }
            PreservePendingSourceCode();
            ProjectSerializer.Save(_project, _projectPath);
            _savedProjectSnapshot = CaptureProjectSnapshot();
            _dirty = false;
            _workspaceNeedsProjectFile = false;
            if (refreshCode && !_codeHasPendingChanges)
            {
                RequestCodeRefresh(useStoredSource: false);
            }
            RefreshWindowTitle();
            StatusText.Text = $"Файлы синхронизированы: найдено новых записей {changes}";
            return true;
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            StatusText.Text = $"Не удалось синхронизировать files: {error.Message}";
            return false;
        }
        finally
        {
            _syncingFilesFromDisk = false;
        }
    }

    private void ConfigureFilesWatcher()
    {
        _filesWatcher?.Dispose();
        _filesWatcher = null;
        if (_projectPath is null)
        {
            return;
        }

        var directory = ProjectAssets.GetAssetsDirectory(_projectPath);
        Directory.CreateDirectory(directory);
        _filesWatcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter =
                NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _filesWatcher.Created += (_, _) => ScheduleFilesRefresh();
        _filesWatcher.Renamed += (_, _) => ScheduleFilesRefresh();
        _filesWatcher.Changed += (_, _) => ScheduleFilesRefresh();
        _filesWatcher.Deleted += (_, _) => ScheduleFilesRefresh();
    }

    private void ScheduleFilesRefresh()
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            _filesRefreshTimer.Stop();
            _filesRefreshTimer.Start();
        });
    }

    private void RefreshAssetList()
    {
        var selectedId = (AssetsGrid.SelectedItem as AssetView)?.Id;
        var assets = _selectedAssetFolder is null
            ? _project.Assets
            : _project.Assets.Where(
                asset => asset.Folder.Equals(
                    _selectedAssetFolder,
                    StringComparison.OrdinalIgnoreCase));
        var folderAssetCount = assets.Count();
        var query = AssetSearchBox.Text.Trim();
        if (query.Length > 0)
        {
            assets = assets.Where(asset => AssetMatchesSearch(asset, query));
        }

        var usageCounts = _project.CountAssetReferencesById();
        var assetViews = assets
            .OrderBy(asset => asset.Kind)
            .ThenBy(asset => asset.Id, StringComparer.CurrentCultureIgnoreCase)
            .Select(
                asset => new AssetView(
                    asset,
                    usageCounts.GetValueOrDefault(asset.Id),
                    AssetSize(asset)))
            .ToList();
        AssetsGrid.ItemsSource = assetViews;
        AssetSearchSummaryText.Text = query.Length == 0
            ? string.Empty
            : $"Найдено {assetViews.Count} из {folderAssetCount}";
        if (selectedId is not null)
        {
            AssetsGrid.SelectedItem = AssetsGrid.Items
                .OfType<AssetView>()
                .FirstOrDefault(
                    view => view.Id.Equals(
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));
        }
        RefreshAssetPreview();
    }

    private static bool AssetMatchesSearch(NovelAsset asset, string query)
    {
        var tokens = query
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 || tokens.All(token =>
            ContainsSearchToken(asset.Id, token)
            || ContainsSearchToken(asset.Path, token)
            || ContainsSearchToken(asset.Folder, token)
            || ContainsSearchToken(Path.GetFileName(asset.Path), token)
            || ContainsSearchToken(asset.Kind.ToString(), token)
            || ContainsSearchToken(AssetKindLabel(asset.Kind), token));
    }

    private static bool ContainsSearchToken(string value, string token) =>
        value.Contains(token, StringComparison.CurrentCultureIgnoreCase);

    private static string AssetKindLabel(AssetKind kind) =>
        kind switch
        {
            AssetKind.Image => "изображение картинка image",
            AssetKind.Audio => "аудио музыка звук audio",
            _ => "файл file",
        };

    private void RefreshAssetFolders()
    {
        _refreshingAssetFolders = true;
        try
        {
            AssetFoldersTree.Items.Clear();
            var all = new TreeViewItem
            {
                Header = CreateFolderHeader("Все файлы", _project.Assets.Count),
                Tag = null,
                IsExpanded = true,
                IsSelected = _selectedAssetFolder is null,
            };
            AssetFoldersTree.Items.Add(all);

            var items = new Dictionary<string, TreeViewItem>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var folder in _project.AssetFolders
                .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
            {
                var parentPath = string.Empty;
                TreeViewItem? parent = null;
                foreach (var segment in folder.Split('/'))
                {
                    var path = parentPath.Length == 0
                        ? segment
                        : $"{parentPath}/{segment}";
                    if (!items.TryGetValue(path, out var item))
                    {
                        var count = _project.Assets.Count(
                            asset => asset.Folder.Equals(
                                path,
                                StringComparison.OrdinalIgnoreCase));
                        item = new TreeViewItem
                        {
                            Header = CreateFolderHeader(segment, count),
                            Tag = path,
                            IsExpanded = true,
                            IsSelected = path.Equals(
                                _selectedAssetFolder,
                                StringComparison.OrdinalIgnoreCase),
                        };
                        if (parent is null)
                        {
                            AssetFoldersTree.Items.Add(item);
                        }
                        else
                        {
                            parent.Items.Add(item);
                        }
                        items[path] = item;
                    }
                    parent = item;
                    parentPath = path;
                }
            }
        }
        finally
        {
            _refreshingAssetFolders = false;
        }
        var hasFolder = _selectedAssetFolder is not null;
        RenameFolderButton.IsEnabled = hasFolder;
        DeleteFolderButton.IsEnabled = hasFolder;
    }

    private static StackPanel CreateFolderHeader(string title, int count) =>
        new()
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock
                {
                    Text = "\uE8B7",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    Margin = new Thickness(0, 0, 7, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                },
                new TextBlock
                {
                    Text = $"{title} ({count})",
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        };

    private string GetVoiceBlipTargetFolder()
    {
        if (_selectedAssetFolder is not null
            && IsFolderOrChild(_selectedAssetFolder, "voices"))
        {
            return ProjectAssets.NormalizeFolder(_selectedAssetFolder);
        }
        return "voices";
    }

    private string CreateUniqueVoiceBlipPath(string folder, string name)
    {
        var safeStem = ProjectAssets.MakeId(name);
        var directory = Path.Combine(
            ProjectAssets.GetAssetsDirectory(_projectPath!),
            ProjectAssets.NormalizeFolder(folder).Replace(
                '/',
                Path.DirectorySeparatorChar));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{safeStem}.wav");
        var suffix = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{safeStem}-{suffix++}.wav");
        }
        return path;
    }

    private static bool IsFolderOrChild(string candidate, string folder)
    {
        candidate = ProjectAssets.NormalizeFolder(candidate);
        folder = ProjectAssets.NormalizeFolder(folder);
        return candidate.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private string AssetSize(NovelAsset asset)
    {
        var path = ResolveAssetPath(asset);
        if (_assetSizeCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        string size;
        if (!File.Exists(path))
        {
            size = "нет файла";
            _assetSizeCache[path] = size;
            return size;
        }
        var bytes = new FileInfo(path).Length;
        size = bytes switch
        {
            >= 1024L * 1024L =>
                $"{bytes / (1024d * 1024d):0.##} МБ",
            >= 1024L => $"{bytes / 1024d:0.##} КБ",
            _ => $"{bytes} Б",
        };
        _assetSizeCache[path] = size;
        return size;
    }

    private void RefreshAssetPreview()
    {
        StopAssetPreviewPlayback();
        _assetPreviewAudioPath = null;
        var view = AssetsGrid.SelectedItem as AssetView;
        var selected = view is not null;
        RenameAssetButton.IsEnabled = selected;
        MoveAssetButton.IsEnabled = selected;
        CopyAssetReferenceButton.IsEnabled = selected;
        FindAssetUsageButton.IsEnabled = selected;
        OpenSelectedAssetButton.IsEnabled = selected;
        DeleteAssetButton.IsEnabled = selected;
        AssetPreviewImage.Source = null;
        AssetPreviewAudioPanel.Visibility = Visibility.Collapsed;
        AssetPreviewPlayButton.IsEnabled = false;
        AssetPreviewStopButton.IsEnabled = false;
        AssetReferenceText.Text = selected
            ? AssetReference.Create(view!.Id)
            : string.Empty;
        AssetPathText.Text = selected ? view!.Path : string.Empty;
        AssetUsageText.Text = selected
            ? $"Использований в проекте: {view!.UsageCount}"
            : string.Empty;
        AssetPreviewPlaceholder.Text = selected
            ? view!.Asset.Kind == AssetKind.Audio
                ? "Аудиофайл"
                : view.Asset.Kind == AssetKind.Other
                    ? "Файл без предпросмотра"
                    : "Изображение не найдено"
            : "Выберите ассет";
        AssetPreviewPlaceholder.Visibility = Visibility.Visible;

        if (view?.Asset.Kind == AssetKind.Audio)
        {
            AssetPreviewAudioPanel.Visibility = Visibility.Visible;
            var audioPath = ResolveAssetPath(view.Asset);
            if (File.Exists(audioPath))
            {
                _assetPreviewAudioPath = audioPath;
                AssetPreviewPlayButton.IsEnabled = true;
                AssetPreviewPlaceholder.Text = "Аудиофайл готов к прослушиванию";
            }
            else
            {
                AssetPreviewPlaceholder.Text = "Аудиофайл не найден";
            }
            return;
        }

        if (view?.Asset.Kind != AssetKind.Image)
        {
            return;
        }
        var path = ResolveAssetPath(view.Asset);
        if (!File.Exists(path))
        {
            return;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 900;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            AssetPreviewImage.Source = image;
            AssetPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            AssetPreviewPlaceholder.Text = "Не удалось открыть изображение";
        }
    }

    private void AssetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshAssetPreview();

    private void AssetSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshAssetList();

    private void AssetSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || AssetSearchBox.Text.Length == 0)
        {
            return;
        }

        AssetSearchBox.Clear();
        e.Handled = true;
    }

    private void PlayAssetPreview_Click(object sender, RoutedEventArgs e)
    {
        if (_assetPreviewAudioPath is null || !File.Exists(_assetPreviewAudioPath))
        {
            StatusText.Text = "Аудиофайл для предпросмотра не найден";
            return;
        }

        try
        {
            _assetPreviewPlayer.Stop();
            _assetPreviewPlayer.Open(new Uri(_assetPreviewAudioPath, UriKind.Absolute));
            _assetPreviewPlayer.Play();
            AssetPreviewStopButton.IsEnabled = true;
            StatusText.Text =
                $"Прослушивание {Path.GetFileName(_assetPreviewAudioPath)}";
        }
        catch (Exception error) when (
            error is IOException
            or InvalidOperationException
            or NotSupportedException
            or UnauthorizedAccessException
            or UriFormatException)
        {
            AssetPreviewStopButton.IsEnabled = false;
            StatusText.Text = "Не удалось воспроизвести аудио";
            MessageBox.Show(
                this,
                error.Message,
                "Предпросмотр аудио",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void StopAssetPreview_Click(object sender, RoutedEventArgs e)
    {
        StopAssetPreviewPlayback();
        StatusText.Text = "Прослушивание остановлено";
    }

    private void StopAssetPreviewPlayback()
    {
        _assetPreviewPlayer.Stop();
        if (IsInitialized)
        {
            AssetPreviewStopButton.IsEnabled = false;
        }
    }

    private void AssetsGrid_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            return;
        }

        row.IsSelected = true;
        AssetsGrid.SelectedItem = row.Item;
        row.Focus();
    }

    private void AssetsGrid_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        var menu = AssetsGrid.ContextMenu ?? new ContextMenu();
        AssetsGrid.ContextMenu = menu;
        menu.Items.Clear();
        if (view is null)
        {
            e.Handled = true;
            return;
        }

        var selectedNode = _project.FindNode(Graph.SelectedNodeId);
        menu.Items.Add(CreateAssetMenuItem(
            "Скопировать ссылку",
            () => CopyAssetReference(view)));
        menu.Items.Add(CreateAssetMenuItem(
            "Где используется",
            () => ShowAssetUsages(view)));
        menu.Items.Add(CreateAssetMenuItem(
            "Открыть файл в проводнике",
            () => OpenSelectedAssetInExplorer(view.Asset)));

        if (view.Asset.Kind == AssetKind.Image)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateAssetMenuItem(
                "Привязать как фон выбранной ноды",
                () => BindAssetAsNodeBackground(view.Asset),
                selectedNode is not null));
            if (IsInAssetFolder(view.Asset, "characters"))
            {
                menu.Items.Add(CreateCharacterAssetVoiceMenu(view.Asset, selectedNode));
            }
        }

        if (view.Asset.Kind == AssetKind.Audio)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateAssetMenuItem(
                "Привязать как музыку выбранной ноды",
                () => BindAssetAsNodeMusic(view.Asset),
                selectedNode is not null));
            if (IsInAssetFolder(view.Asset, "voices"))
            {
                menu.Items.Add(CreateVoiceBindingMenu(view.Asset, selectedNode));
                menu.Items.Add(CreateLibraryVoiceBindingMenu(view.Asset));
            }
        }
    }

    private MenuItem CreateCharacterAssetVoiceMenu(NovelAsset asset, NovelNode? node)
    {
        var voices = _project.Assets
            .Where(candidate => candidate.Kind == AssetKind.Audio)
            .Where(candidate => IsInAssetFolder(candidate, "voices"))
            .OrderBy(candidate => candidate.Id, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (voices.Count == 0)
        {
            return CreateDisabledAssetMenuItem("Подвязать voice-блип: в voices нет аудио");
        }

        var targets = new List<(string Label, Action<NovelAsset> Bind)>();
        if (node is not null && FindEffectiveCharacterBySprite(node, asset) is { } nodeCharacter)
        {
            targets.Add((
                $"в выбранной ноде: «{CharacterLabel(nodeCharacter)}»",
                voice => BindVoiceAssetToCharacter(voice, nodeCharacter.Id)));
        }

        if (FindLibraryCharacterBySprite(asset) is { } libraryCharacter)
        {
            targets.Add((
                $"в библиотеке: «{CharacterLabel(libraryCharacter)}»",
                voice => BindVoiceAssetToLibraryCharacter(voice, libraryCharacter.Id)));
        }

        if (targets.Count == 0)
        {
            return CreateDisabledAssetMenuItem(
                "Подвязать voice-блип: персонаж со спрайтом не найден");
        }

        if (voices.Count == 1 && targets.Count == 1)
        {
            var voice = voices[0];
            return CreateAssetMenuItem(
                $"Добавить «{voice.Id}» {targets[0].Label}",
                () => targets[0].Bind(voice));
        }

        var menu = CreateHoverSubmenu("Добавить voice-блип из voices");
        foreach (var voice in voices)
        {
            if (targets.Count == 1)
            {
                menu.Items.Add(CreateAssetMenuItem(
                    $"{voice.Id}  ·  {Path.GetFileName(voice.Path)}",
                    () => targets[0].Bind(voice)));
                continue;
            }

            var voiceMenu = CreateHoverSubmenu(
                $"{voice.Id}  ·  {Path.GetFileName(voice.Path)}");
            foreach (var target in targets)
            {
                voiceMenu.Items.Add(CreateAssetMenuItem(
                    target.Label,
                    () => target.Bind(voice)));
            }
            menu.Items.Add(voiceMenu);
        }
        return menu;
    }

    private MenuItem CreateVoiceBindingMenu(NovelAsset asset, NovelNode? node)
    {
        if (node is null)
        {
            return CreateDisabledAssetMenuItem("Привязать voice-блип: выберите ноду");
        }

        var characters = GetEffectiveCharacters(node).ToList();
        if (characters.Count == 0)
        {
            return CreateDisabledAssetMenuItem("Привязать voice-блип: в ноде нет персонажей");
        }

        if (characters.Count == 1)
        {
            var character = characters[0];
            return CreateAssetMenuItem(
                $"Добавить voice-блип к «{CharacterLabel(character)}»",
                () => BindVoiceAssetToCharacter(asset, character.Id));
        }

        var menu = CreateHoverSubmenu("Добавить voice-блип к персонажу");
        foreach (var character in characters.OrderBy(
            character => character.Name,
            StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(CreateAssetMenuItem(
                $"Персонаж «{CharacterLabel(character)}»",
                () => BindVoiceAssetToCharacter(asset, character.Id)));
        }

        return menu;
    }

    private MenuItem CreateLibraryVoiceBindingMenu(NovelAsset asset)
    {
        if (_project.Characters.Count == 0)
        {
            return CreateDisabledAssetMenuItem(
                "Добавить voice-блип в библиотеку: персонажей нет");
        }

        if (_project.Characters.Count == 1)
        {
            var character = _project.Characters[0];
            return CreateAssetMenuItem(
                $"Добавить voice-блип в библиотеку: «{CharacterLabel(character)}»",
                () => BindVoiceAssetToLibraryCharacter(asset, character.Id));
        }

        var menu = CreateHoverSubmenu("Добавить voice-блип в библиотеку");
        foreach (var character in _project.Characters.OrderBy(
            character => CharacterLabel(character),
            StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(CreateAssetMenuItem(
                $"Персонаж «{CharacterLabel(character)}»",
                () => BindVoiceAssetToLibraryCharacter(asset, character.Id)));
        }

        return menu;
    }

    private static string CharacterLabel(CharacterPlacement character) =>
        string.IsNullOrWhiteSpace(character.Name)
            ? character.Id
            : character.Name;

    private static List<string> CharacterVoiceReferences(CharacterPlacement character)
        => character.GetVoiceSounds();

    private CharacterPlacement? FindEffectiveCharacterBySprite(
        NovelNode node,
        NovelAsset asset) =>
        GetEffectiveCharacters(node).FirstOrDefault(
            character => ReferencesAsset(character.Sprite, asset));

    private CharacterPlacement? FindLibraryCharacterBySprite(NovelAsset asset) =>
        _project.Characters.FirstOrDefault(
            character => ReferencesAsset(character.Sprite, asset));

    private static bool ReferencesAsset(string value, NovelAsset asset)
    {
        if (AssetReference.TryGetId(value, out var id))
        {
            return id.Equals(asset.Id, StringComparison.OrdinalIgnoreCase);
        }
        return value.Replace('\\', '/').Equals(
            asset.Path,
            StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<CharacterPlacement> GetEffectiveCharacters(NovelNode node)
    {
        if (node.Kind == NodeKind.Start || !node.InheritCharacters)
        {
            return node.Characters;
        }

        try
        {
            var player = new NovelPlayer(_project);
            _ = player.StartAt(node.Id);
            return player.State.CurrentCharacters
                .Select(character => character.Clone())
                .ToList();
        }
        catch (Exception error) when (
            error is InvalidDataException
            or InvalidOperationException)
        {
            return node.Characters;
        }
    }

    private void BindAssetAsNodeBackground(NovelAsset asset)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null || asset.Kind != AssetKind.Image)
        {
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        MarkOverrideIfChanged(node, "inheritBackground", node.InheritBackground, false);
        MarkOverrideIfChanged(node, "background", node.Background, reference);
        node.InheritBackground = false;
        node.Background = reference;
        MarkDirty();
        RefreshAfterAssetBinding();
        StatusText.Text = $"Фон ноды «{node.Title}»: {reference}";
    }

    private void BindAssetAsNodeMusic(NovelAsset asset)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null || asset.Kind != AssetKind.Audio)
        {
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        MarkOverrideIfChanged(node, "inheritMusic", node.InheritMusic, false);
        MarkOverrideIfChanged(node, "music", node.Music, reference);
        node.InheritMusic = false;
        node.Music = reference;
        MarkDirty();
        RefreshAfterAssetBinding();
        StatusText.Text = $"Музыка ноды «{node.Title}»: {reference}";
    }

    private void BindVoiceAssetToCharacter(NovelAsset asset, string characterId)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null || asset.Kind != AssetKind.Audio)
        {
            return;
        }

        if (node.Kind != NodeKind.Start && node.InheritCharacters)
        {
            var effectiveCharacters = GetEffectiveCharacters(node)
                .Select(character => character.Clone())
                .ToList();
            node.Characters.Clear();
            node.Characters.AddRange(effectiveCharacters);
            node.InheritCharacters = false;
            if (node.UsesTypeDefaults)
            {
                node.PropertyOverrides.Add("inheritCharacters");
            }
        }

        var character = node.Characters.FirstOrDefault(
            candidate => candidate.Id == characterId);
        if (character is null)
        {
            StatusText.Text = "Персонаж для привязки voice-блипа не найден";
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        var voices = CharacterVoiceReferences(character);
        if (!voices.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            voices.Add(reference);
        }
        character.SetVoiceSounds(voices);
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("characters");
        }

        MarkDirty();
        RefreshAfterAssetBinding();
        StatusText.Text =
            $"Voice-блипы персонажа «{character.Name}»: {character.VoiceSounds.Count}";
    }

    private void BindVoiceAssetToLibraryCharacter(
        NovelAsset asset,
        string characterId)
    {
        if (asset.Kind != AssetKind.Audio)
        {
            return;
        }

        var character = _project.FindCharacter(characterId);
        if (character is null)
        {
            StatusText.Text = "Персонаж библиотеки для voice-блипа не найден";
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        var voices = CharacterVoiceReferences(character);
        if (!voices.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            voices.Add(reference);
        }
        character.SetVoiceSounds(voices);

        MarkDirty();
        RefreshAfterAssetBinding();
        StatusText.Text =
            $"Voice-блипы библиотечного персонажа «{CharacterLabel(character)}»: {character.VoiceSounds.Count}";
    }

    private void RefreshAfterAssetBinding()
    {
        Graph.RefreshGraph();
        RefreshProperties();
        RefreshAssets(syncFromDisk: false);
        RequestCodeRefresh(useStoredSource: false);
        RequestDiagnosticsRefresh();
    }

    private static bool IsInAssetFolder(NovelAsset asset, string folder)
    {
        var assetFolder = ProjectAssets.NormalizeFolder(asset.Folder);
        folder = ProjectAssets.NormalizeFolder(folder);
        return assetFolder.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || assetFolder.StartsWith(
                folder + "/",
                StringComparison.OrdinalIgnoreCase);
    }

    private void CopyAssetReference(AssetView view)
    {
        try
        {
            Clipboard.SetText(AssetReference.Create(view.Id));
            StatusText.Text = $"Скопировано: @{view.Id}";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusText.Text = "Буфер обмена временно недоступен";
        }
    }

    private static MenuItem CreateAssetMenuItem(
        string header,
        Action action,
        bool isEnabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = isEnabled,
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowAssetUsages(AssetView view)
    {
        var dialog = new AssetUsageWindow(
            view.Asset,
            _project.FindAssetUsages(view.Id))
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true && dialog.SelectedUsage is { } usage)
        {
            NavigateToAssetUsage(usage);
        }
    }

    private void NavigateToAssetUsage(AssetUsage usage)
    {
        if (usage.NodeId is not null
            && _project.FindNode(usage.NodeId) is { } node)
        {
            Graph.SelectNode(node.Id);
            WorkspaceTabs.SelectedItem = GraphTab;
            SelectAssetUsageDetail(usage.Location);
            StatusText.Text =
                $"Ассет используется в ноде «{DiagnosticNodeDisplay(node)}»";
            return;
        }

        if (usage.Location.StartsWith("главное меню", StringComparison.OrdinalIgnoreCase))
        {
            EditMainMenu();
            return;
        }

        if (usage.Location.StartsWith("тип ", StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceTabs.SelectedItem = CodeTab;
            StatusText.Text =
                $"Ассет используется в настройках типа: {usage.Location}";
            return;
        }

        if (usage.Location.StartsWith(
            "библиотека персонажей",
            StringComparison.OrdinalIgnoreCase))
        {
            WorkspaceTabs.SelectedItem = GraphTab;
            StatusText.Text =
                $"Ассет используется в библиотеке персонажей: {usage.Location}";
            return;
        }

        StatusText.Text = $"Ассет используется: {usage.Location}";
    }

    private void SelectAssetUsageDetail(string location)
    {
        var outputLabel = TryReadLocationTail(location, "переход ");
        if (outputLabel is not null)
        {
            var outputView = OutputsGrid.Items
                .OfType<OutputView>()
                .FirstOrDefault(view => view.Label.Equals(
                    outputLabel,
                    StringComparison.Ordinal));
            if (outputView is not null)
            {
                OutputsGrid.SelectedItem = outputView;
                OutputsGrid.ScrollIntoView(outputView);
            }
            return;
        }

        var characterName = TryReadLocationTail(location, "голос персонажа ")
            ?? TryReadLocationTail(location, "персонаж ");
        if (characterName is null)
        {
            return;
        }

        var characterView = CharactersGrid.Items
            .OfType<CharacterView>()
            .FirstOrDefault(view => view.Name.Equals(
                characterName,
                StringComparison.Ordinal));
        if (characterView is not null)
        {
            CharactersGrid.SelectedItem = characterView;
            CharactersGrid.ScrollIntoView(characterView);
        }
    }

    private static string? TryReadLocationTail(string location, string marker)
    {
        var start = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var value = location[(start + marker.Length)..].Trim();
        return value.Length == 0 ? null : value;
    }

    private static MenuItem CreateHoverSubmenu(string header)
    {
        var item = new MenuItem { Header = header };
        item.MouseEnter += (_, _) =>
        {
            if (item.IsEnabled && item.HasItems)
            {
                item.IsSubmenuOpen = true;
            }
        };
        return item;
    }

    private static MenuItem CreateDisabledAssetMenuItem(string header) =>
        new()
        {
            Header = header,
            IsEnabled = false,
        };

    private static T? FindVisualParent<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T typed)
            {
                return typed;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void AssetFoldersTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_refreshingAssetFolders)
        {
            return;
        }
        _selectedAssetFolder = (e.NewValue as TreeViewItem)?.Tag as string;
        RefreshAssetList();
    }

    private void OpenAssetManager_Click(object sender, RoutedEventArgs e) =>
        WorkspaceTabs.SelectedItem = FilesTab;

    private void ImportAssets_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Импортировать ассеты в проект",
            Filter =
                "Ассеты|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.mp3;*.wav;*.wma;*.aac;*.m4a;*.ogg;*.flac|"
                + "Все файлы|*.*",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var before = _project.Assets.Count;
            foreach (var file in dialog.FileNames)
            {
                _ = ImportAssetFile(file);
            }
            if (_project.Assets.Count != before)
            {
                MarkDirty();
            }
            RefreshAssets();
            StatusText.Text = $"Импортировано файлов: {dialog.FileNames.Length}";
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Не удалось импортировать ассет:\n\n{error.Message}",
                "Импорт файлов",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CreateVoiceBlip_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return;
        }

        var dialog = new VoiceBlipEditorWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folder = GetVoiceBlipTargetFolder();
            ProjectAssets.CreateFolder(_project, folder);
            var targetPath = CreateUniqueVoiceBlipPath(folder, dialog.BlipName);
            VoiceBlipGenerator.WriteWaveFile(targetPath, dialog.Options);
            var asset = ProjectAssets.Import(_project, _projectPath!, targetPath, folder);
            _selectedAssetFolder = folder;
            MarkDirty();
            RefreshAssets(syncFromDisk: false);
            AssetsGrid.SelectedItem = AssetsGrid.Items
                .OfType<AssetView>()
                .FirstOrDefault(view => view.Id == asset.Id);
            StatusText.Text = $"Создан voice-блип @{asset.Id}";
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Создать voice-блип",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CreateAssetFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return;
        }
        var dialog = new AssetFolderEditorWindow("Новая папка") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        var folder = _selectedAssetFolder is null
            ? dialog.FolderName
            : $"{_selectedAssetFolder}/{dialog.FolderName}";
        try
        {
            ProjectAssets.CreateFolder(_project, folder);
            _selectedAssetFolder = ProjectAssets.NormalizeFolder(folder);
            if (_projectPath is not null)
            {
                Directory.CreateDirectory(
                    Path.Combine(
                        ProjectAssets.GetAssetsDirectory(_projectPath),
                        _selectedAssetFolder.Replace(
                            '/',
                            Path.DirectorySeparatorChar)));
            }
            MarkDirty();
            RefreshAssets();
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Новая папка",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void RenameAssetFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAssetFolder is null || !EnsureProjectSavedForAssets())
        {
            return;
        }
        var currentName = _selectedAssetFolder.Split('/')[^1];
        var dialog = new AssetFolderEditorWindow(
            "Переименовать папку",
            currentName)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.FolderName == currentName)
        {
            return;
        }
        try
        {
            var parentSeparator = _selectedAssetFolder.LastIndexOf('/');
            var parent = parentSeparator < 0
                ? string.Empty
                : _selectedAssetFolder[..parentSeparator];
            ProjectAssets.RenameFolder(
                _project,
                _projectPath!,
                _selectedAssetFolder,
                dialog.FolderName);
            _selectedAssetFolder = parent.Length == 0
                ? dialog.FolderName
                : $"{parent}/{dialog.FolderName}";
            MarkDirty();
            RefreshAssets();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Переименование папки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void DeleteAssetFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAssetFolder is null || !EnsureProjectSavedForAssets())
        {
            return;
        }
        var result = MessageBox.Show(
            this,
            $"Удалить пустую папку «{_selectedAssetFolder}»?",
            "Удаление папки",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK)
        {
            return;
        }
        try
        {
            ProjectAssets.DeleteFolder(
                _project,
                _projectPath!,
                _selectedAssetFolder);
            _selectedAssetFolder = null;
            MarkDirty();
            RefreshAssets();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Удаление папки",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void MoveAsset_Click(object sender, RoutedEventArgs e)
    {
        var asset = (AssetsGrid.SelectedItem as AssetView)?.Asset;
        if (asset is null || !EnsureProjectSavedForAssets())
        {
            return;
        }
        var dialog = new AssetFolderPickerWindow(
            _project.AssetFolders,
            asset.Folder)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            ProjectAssets.MoveAsset(
                _project,
                _projectPath!,
                asset,
                dialog.SelectedFolder);
            _selectedAssetFolder = dialog.SelectedFolder;
            MarkDirty();
            RefreshAssets();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Перемещение ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string ImportAssetFile(string sourcePath, string? folder = null)
    {
        if (_projectPath is null)
        {
            throw new InvalidOperationException(
                "Сначала сохраните проект, чтобы импортировать ассеты.");
        }
        var asset = ProjectAssets.Import(
            _project,
            _projectPath,
            sourcePath,
            folder ?? _selectedAssetFolder);
        return AssetReference.Create(asset.Id);
    }

    private void RenameAsset_Click(object sender, RoutedEventArgs e)
    {
        var asset = (AssetsGrid.SelectedItem as AssetView)?.Asset;
        if (asset is null)
        {
            return;
        }
        var dialog = new AssetIdEditorWindow(asset.Id) { Owner = this };
        if (dialog.ShowDialog() != true
            || asset.Id.Equals(dialog.AssetId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (_project.FindAsset(dialog.AssetId) is not null)
        {
            MessageBox.Show(
                this,
                "Ассет с таким именем уже существует.",
                "Переименование ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var oldId = asset.Id;
        _project.ReplaceAssetReference(
            oldId,
            AssetReference.Create(dialog.AssetId));
        asset.Id = dialog.AssetId;
        MarkDirty();
        RefreshAssets();
        AssetsGrid.SelectedItem = AssetsGrid.Items
            .OfType<AssetView>()
            .FirstOrDefault(view => view.Id == dialog.AssetId);
    }

    private void CopyAssetReference_Click(object sender, RoutedEventArgs e)
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        if (view is null)
        {
            return;
        }
        CopyAssetReference(view);
    }

    private void FindAssetUsage_Click(object sender, RoutedEventArgs e)
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        if (view is null)
        {
            return;
        }
        ShowAssetUsages(view);
    }

    private void OpenSelectedAsset_Click(object sender, RoutedEventArgs e)
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        if (view is null)
        {
            return;
        }
        OpenSelectedAssetInExplorer(view.Asset);
    }

    private void DeleteAsset_Click(object sender, RoutedEventArgs e)
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        if (view is null)
        {
            return;
        }
        var result = MessageBox.Show(
            this,
            $"Удалить ассет «@{view.Id}»?\n\n"
            + $"Использований: {view.UsageCount}.\n"
            + "Да — удалить запись и физический файл.\n"
            + "Нет — удалить только запись из проекта.",
            "Удаление ассета",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Cancel)
        {
            return;
        }

        _project.ReplaceAssetReference(view.Id, string.Empty);
        _project.Assets.Remove(view.Asset);
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                DeleteManagedAssetFile(view.Asset);
            }
            catch (Exception error) when (
                error is IOException
                or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    this,
                    $"Запись удалена из проекта, но файл удалить не удалось:\n\n{error.Message}",
                    "Удаление ассета",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        MarkDirty();
        RefreshAssets();
    }

    private void DeleteManagedAssetFile(NovelAsset asset)
    {
        if (_projectPath is null)
        {
            return;
        }
        var projectDirectory = Path.GetDirectoryName(Path.GetFullPath(_projectPath))!;
        var managedDirectories = new[]
        {
            Path.GetFullPath(ProjectAssets.GetAssetsDirectory(_projectPath))
                + Path.DirectorySeparatorChar,
            Path.GetFullPath(
                    Path.Combine(
                        projectDirectory,
                        ProjectAssets.LegacyAssetsDirectoryName))
                + Path.DirectorySeparatorChar,
        };
        var path = ResolveAssetPath(asset);
        if (managedDirectories.Any(directory =>
                path.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void OpenAssetsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return;
        }
        var directory = _projectPath is null
            ? Path.Combine(GetAssetDirectory(), ProjectAssets.ManagedFilesDirectoryName)
            : ProjectAssets.GetAssetsDirectory(_projectPath);
        try
        {
            Directory.CreateDirectory(directory);
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true,
                });
        }
        catch (Exception error) when (IsShellOpenError(error))
        {
            ShowShellOpenError("Открыть папку files", error);
        }
    }

    private void OpenSelectedAssetInExplorer(NovelAsset asset)
    {
        try
        {
            var path = ResolveAssetPath(asset);
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (File.Exists(fullPath))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{fullPath}\"",
                        UseShellExecute = true,
                    });
                StatusText.Text = $"Открыт файл {Path.GetFileName(fullPath)}";
                return;
            }

            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true,
                    });
            }
            StatusText.Text = $"Файл ассета не найден: {fullPath}";
        }
        catch (Exception error) when (IsShellOpenError(error))
        {
            ShowShellOpenError("Открыть ассет", error);
        }
    }

    private bool IsShellOpenError(Exception error) =>
        error is IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or Win32Exception;

    private void ShowShellOpenError(string title, Exception error)
    {
        StatusText.Text = $"{title}: {error.Message}";
        MessageBox.Show(
            this,
            error.Message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private bool EnsureProjectSavedForAssets()
    {
        if (!EnsureCodeApplied())
        {
            return false;
        }
        if (_projectPath is not null)
        {
            EnsureWorkspaceStructure();
            return true;
        }
        if (_workspaceDirectory is not null)
        {
            return SaveProjectToWorkspace();
        }
        MessageBox.Show(
            this,
            "Сначала сохраните проект. Файлы будут скопированы в папку files рядом с ним.",
            "Файлы проекта",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return SaveProjectAs();
    }

    private string ResolveAssetPath(NovelAsset asset)
    {
        return _projectPath is null
            ? Path.GetFullPath(asset.Path)
            : ProjectAssets.ResolvePath(_projectPath, asset);
    }

    private void RefreshWindowTitle()
    {
        var name = _projectPath is null
            ? _workspaceDirectory is null
                ? "Без имени"
                : $"{Path.GetFileName(_workspaceDirectory)} / Без имени"
            : Path.GetFileName(_projectPath);
        Title = $"{name}{(_dirty ? " *" : string.Empty)} — Novel Engine WPF";
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        NewProject();
    }

    private void NewProject()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var directory = ChooseProjectFolder("Выберите папку для нового проекта");
        if (directory is null)
        {
            return;
        }

        try
        {
            OpenProject(ProjectWorkspace.CreateProjectInDirectory(directory));
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                $"Не удалось создать проект:\n\n{error.Message}",
                "Создание проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var directory = ChooseProjectFolder("Выберите папку с проектом");
        if (directory is null)
        {
            return;
        }

        try
        {
            OpenProject(directory);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть проект:\n\n{error.Message}",
                "Открытие проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenStartupProject(string inputPath)
    {
        try
        {
            OpenProject(inputPath);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                this,
                $"Не удалось открыть проект:\n\n{error.Message}",
                "Открытие проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenProject(string inputPath)
    {
        var result = ProjectOpenResolver.Resolve(inputPath);
        if (result.ProjectPath is null)
        {
            SetProject(
                NovelProject.CreateDefault(),
                null,
                result.WorkspaceDirectory);
            RecentProjectsStore.Remember(result.WorkspaceDirectory);
            StatusText.Text =
                $"Открыта папка {Path.GetFileName(result.WorkspaceDirectory)}. Нажмите Ctrl+S, чтобы создать проект в этой папке.";
            return;
        }

        SetProject(
            ProjectSerializer.Load(result.ProjectPath),
            result.ProjectPath,
            result.WorkspaceDirectory);
        RecentProjectsStore.Remember(result.ProjectPath);
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject();

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e) => SaveProjectAs();

    private void RestoreAutoSave_Click(object sender, RoutedEventArgs e) => RestoreAutoSave();

    private void Undo_Click(object sender, RoutedEventArgs e) => UndoProject();

    private void Redo_Click(object sender, RoutedEventArgs e) => RedoProject();

    private void RestoreAutoSave()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var autoSaveDirectory = GetAutoSaveDirectory();
        if (autoSaveDirectory is null
            || !Directory.Exists(autoSaveDirectory)
            || !Directory.EnumerateFiles(autoSaveDirectory, "*.novel.json").Any())
        {
            MessageBox.Show(
                this,
                "Для текущего проекта пока нет автосейвов.",
                "Восстановление автосейва",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Выберите автосейв Novel Engine",
            Filter = "Автосейвы Novel Engine|*.novel.json|JSON|*.json",
            InitialDirectory = autoSaveDirectory,
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var targetProjectPath = _projectPath;
        var targetWorkspaceDirectory = _workspaceDirectory
            ?? (targetProjectPath is null
                ? Path.GetDirectoryName(Path.GetFullPath(dialog.FileName))
                : Path.GetDirectoryName(Path.GetFullPath(targetProjectPath)));
        try
        {
            SetProject(
                ProjectSerializer.Load(dialog.FileName),
                targetProjectPath,
                targetWorkspaceDirectory,
                saveStructureChanges: false);
            _savedProjectSnapshot = string.Empty;
            _dirty = true;
            RefreshWindowTitle();
            StatusText.Text =
                $"Восстановлен автосейв {Path.GetFileName(dialog.FileName)}. Нажмите Ctrl+S, чтобы применить.";
            RequestDiagnosticsRefresh();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException
            or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                this,
                $"Не удалось восстановить автосейв:\n\n{error.Message}",
                "Восстановление автосейва",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool SaveProject()
    {
        if (!EnsureCodeApplied())
        {
            return false;
        }
        if (_projectPath is not null)
        {
            SyncFilesFromDisk(refreshCode: false);
        }
        if (_projectPath is null)
        {
            return _workspaceDirectory is not null
                ? SaveProjectToWorkspace()
                : SaveProjectAs();
        }
        return WriteProject(_projectPath);
    }

    private bool SaveProjectAs()
    {
        if (!EnsureCodeApplied())
        {
            return false;
        }
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить проект новеллы",
            Filter = "Проект Novel Engine|*.novel.json|JSON|*.json",
            DefaultExt = ".novel.json",
            AddExtension = true,
            InitialDirectory = GetSaveDialogInitialDirectory(),
            FileName = GetDefaultProjectFileName(),
        };
        return dialog.ShowDialog(this) == true && WriteProject(dialog.FileName);
    }

    private bool WriteProject(string path)
    {
        var previousProjectPath = _projectPath;
        var previousWorkspaceDirectory = _workspaceDirectory;
        var previousWorkspaceNeedsProjectFile = _workspaceNeedsProjectFile;
        try
        {
            var fullPath = Path.GetFullPath(path);
            _projectPath = fullPath;
            _workspaceDirectory = NormalizeWorkspaceDirectory(Path.GetDirectoryName(fullPath));
            _workspaceNeedsProjectFile = false;
            EnsureWorkspaceStructure();
            ProjectSerializer.Save(_project, fullPath);
            ConfigureFilesWatcher();
            _savedProjectSnapshot = CaptureProjectSnapshot();
            _dirty = false;
            UpdateHistoryControls();
            RefreshWindowTitle();
            StatusText.Text = $"Сохранён {Path.GetFileName(fullPath)}";
            RecentProjectsStore.Remember(fullPath);
            return true;
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            _projectPath = previousProjectPath;
            _workspaceDirectory = previousWorkspaceDirectory;
            _workspaceNeedsProjectFile = previousWorkspaceNeedsProjectFile;
            MessageBox.Show(
                this,
                $"Не удалось сохранить проект:\n\n{error.Message}",
                "Сохранение проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool SaveProjectToWorkspace()
    {
        if (_workspaceDirectory is null)
        {
            return SaveProjectAs();
        }
        var path = _workspaceNeedsProjectFile
            ? ProjectWorkspace.GetAvailableProjectPath(_workspaceDirectory)
            : Path.Combine(_workspaceDirectory, GetDefaultProjectFileName());
        return WriteProject(path);
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            "Сохранить изменения перед продолжением?",
            "Несохранённые изменения",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveProject(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }
        _autoSaveTimer.Stop();
        _filesWatcher?.Dispose();
        _assetPreviewPlayer.Close();
        StopGameProcess(silent: true);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void AddScene_Click(object sender, RoutedEventArgs e) =>
        Graph.AddNodeAtCenter(NodeKind.Scene);

    private void AddDialogue_Click(object sender, RoutedEventArgs e) =>
        Graph.AddNodeAtCenter(NodeKind.Dialogue);

    private void AddConnectedScene_Click(object sender, RoutedEventArgs e) =>
        Graph.AddConnectedSceneFromSelected();

    private void AddConnectedDialogue_Click(object sender, RoutedEventArgs e) =>
        Graph.AddConnectedDialogueFromSelected();

    private void DeleteNode_Click(object sender, RoutedEventArgs e) =>
        Graph.DeleteSelected();

    private void DuplicateNode_Click(object sender, RoutedEventArgs e) =>
        Graph.DuplicateSelected();

    private void CenterGraph_Click(object sender, RoutedEventArgs e) =>
        Graph.CenterGraph();

    private void ProjectTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingSelection)
        {
            return;
        }
        Graph.SelectNode((e.NewValue as TreeViewItem)?.Tag as string);
    }

    private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshExplorer();

    private void ProjectSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var query = ProjectSearchBox.Text.Trim();
        var node = FilteredNodes(query).FirstOrDefault();
        if (node is null)
        {
            StatusText.Text = query.Length == 0
                ? "В проекте нет нод"
                : $"По запросу «{query}» ничего не найдено";
            e.Handled = true;
            return;
        }

        Graph.SelectNode(node.Id);
        RefreshExplorer();
        StatusText.Text = $"Найдена нода «{node.Title}»";
        e.Handled = true;
    }

    private void Inheritance_Changed(object sender, RoutedEventArgs e)
    {
        if (_refreshingProperties)
        {
            return;
        }

        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null)
        {
            return;
        }
        BackgroundBox.IsEnabled = node.Kind == NodeKind.Start
            || InheritBackgroundCheck.IsChecked != true;
        BackgroundFolderBox.IsEnabled = BackgroundBox.IsEnabled;
        BackgroundAssetBox.IsEnabled = BackgroundBox.IsEnabled;
        MusicBox.IsEnabled = node.Kind == NodeKind.Start
            || InheritMusicCheck.IsChecked != true;
        MusicFolderBox.IsEnabled = MusicBox.IsEnabled;
        MusicAssetBox.IsEnabled = MusicBox.IsEnabled;
        SetCharacterButtons(
            node.Kind == NodeKind.Start || InheritCharactersCheck.IsChecked != true);
    }

    private void BackgroundFolderBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingProperties)
        {
            return;
        }
        var folder = (BackgroundFolderBox.SelectedItem as NodeAssetFolderOption)?.Folder
            ?? string.Empty;
        RefreshAssetChoices(
            BackgroundAssetBox,
            AssetKind.Image,
            folder,
            BackgroundBox.Text.Trim());
    }

    private void BackgroundAssetBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingProperties
            || _refreshingAssetPickers
            || BackgroundAssetBox.SelectedItem is not NodeAssetChoice choice)
        {
            return;
        }
        InheritBackgroundCheck.IsChecked = false;
        BackgroundBox.Text = choice.Asset is null
            ? string.Empty
            : AssetReference.Create(choice.Asset.Id);
        _ = ApplyProperties();
    }

    private void MusicFolderBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingProperties)
        {
            return;
        }
        var folder = (MusicFolderBox.SelectedItem as NodeAssetFolderOption)?.Folder
            ?? string.Empty;
        RefreshAssetChoices(
            MusicAssetBox,
            AssetKind.Audio,
            folder,
            MusicBox.Text.Trim());
    }

    private void MusicAssetBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_refreshingProperties
            || _refreshingAssetPickers
            || MusicAssetBox.SelectedItem is not NodeAssetChoice choice)
        {
            return;
        }
        InheritMusicCheck.IsChecked = false;
        MusicBox.Text = choice.Asset is null
            ? string.Empty
            : AssetReference.Create(choice.Asset.Id);
        _ = ApplyProperties();
    }

    private void BrowseBackground_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseAsset(
            "Выберите фон",
            "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif",
            "backgrounds");
        if (path is not null)
        {
            InheritBackgroundCheck.IsChecked = false;
            BackgroundBox.Text = path;
        }
    }

    private void BrowseMusic_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseAsset(
            "Выберите музыку",
            "Аудио|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.ogg;*.flac",
            "audio");
        if (path is not null)
        {
            InheritMusicCheck.IsChecked = false;
            MusicBox.Text = path;
        }
    }

    private string? BrowseAsset(string title, string filter, string folder)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return null;
        }
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{filter}|Все файлы|*.*",
            InitialDirectory = _projectPath is null
                ? Path.Combine(GetAssetDirectory(), ProjectAssets.ManagedFilesDirectoryName)
                : ProjectAssets.GetAssetsDirectory(_projectPath),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }
        try
        {
            var count = _project.Assets.Count;
            var reference = ImportAssetFile(dialog.FileName, folder);
            if (_project.Assets.Count != count)
            {
                MarkDirty();
                RefreshAssets();
            }
            return reference;
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Импорт ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }
    }

    private void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null)
        {
            return;
        }

        var dialog = new CharacterEditorWindow(
            null,
            GetCharacterSpriteAssets(),
            GetVoiceBlipAssets())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var voiceSounds = NormalizeVoiceReferences(dialog.VoiceSounds);
        node.InheritCharacters = false;
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("inheritCharacters");
            node.PropertyOverrides.Add("characters");
        }
        InheritCharactersCheck.IsChecked = false;
        node.Characters.Add(
            new CharacterPlacement
            {
                Id = $"character-{Guid.NewGuid():N}",
                Name = dialog.CharacterName,
                Sprite = NormalizeAssetPath(dialog.Sprite, "characters"),
                Position = dialog.Position,
                VoiceSound = voiceSounds.FirstOrDefault() ?? string.Empty,
                VoiceSounds = voiceSounds,
                VoicePitch = dialog.VoicePitch,
                VoiceEveryNthCharacter = dialog.VoiceEveryNthCharacter,
            });
        MarkDirty();
    }

    private void AddLibraryCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node is null)
        {
            return;
        }
        if (_project.Characters.Count == 0)
        {
            MessageBox.Show(
                this,
                "Библиотека персонажей пока пуста. Добавьте персонажа на сцену и нажмите «В библиотеку».",
                "Библиотека персонажей",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new CharacterLibraryPickerWindow(_project.Characters)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.SelectedCharacter is null)
        {
            return;
        }

        node.InheritCharacters = false;
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("inheritCharacters");
            node.PropertyOverrides.Add("characters");
        }
        InheritCharactersCheck.IsChecked = false;
        _project.AddCharacterClone(node.Id, dialog.SelectedCharacter);
        MarkDirty();
        StatusText.Text = $"Персонаж «{CharacterLabel(dialog.SelectedCharacter)}» добавлен из библиотеки";
    }

    private void SaveCharacterToLibrary_Click(object sender, RoutedEventArgs e)
    {
        var character = SelectedCharacter();
        if (character is null)
        {
            return;
        }

        var existing = _project.FindCharacter(character.Id);
        if (existing is null)
        {
            _project.Characters.Add(character.Clone());
            StatusText.Text = $"Персонаж «{CharacterLabel(character)}» добавлен в библиотеку";
        }
        else
        {
            CopyCharacterValues(character, existing);
            StatusText.Text = $"Персонаж «{CharacterLabel(character)}» обновлён в библиотеке";
        }
        MarkDirty();
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        var character = SelectedCharacter();
        if (character is null)
        {
            return;
        }

        var dialog = new CharacterEditorWindow(
            character,
            GetCharacterSpriteAssets(),
            GetVoiceBlipAssets())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var voiceSounds = NormalizeVoiceReferences(dialog.VoiceSounds);
        character.Name = dialog.CharacterName;
        character.Sprite = NormalizeAssetPath(dialog.Sprite, "characters");
        character.SetVoiceSounds(voiceSounds);
        character.VoicePitch = dialog.VoicePitch;
        character.VoiceEveryNthCharacter = dialog.VoiceEveryNthCharacter;
        if (character.Position != dialog.Position)
        {
            character.HasCustomTransform = false;
            character.Scale = 1;
            character.Rotation = 0;
        }
        character.Position = dialog.Position;
        var node = _project.FindNode(Graph.SelectedNodeId);
        if (node?.UsesTypeDefaults == true)
        {
            node.PropertyOverrides.Add("characters");
        }
        MarkDirty();
    }

    private List<string> NormalizeVoiceReferences(IEnumerable<string> references) =>
        references
            .Select(reference => NormalizeAssetPath(reference, "voices"))
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void CopyCharacterValues(
        CharacterPlacement source,
        CharacterPlacement target)
    {
        target.Name = source.Name;
        target.Sprite = source.Sprite;
        target.Position = source.Position;
        target.HasCustomTransform = source.HasCustomTransform;
        target.X = source.X;
        target.Y = source.Y;
        target.Scale = source.Scale;
        target.Rotation = source.Rotation;
        target.SetVoiceSounds(source.GetVoiceSounds());
        target.VoicePitch = source.VoicePitch;
        target.VoiceEveryNthCharacter = source.VoiceEveryNthCharacter;
    }

    private void DeleteCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var character = SelectedCharacter();
        if (node is null || character is null)
        {
            return;
        }
        node.Characters.Remove(character);
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("characters");
        }
        MarkDirty();
    }

    private void DuplicateCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var character = SelectedCharacter();
        if (node is null || character is null)
        {
            return;
        }

        var duplicate = _project.DuplicateCharacter(node.Id, character.Id);
        MarkDirty();
        SelectCharacterView(duplicate.Id);
        StatusText.Text = $"Персонаж «{CharacterLabel(character)}» продублирован";
    }

    private void MoveCharacterLeft_Click(object sender, RoutedEventArgs e) =>
        SetSelectedCharacterPosition(CharacterPosition.Left);

    private void MoveCharacterCenter_Click(object sender, RoutedEventArgs e) =>
        SetSelectedCharacterPosition(CharacterPosition.Center);

    private void MoveCharacterRight_Click(object sender, RoutedEventArgs e) =>
        SetSelectedCharacterPosition(CharacterPosition.Right);

    private void MoveCharacterUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedCharacter(-1);

    private void MoveCharacterDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedCharacter(1);

    private void MoveSelectedCharacter(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        var node = _project.FindNode(Graph.SelectedNodeId);
        var character = SelectedCharacter();
        if (node is null || character is null)
        {
            return;
        }

        if (!_project.MoveCharacter(node.Id, character.Id, direction))
        {
            return;
        }

        MarkDirty();
        SelectCharacterView(character.Id);
        StatusText.Text = $"Персонаж «{CharacterLabel(character)}» перемещён в списке";
    }

    private void SetSelectedCharacterPosition(CharacterPosition position)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var character = SelectedCharacter();
        if (node is null || character is null || character.Position == position)
        {
            return;
        }

        var characterId = character.Id;
        if (!_project.SetCharacterPosition(node.Id, character.Id, position))
        {
            return;
        }

        MarkDirty();
        SelectCharacterView(characterId);
        StatusText.Text = $"Персонаж «{CharacterLabel(character)}» перемещён: {CharacterPositionLabel(position)}";
    }

    private CharacterPlacement? SelectedCharacter()
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var view = CharactersGrid.SelectedItem as CharacterView;
        return node?.Characters.FirstOrDefault(character => character.Id == view?.Id);
    }

    private void SelectCharacterView(string characterId)
    {
        var characterView = CharactersGrid.Items
            .OfType<CharacterView>()
            .FirstOrDefault(view => view.Id == characterId);
        if (characterView is null)
        {
            return;
        }

        CharactersGrid.SelectedItem = characterView;
        CharactersGrid.ScrollIntoView(characterView);
    }

    private IReadOnlyList<NovelAsset> GetCharacterSpriteAssets() =>
        _project.Assets
            .Where(asset => asset.Kind == AssetKind.Image)
            .Where(asset => IsInAssetFolder(asset, "characters"))
            .ToList();

    private IReadOnlyList<NovelAsset> GetVoiceBlipAssets() =>
        _project.Assets
            .Where(asset => asset.Kind == AssetKind.Audio)
            .Where(asset => IsInAssetFolder(asset, "voices"))
            .ToList();

    private void CharactersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        SetCharacterButtons(
            node is not null
            && (node.Kind == NodeKind.Start || InheritCharactersCheck.IsChecked != true));
    }

    private void SetCharacterButtons(bool canEdit)
    {
        var hasSelectedCharacter = CharactersGrid.SelectedItem is CharacterView;
        var selectedIndex = CharactersGrid.SelectedIndex;
        var characterCount = CharactersGrid.Items.Count;

        CharactersGrid.IsEnabled = canEdit;
        AddCharacterButton.IsEnabled = canEdit;
        AddLibraryCharacterButton.IsEnabled = canEdit && _project.Characters.Count > 0;
        SaveCharacterToLibraryButton.IsEnabled =
            canEdit && hasSelectedCharacter;
        EditCharacterButton.IsEnabled = canEdit && hasSelectedCharacter;
        DuplicateCharacterButton.IsEnabled = canEdit && hasSelectedCharacter;
        MoveCharacterUpButton.IsEnabled = canEdit && hasSelectedCharacter && selectedIndex > 0;
        MoveCharacterDownButton.IsEnabled =
            canEdit && hasSelectedCharacter && selectedIndex >= 0 && selectedIndex < characterCount - 1;
        MoveCharacterLeftButton.IsEnabled = canEdit && hasSelectedCharacter;
        MoveCharacterCenterButton.IsEnabled = canEdit && hasSelectedCharacter;
        MoveCharacterRightButton.IsEnabled = canEdit && hasSelectedCharacter;
        DeleteCharacterButton.IsEnabled = canEdit && hasSelectedCharacter;
    }

    private void AddOutput_Click(object sender, RoutedEventArgs e) => AddOutput();

    private void AddOutput(string? nodeId = null)
    {
        var node = _project.FindNode(nodeId ?? Graph.SelectedNodeId);
        if (node?.Kind != NodeKind.Dialogue)
        {
            return;
        }

        var output = _project.AddChoice(node.Id);
        var dialog = new OutputEditorWindow(output) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            _project.RemoveOutput(node.Id, output.Id);
            return;
        }
        if (!ValidateOutputDialog(dialog))
        {
            _project.RemoveOutput(node.Id, output.Id);
            return;
        }
        dialog.ApplyTo(output);
        MarkDirty();
    }

    private void EditOutput_Click(object sender, RoutedEventArgs e)
    {
        var output = SelectedOutput();
        if (output is null)
        {
            return;
        }

        var dialog = new OutputEditorWindow(output) { Owner = this };
        if (dialog.ShowDialog() != true || !ValidateOutputDialog(dialog))
        {
            return;
        }
        dialog.ApplyTo(output);
        MarkDirty();
    }

    private bool ValidateOutputDialog(OutputEditorWindow dialog)
    {
        try
        {
            _ = NovelScript.Evaluate(dialog.Condition, new ScriptState());
            NovelScript.Execute(dialog.Script, new ScriptState());
            return true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Ошибка варианта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void DeleteOutput_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var output = SelectedOutput();
        if (node?.Kind == NodeKind.Dialogue
            && output is not null
            && _project.RemoveOutput(node.Id, output.Id))
        {
            MarkDirty();
        }
    }

    private void DuplicateOutput_Click(object sender, RoutedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var output = SelectedOutput();
        if (node?.Kind != NodeKind.Dialogue || output is null)
        {
            return;
        }

        NodeOutput duplicate;
        try
        {
            duplicate = _project.DuplicateOutput(node.Id, output.Id);
        }
        catch (InvalidOperationException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Дублирование варианта",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MarkDirty();
        SelectOutputView(duplicate.Id);
    }

    private void MoveOutputUp_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedOutput(-1);

    private void MoveOutputDown_Click(object sender, RoutedEventArgs e) =>
        MoveSelectedOutput(1);

    private void MoveSelectedOutput(int direction)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var output = SelectedOutput();
        if (node?.Kind != NodeKind.Dialogue || output is null)
        {
            return;
        }

        if (!_project.MoveOutput(node.Id, output.Id, direction))
        {
            return;
        }

        MarkDirty();
        SelectOutputView(output.Id);
    }

    private void DisconnectOutput_Click(object sender, RoutedEventArgs e)
    {
        var output = SelectedOutput();
        if (output?.TargetNodeId is null)
        {
            return;
        }
        output.TargetNodeId = null;
        MarkDirty();
    }

    private NodeOutput? SelectedOutput()
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var view = OutputsGrid.SelectedItem as OutputView;
        return node?.Outputs.FirstOrDefault(output => output.Id == view?.Id);
    }

    private void SelectOutputView(string outputId)
    {
        var outputView = OutputsGrid.Items
            .OfType<OutputView>()
            .FirstOrDefault(view => view.Id == outputId);
        if (outputView is null)
        {
            return;
        }

        OutputsGrid.SelectedItem = outputView;
        OutputsGrid.ScrollIntoView(outputView);
    }

    private void OutputsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        SetOutputButtons(
            node?.Kind == NodeKind.Dialogue,
            OutputsGrid.SelectedItem is OutputView);
    }

    private void SetOutputButtons(bool canAdd, bool selected)
    {
        var selectedIndex = OutputsGrid.SelectedIndex;
        var outputCount = OutputsGrid.Items.Count;

        AddOutputButton.IsEnabled = canAdd;
        EditOutputButton.IsEnabled = selected;
        DuplicateOutputButton.IsEnabled = canAdd && selected;
        MoveOutputUpButton.IsEnabled = canAdd && selected && selectedIndex > 0;
        MoveOutputDownButton.IsEnabled =
            canAdd && selected && selectedIndex >= 0 && selectedIndex < outputCount - 1;
        DeleteOutputButton.IsEnabled = canAdd && selected;
        DisconnectOutputButton.IsEnabled = selected;
    }

    private void EditTransition(string sourceNodeId, string outputId)
    {
        var output = _project.FindOutput(sourceNodeId, outputId);
        if (output is null)
        {
            return;
        }

        var dialog = new TransitionEditorWindow(output, GetAssetDirectory()) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        output.TransitionSound = NormalizeAssetPath(dialog.TransitionSound);
        output.FadeDurationMs = dialog.FadeDurationMs;
        MarkDirty();
    }

    private void ApplyProperties_Click(object sender, RoutedEventArgs e) => ApplyProperties();

    private async void BuildGame_Click(object sender, RoutedEventArgs e) =>
        await CompileGameAsync(debugSymbols: false, reportSuccess: true);

    private async void RunProject_Click(object sender, RoutedEventArgs e) =>
        await RunCompiledGameAsync(debugMode: false);

    private async void DebugGame_Click(object sender, RoutedEventArgs e) =>
        await RunCompiledGameAsync(debugMode: true);

    private void StopGame_Click(object sender, RoutedEventArgs e) =>
        StopGameProcess();

    private void PreviewNode_Click(object sender, RoutedEventArgs e) =>
        PreviewNode(Graph.SelectedNodeId);

    private async Task RunCompiledGameAsync(bool debugMode)
    {
        if (IsGameRunning())
        {
            StatusText.Text = "Игра уже запущена";
            return;
        }
        var build = await CompileGameAsync(
            debugSymbols: debugMode,
            reportSuccess: false);
        if (build is null)
        {
            return;
        }
        try
        {
            StartGameProcess(build, debugMode);
        }
        catch (Exception error) when (
            error is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Не удалось запустить игру";
            MessageBox.Show(
                this,
                error.Message,
                "Запуск игры",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task<NovelBuildResult?> CompileGameAsync(
        bool debugSymbols,
        bool reportSuccess)
    {
        if (_buildInProgress || IsGameRunning())
        {
            StatusText.Text = _buildInProgress
                ? "Компиляция уже выполняется"
                : "Остановите игру перед новой компиляцией";
            return null;
        }
        if (!EnsureCodeApplied() || !ApplyProperties() || !SaveProject())
        {
            return null;
        }
        if (!ConfirmProjectReadyForBuild())
        {
            return null;
        }

        _buildInProgress = true;
        UpdateGameControls();
        StatusText.Text = debugSymbols
            ? "Компиляция debug build..."
            : "Компиляция игры...";
        try
        {
            var projectPath = _projectPath!;
            var projectSnapshot = ProjectSerializer.FromJson(
                ProjectSerializer.ToJson(_project));
            var outputDirectory = GetBuildDirectory(projectPath);
            var result = await Task.Run(
                () => NovelBuildCompiler.Compile(
                    projectSnapshot,
                    projectPath,
                    outputDirectory,
                    debugSymbols));
            StatusText.Text =
                $"Build {result.Manifest.BuildId}: "
                + $"{result.Manifest.NodeCount} нод, "
                + $"{result.Manifest.AssetCount} ассетов";
            if (reportSuccess)
            {
                MessageBox.Show(
                    this,
                    $"Игра скомпилирована.\n\n{result.OutputDirectory}",
                    "Сборка завершена",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            return result;
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            StatusText.Text = "Ошибка компиляции";
            MessageBox.Show(
                this,
                error.Message,
                "Компиляция игры",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return null;
        }
        finally
        {
            _buildInProgress = false;
            UpdateGameControls();
        }
    }

    private void StartGameProcess(NovelBuildResult build, bool debugMode)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException(
                "Не удалось определить исполняемый файл Novel Engine.");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = build.OutputDirectory,
            UseShellExecute = false,
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals(
            "dotnet",
            StringComparison.OrdinalIgnoreCase))
        {
            var entryAssembly = System.Reflection.Assembly
                .GetEntryAssembly()?.Location
                ?? throw new InvalidOperationException(
                    "Не удалось определить сборку player.");
            startInfo.ArgumentList.Add(entryAssembly);
        }
        startInfo.ArgumentList.Add("--play-build");
        startInfo.ArgumentList.Add(build.ManifestPath);
        if (debugMode)
        {
            startInfo.ArgumentList.Add("--debug");
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        process.Exited += GameProcess_Exited;
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Не удалось запустить процесс игры.");
        }
        _gameProcess = process;
        UpdateGameControls();
        StatusText.Text = debugMode
            ? $"Debug запущен · PID {process.Id}"
            : $"Игра запущена · PID {process.Id}";
    }

    private void GameProcess_Exited(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                if (sender is not Process process
                    || !ReferenceEquals(process, _gameProcess))
                {
                    return;
                }
                var exitCode = process.ExitCode;
                process.Dispose();
                _gameProcess = null;
                UpdateGameControls();
                StatusText.Text = exitCode == 0
                    ? "Игра остановлена"
                    : $"Процесс игры завершился с кодом {exitCode}";
            });
    }

    private void StopGameProcess(bool silent = false)
    {
        if (!IsGameRunning())
        {
            if (!silent)
            {
                StatusText.Text = "Игра не запущена";
            }
            return;
        }
        try
        {
            _gameProcess!.Kill(entireProcessTree: true);
            if (!silent)
            {
                StatusText.Text = "Остановка игры...";
            }
        }
        catch (InvalidOperationException)
        {
            _gameProcess?.Dispose();
            _gameProcess = null;
            UpdateGameControls();
        }
    }

    private bool IsGameRunning() =>
        _gameProcess is { HasExited: false };

    private void UpdateGameControls()
    {
        var running = IsGameRunning();
        BuildGameButton.IsEnabled = !_buildInProgress && !running;
        RunGameButton.IsEnabled = !_buildInProgress && !running;
        DebugGameButton.IsEnabled = !_buildInProgress && !running;
        StopGameButton.IsEnabled = running;
    }

    private static string GetBuildDirectory(string projectPath)
    {
        var fileName = Path.GetFileName(projectPath);
        var buildName = fileName.EndsWith(
            ".novel.json",
            StringComparison.OrdinalIgnoreCase)
                ? fileName[..^".novel.json".Length]
                : Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(projectPath))!,
            "build",
            buildName);
    }

    private void PreviewNode(string? nodeId)
    {
        if (nodeId is null)
        {
            MessageBox.Show(
                this,
                "Сначала выберите ноду.",
                "Предпросмотр",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }
        if (!EnsureCodeApplied())
        {
            return;
        }
        if (!ApplyProperties())
        {
            return;
        }
        new PreviewWindow(_project, nodeId, GetAssetDirectory()) { Owner = this }.ShowDialog();
    }

    private void EditNodeScene(string nodeId)
    {
        if (!EnsureCodeApplied() || !ApplyProperties())
        {
            return;
        }
        var node = _project.FindNode(nodeId);
        if (node is null)
        {
            return;
        }

        var editor = new SceneEditorWindow(_project, node, GetAssetDirectory())
        {
            Owner = this,
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        node.InheritCharacters = false;
        node.Characters.Clear();
        node.Characters.AddRange(
            editor.Characters.Select(character => character.Clone()));
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("inheritCharacters");
            node.PropertyOverrides.Add("characters");
        }
        MarkDirty();
        StatusText.Text = $"Сцена «{node.Title}» обновлена";
    }

    private string? GetSaveDialogInitialDirectory() =>
        _workspaceDirectory is not null && Directory.Exists(_workspaceDirectory)
            ? _workspaceDirectory
            : _projectPath is null
                ? null
                : Path.GetDirectoryName(_projectPath);

    private string? ChooseProjectFolder(string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        var initialDirectory = GetSaveDialogInitialDirectory();
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private string GetDefaultProjectFileName()
    {
        var source = _workspaceDirectory is null
            ? _project.Title
            : Path.GetFileName(_workspaceDirectory);
        var safeName = string.Concat(
            source.Select(character =>
                Path.GetInvalidFileNameChars().Contains(character)
                    ? '_'
                    : character)).Trim();
        if (safeName.Length == 0)
        {
            safeName = "NovelProject";
        }
        return $"{safeName}.novel.json";
    }

    private static string? NormalizeWorkspaceDirectory(string? directory) =>
        string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.GetFullPath(directory);

    private void EditMainMenu()
    {
        if (!EnsureCodeApplied() || !ApplyProperties())
        {
            return;
        }
        var editor = new MainMenuEditorWindow(
            _project,
            _project.MainMenu,
            GetAssetDirectory())
        {
            Owner = this,
        };
        if (editor.ShowDialog() != true)
        {
            return;
        }

        _project.MainMenu.Background = editor.Design.Background;
        _project.MainMenu.Elements.Clear();
        _project.MainMenu.Elements.AddRange(
            editor.Design.Elements.Select(element => element.Clone()));
        MarkDirty();
        StatusText.Text = "Главное меню обновлено";
    }

    private string GetAssetDirectory() =>
        _projectPath is null
            ? _workspaceDirectory ?? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(_projectPath))
                ?? Environment.CurrentDirectory;

    private void AutoSaveProject()
    {
        if (_projectPath is null)
        {
            if (_workspaceDirectory is null || !SaveProjectToWorkspace())
            {
                return;
            }
        }
        else
        {
            SyncFilesFromDisk(refreshCode: false);
        }
        var projectPath = _projectPath;
        if (projectPath is null)
        {
            return;
        }

        try
        {
            PreservePendingSourceCode();
            ProjectSerializer.Save(_project, projectPath);
            WriteAutoSaveSnapshot();
            _savedProjectSnapshot = CaptureProjectSnapshot();
            _dirty = false;
            _workspaceNeedsProjectFile = false;
            RefreshWindowTitle();
            StatusText.Text = $"Автосохранено {DateTime.Now:HH:mm}";
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            StatusText.Text = $"Автосохранение не удалось: {error.Message}";
        }
    }

    private void PreservePendingSourceCode()
    {
        if (_codeHasPendingChanges)
        {
            _project.SourceCode = CodeEditor.SourceText;
        }
    }

    private void WriteAutoSaveSnapshot()
    {
        var directory = GetAutoSaveDirectory();
        if (directory is null)
        {
            return;
        }
        Directory.CreateDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(
            _projectPath ?? GetDefaultProjectFileName());
        var path = AutoSaveStore.CreateSnapshotPath(
            directory,
            stem,
            DateTime.UtcNow);
        ProjectSerializer.Save(_project, path);
        AutoSaveStore.Prune(directory, stem, keepCount: 24);
    }

    private string? GetAutoSaveDirectory()
    {
        var workspace = _workspaceDirectory
            ?? (_projectPath is null
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(_projectPath)));
        return workspace is null
            ? null
            : Path.Combine(workspace, "autosaves");
    }

    private int EnsureWorkspaceStructure()
    {
        if (_workspaceDirectory is null)
        {
            return 0;
        }

        Directory.CreateDirectory(_workspaceDirectory);
        var changes = _projectPath is null
            ? EnsureDefaultWorkspaceFoldersWithoutProjectFile()
            : ProjectAssets.EnsureDefaultStructure(_project, _projectPath);
        Directory.CreateDirectory(Path.Combine(_workspaceDirectory, "autosaves"));
        return changes;
    }

    private int EnsureDefaultWorkspaceFoldersWithoutProjectFile()
    {
        var changes = ProjectAssets.EnsureDefaultFolders(_project);
        var root = Path.Combine(
            _workspaceDirectory!,
            ProjectAssets.ManagedFilesDirectoryName);
        Directory.CreateDirectory(root);
        foreach (var folder in ProjectAssets.DefaultProjectFolders)
        {
            Directory.CreateDirectory(
                Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar)));
        }
        return changes;
    }

    private string NormalizeAssetPath(string path, string? folder = null)
    {
        if (path.Length == 0 || !Path.IsPathRooted(path))
        {
            return path;
        }
        if (!EnsureProjectSavedForAssets())
        {
            return path;
        }
        try
        {
            return ImportAssetFile(path, folder);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Импорт ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return path;
        }
    }

    private void ValidateProject_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureCodeApplied())
        {
            return;
        }
        RefreshProjectDiagnostics(showPanel: true);
    }

    private bool ConfirmProjectReadyForBuild()
    {
        var report = RefreshProjectDiagnostics(showPanel: true);
        if (report.HasErrors)
        {
            StatusText.Text = "Сборка остановлена: есть ошибки проекта";
            MessageBox.Show(
                this,
                "В проекте есть ошибки. Исправьте их перед сборкой или запуском.",
                "Проверка проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }

        if (report.WarningCount == 0)
        {
            return true;
        }

        var result = MessageBox.Show(
            this,
            $"В проекте есть предупреждения: {report.WarningCount}.\n\n"
            + "Продолжить сборку?",
            "Проверка проекта",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            return true;
        }

        StatusText.Text = "Сборка отменена после проверки проекта";
        return false;
    }

    private void RequestDiagnosticsRefresh()
    {
        _diagnosticsTimer.Stop();
        _diagnosticsTimer.Start();
    }

    private ProjectDiagnosticReport RefreshProjectDiagnostics(bool showPanel)
    {
        var report = ProjectDiagnostics.Analyze(_project, _projectPath);
        var diagnostics = report.Diagnostics
            .Select(diagnostic => new DiagnosticView(diagnostic))
            .ToList();
        DiagnosticsGrid.ItemsSource = diagnostics;
        DiagnosticsSummaryText.Text =
            $"Диагностика: {report.ErrorCount} ошибок, "
            + $"{report.WarningCount} предупреждений, "
            + $"{report.InfoCount} заметок";
        DiagnosticsSummaryText.Foreground = report.HasErrors
            ? Brushes.IndianRed
            : report.WarningCount > 0
                ? Brushes.Khaki
                : (Brush)FindResource("MutedBrush");

        if (diagnostics.Count == 0)
        {
            DiagnosticsPanel.Visibility = Visibility.Collapsed;
            if (showPanel)
            {
                StatusText.Text = "Проверка проекта: проблем не найдено";
            }
            return report;
        }

        var shouldShowPanel = report.HasErrors || showPanel;
        DiagnosticsPanel.Visibility = shouldShowPanel
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (shouldShowPanel)
        {
            StatusText.Text = report.HasErrors
                ? $"Проверка проекта: ошибок {report.ErrorCount}"
                : report.WarningCount > 0
                    ? $"Проверка проекта: предупреждений {report.WarningCount}"
                    : $"Проверка проекта: заметок {report.InfoCount}";
        }
        return report;
    }

    private void HideDiagnostics_Click(object sender, RoutedEventArgs e) =>
        DiagnosticsPanel.Visibility = Visibility.Collapsed;

    private void DiagnosticsGrid_MouseDoubleClick(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DiagnosticsGrid.SelectedItem is not DiagnosticView view)
        {
            return;
        }

        NavigateToDiagnostic(view.Diagnostic);
    }

    private void NavigateToDiagnostic(ProjectDiagnostic diagnostic)
    {
        if (TryNavigateToDiagnosticAsset(diagnostic.Location))
        {
            return;
        }
        if (TryNavigateToDiagnosticNode(diagnostic.Location))
        {
            return;
        }

        StatusText.Text = $"Нет быстрой навигации для «{diagnostic.Location}»";
    }

    private bool TryNavigateToDiagnosticAsset(string location)
    {
        var assetId = TryReadDiagnosticAssetId(location);
        if (assetId is null)
        {
            return false;
        }

        WorkspaceTabs.SelectedItem = FilesTab;
        RefreshAssets();
        var assetView = AssetsGrid.Items
            .OfType<AssetView>()
            .FirstOrDefault(view => view.Id.Equals(
                assetId,
                StringComparison.OrdinalIgnoreCase));
        if (assetView is null)
        {
            StatusText.Text = $"Ассет @{assetId} не найден в менеджере файлов";
            return true;
        }

        AssetsGrid.SelectedItem = assetView;
        AssetsGrid.ScrollIntoView(assetView);
        RefreshAssetPreview();
        StatusText.Text = $"Открыт ассет @{assetId}";
        return true;
    }

    private bool TryNavigateToDiagnosticNode(string location)
    {
        var node = _project.Nodes
            .OrderByDescending(node => DiagnosticNodeDisplay(node).Length)
            .FirstOrDefault(node => LocationReferencesNode(location, node));
        if (node is null)
        {
            return false;
        }

        Graph.SelectNode(node.Id);
        if (location.Contains("скрипт", StringComparison.OrdinalIgnoreCase)
            || location.Contains("условие", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToNodeCode(node.Id);
        }
        else
        {
            WorkspaceTabs.SelectedItem = GraphTab;
            StatusText.Text = $"Выбрана нода «{DiagnosticNodeDisplay(node)}»";
        }
        return true;
    }

    private static string? TryReadDiagnosticAssetId(string location)
    {
        const string marker = "Ассет @";
        var start = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = start;
        while (end < location.Length
            && (char.IsLetterOrDigit(location[end])
                || location[end] is '_' or '-'))
        {
            end++;
        }

        var assetId = location[start..end];
        return AssetReference.IsValidId(assetId) ? assetId : null;
    }

    private static bool LocationReferencesNode(string location, NovelNode node)
    {
        var display = DiagnosticNodeDisplay(node);
        return location.Contains($"Нода «{display}»", StringComparison.Ordinal)
            || location.Equals($"Нода {node.Id}", StringComparison.Ordinal)
            || location.StartsWith($"Нода {node.Id},", StringComparison.Ordinal)
            || location.StartsWith($"Нода {node.Id} ", StringComparison.Ordinal);
    }

    private static string DiagnosticNodeDisplay(NovelNode node) =>
        string.IsNullOrWhiteSpace(node.Title) ? node.Id : node.Title;

    private static string KindName(NodeKind kind) =>
        kind switch
        {
            NodeKind.Start => "Старт",
            NodeKind.Scene => "Сцена",
            NodeKind.Dialogue => "Диалог",
            _ => kind.ToString(),
        };

    private static string CharacterPositionLabel(CharacterPosition position) =>
        position switch
        {
            CharacterPosition.Left => "Слева",
            CharacterPosition.Right => "Справа",
            _ => "По центру",
        };

    private sealed record CharacterView(CharacterPlacement Character)
    {
        public string Id => Character.Id;
        public string Name => Character.Name;
        public string Position => CharacterPositionLabel(Character.Position);
        public int VoiceCount => CharacterVoiceReferences(Character).Count;
    }

    private sealed record OutputView(NodeOutput Output, string TargetTitle)
    {
        public string Id => Output.Id;
        public string Label => Output.Label;
        public string Condition => Output.Condition.Length == 0 ? "всегда" : Output.Condition;
    }

    private sealed record AssetView(
        NovelAsset Asset,
        int UsageCount,
        string Size)
    {
        public string Id => Asset.Id;
        public string Kind => Asset.Kind switch
        {
            AssetKind.Image => "Изображение",
            AssetKind.Audio => "Аудио",
            _ => "Файл",
        };
        public string IconGlyph => Asset.Kind switch
        {
            AssetKind.Image => "\uEB9F",
            AssetKind.Audio => "\uE8D6",
            _ => "\uE8A5",
        };
        public string Folder => Asset.Folder;
        public string Path => Asset.Path;
    }

    private sealed record NodeAssetFolderOption(string Folder, string Name);

    private sealed record NodeAssetChoice(NovelAsset? Asset, string Name);

    private sealed record ProjectHistoryEntry(string Snapshot, string? SelectedNodeId);

    private sealed record DiagnosticView(ProjectDiagnostic Diagnostic)
    {
        public string Severity => Diagnostic.Severity switch
        {
            ProjectDiagnosticSeverity.Error => "Ошибка",
            ProjectDiagnosticSeverity.Warning => "Предупреждение",
            _ => "Заметка",
        };
        public string Location => Diagnostic.Location;
        public string Message => Diagnostic.Message;
    }
}
