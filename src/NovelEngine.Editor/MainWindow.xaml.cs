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

internal enum NodeVoiceBindingResult
{
    Unavailable,
    Unchanged,
    Changed,
}

internal sealed record AssetSizeCacheEntry(
    long Bytes,
    DateTime LastWriteTimeUtc,
    string Label);

public partial class MainWindow : Window
{
    private const int MaxProjectHistoryEntries = 100;
    private const int MaxCachedAssetPreviewImages = 48;

    private static readonly IReadOnlyDictionary<string, string> EmptyNodeTitleLookup =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

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
    private readonly DispatcherTimer _projectExplorerSearchTimer;
    private readonly DispatcherTimer _assetSearchTimer;
    private string _codeCursorSource = string.Empty;
    private int[] _codeLineStarts = [0];
    private int _lastCodeCursorOffset = -1;
    private bool _codeCursorCacheDirty = true;
    private string? _codeSyntaxSource;
    private IReadOnlyList<ProjectLanguageSyntaxSpan> _codeSyntaxSpans = [];
    private bool _codeSyntaxSpanLimitExceeded;
    private string? _parsedCodeSource;
    private NovelProject? _parsedCodeProject;
    private bool _codeRefreshPending = true;
    private bool _codeRefreshUseStoredSource = true;
    private bool _syncingFilesFromDisk;
    private readonly DispatcherDebounceGate _filesRefreshDispatchGate = new();
    private readonly List<ProjectHistoryEntry> _projectHistory = [];
    private int _projectHistoryIndex = -1;
    private bool _restoringProjectHistory;
    private string _savedProjectSnapshot = string.Empty;
    private string? _assetPreviewAudioPath;
    private readonly Dictionary<string, AssetSizeCacheEntry> _assetSizeCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly BoundedCache<string, BitmapImage?> _assetPreviewImageCache =
        new(MaxCachedAssetPreviewImages, StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, int>? _assetUsageCountCache;
    private AssetListStamp? _assetListStamp;
    private AssetFolderTreeStamp? _assetFolderTreeStamp;
    private AssetPreviewStamp? _assetPreviewStamp;
    private int _assetCatalogRevision;
    private readonly Dictionary<AssetKind, List<NodeAssetFolderOption>>
        _nodeAssetFolderOptionsCache = [];
    private readonly Dictionary<NodeAssetChoiceCacheKey, List<NodeAssetChoice>>
        _nodeAssetChoicesCache = [];
    private readonly Dictionary<string, NovelAsset> _nodeAssetByIdCache =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _nodeAssetPickerCachesDirty = true;
    private ProjectDiagnosticReport? _projectDiagnosticsCache;
    private DiagnosticPanelStamp? _diagnosticsPanelStamp;
    private readonly Dictionary<string, TreeViewItem> _projectTreeItemsByNodeId =
        new(StringComparer.OrdinalIgnoreCase);
    private TreeViewItem? _selectedProjectTreeItem;
    private ProjectExplorerStamp? _projectExplorerStamp;
    private NodePropertyPanelStamp? _nodePropertyPanelStamp;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? startupProjectPath)
    {
        InitializeComponent();
        CodeEditor.CompletionProvider = ProjectLanguage.GetCompletions;
        AssetsGrid.ContextMenu = new ContextMenu();
        CharactersGrid.ContextMenu = new ContextMenu();
        OutputsGrid.ContextMenu = new ContextMenu();

        Graph.SelectionChanged += (_, _) => HandleGraphSelection();
        Graph.ProjectChanged += (_, _) => MarkDirty(
            refreshGraph: ShouldRefreshGraphForDirtyChange(originatedFromGraphSurface: true));
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
            if (ReferenceEquals(WorkspaceTabs.SelectedItem, FilesTab))
            {
                RefreshAssets(externalFileEvent: true);
            }
            else
            {
                var filesChanged = SyncFilesFromDisk(refreshCode: false);
                if (ShouldClearAssetFileCaches(
                        externalFileEvent: true,
                        diskSyncChangedProject: filesChanged))
                {
                    ClearAssetFileCaches();
                }
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
        _projectExplorerSearchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _projectExplorerSearchTimer.Tick += (_, _) =>
        {
            _projectExplorerSearchTimer.Stop();
            RefreshExplorer();
        };
        _assetSearchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _assetSearchTimer.Tick += (_, _) =>
        {
            _assetSearchTimer.Stop();
            RefreshAssetList();
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

    internal NovelProject ProjectForSmoke => _project;

    internal bool SaveProjectForSmoke() => SaveProject();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var modifiers = Keyboard.Modifiers;
        var isTextEditing = IsTextEditing();
        var isGraphShortcutContext = IsGraphShortcutContext();
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
        else if (IsGraphDeleteShortcut(
                e.Key,
                modifiers,
                isTextEditing,
                isGraphShortcutContext))
        {
            Graph.DeleteSelected();
            e.Handled = true;
        }
        else if (IsGraphDuplicateShortcut(
            e.Key,
            modifiers,
            isTextEditing,
            isGraphShortcutContext))
        {
            Graph.DuplicateSelected();
            e.Handled = true;
        }
        else if (IsGraphCenterShortcut(
            e.Key,
            modifiers,
            isTextEditing,
            isGraphShortcutContext))
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

    private bool IsGraphShortcutContext() =>
        Keyboard.FocusedElement is DependencyObject focused
        && IsDescendantOf(focused, Graph);

    private static bool IsDescendantOf(
        DependencyObject source,
        DependencyObject target)
    {
        var current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, target))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current)
                ?? LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    internal static bool IsGraphDeleteShortcut(
        Key key,
        ModifierKeys modifiers,
        bool isTextEditing,
        bool isGraphShortcutContext) =>
        key == Key.Delete
        && modifiers == ModifierKeys.None
        && !isTextEditing
        && isGraphShortcutContext;

    internal static bool IsGraphDuplicateShortcut(
        Key key,
        ModifierKeys modifiers,
        bool isTextEditing,
        bool isGraphShortcutContext) =>
        key == Key.D
        && modifiers == ModifierKeys.Control
        && !isTextEditing
        && isGraphShortcutContext;

    internal static bool IsGraphCenterShortcut(
        Key key,
        ModifierKeys modifiers,
        bool isTextEditing,
        bool isGraphShortcutContext) =>
        key == Key.Home
        && modifiers == ModifierKeys.None
        && !isTextEditing
        && isGraphShortcutContext;

    private void SetProject(
        NovelProject project,
        string? path,
        string? workspaceDirectory = null,
        bool saveStructureChanges = true)
    {
        _project = project;
        ClearNodeAssetPickerCaches();
        _projectPath = path;
        _workspaceDirectory = NormalizeWorkspaceDirectory(
            workspaceDirectory
            ?? (_projectPath is null
                ? _workspaceDirectory
                : Path.GetDirectoryName(_projectPath)));
        _workspaceNeedsProjectFile = path is null && workspaceDirectory is not null;
        _dirty = false;
        _selectedAssetFolder = null;
        _assetFolderTreeStamp = null;
        _assetPreviewStamp = null;
        ClearProjectAnalysisCaches();
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
        RefreshExplorerSelection();
        RefreshProperties();
    }

    private void RefreshExplorerSelection()
    {
        if (ProjectTree.Items.Count == 0)
        {
            RefreshExplorer();
            return;
        }

        _syncingSelection = true;
        try
        {
            SyncProjectTreeSelection(Graph.SelectedNodeId);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void RefreshExplorer()
    {
        _syncingSelection = true;
        try
        {
            var query = ProjectSearchBox.Text.Trim();
            var stamp = CreateProjectExplorerStamp(_project, query);
            if (_projectExplorerStamp == stamp && ProjectTree.Items.Count > 0)
            {
                SyncProjectTreeSelection(Graph.SelectedNodeId);
                return;
            }

            _projectExplorerStamp = stamp;
            _projectTreeItemsByNodeId.Clear();
            _selectedProjectTreeItem = null;

            var groups = BuildProjectExplorerGroups(_project.Nodes, query);
            ProjectTree.Items.Clear();
            var root = new TreeViewItem
            {
                Header = query.Length == 0
                    ? _project.Title
                    : $"{_project.Title} · найдено {groups.TotalCount}",
                IsExpanded = true,
            };
            ProjectTree.Items.Add(root);
            AddNodeGroup(root, "Старт", query, groups.StartNodes);
            AddNodeGroup(root, "Сцены", query, groups.SceneNodes);
            AddNodeGroup(root, "Диалоги", query, groups.DialogueNodes);
            SyncProjectTreeSelection(Graph.SelectedNodeId);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void AddNodeGroup(
        TreeViewItem root,
        string title,
        string query,
        IReadOnlyList<NovelNode> nodes)
    {
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
            };
            _projectTreeItemsByNodeId[node.Id] = item;
            group.Items.Add(item);
        }
    }

    internal static ProjectExplorerNodeGroups BuildProjectExplorerGroups(
        IEnumerable<NovelNode> nodes,
        string query)
    {
        var startNodes = new List<NovelNode>();
        var sceneNodes = new List<NovelNode>();
        var dialogueNodes = new List<NovelNode>();

        foreach (var node in nodes)
        {
            if (!NodeMatchesSearch(node, query))
            {
                continue;
            }

            switch (node.Kind)
            {
                case NodeKind.Start:
                    startNodes.Add(node);
                    break;
                case NodeKind.Scene:
                    sceneNodes.Add(node);
                    break;
                case NodeKind.Dialogue:
                    dialogueNodes.Add(node);
                    break;
            }
        }

        return new ProjectExplorerNodeGroups(
            startNodes,
            sceneNodes,
            dialogueNodes);
    }

    private void SyncProjectTreeSelection(string? nodeId)
    {
        var selectedItemNodeId = _selectedProjectTreeItem?.Tag as string;
        if (!ShouldSyncProjectTreeSelection(
                nodeId,
                selectedItemNodeId,
                _selectedProjectTreeItem?.IsSelected == true))
        {
            return;
        }

        if (_selectedProjectTreeItem is not null)
        {
            _selectedProjectTreeItem.IsSelected = false;
            _selectedProjectTreeItem = null;
        }

        if (nodeId is null
            || !_projectTreeItemsByNodeId.TryGetValue(nodeId, out var item))
        {
            return;
        }

        item.IsSelected = true;
        _selectedProjectTreeItem = item;
    }

    internal static bool ShouldSyncProjectTreeSelection(
        string? requestedNodeId,
        string? selectedItemNodeId,
        bool selectedItemIsSelected)
    {
        if (requestedNodeId is null)
        {
            return selectedItemNodeId is not null;
        }

        return !selectedItemIsSelected
            || !requestedNodeId.Equals(selectedItemNodeId, StringComparison.Ordinal);
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

    internal static ProjectExplorerStamp CreateProjectExplorerStamp(
        NovelProject project,
        string query)
    {
        var normalizedQuery = query.Trim();
        var isSearching = normalizedQuery.Length > 0;
        var hash = new HashCode();
        hash.Add(project.Title, StringComparer.Ordinal);
        foreach (var node in project.Nodes)
        {
            hash.Add(node.Id, StringComparer.Ordinal);
            hash.Add(node.Kind);
            hash.Add(node.Title, StringComparer.Ordinal);
            if (isSearching)
            {
                hash.Add(node.Speaker, StringComparer.Ordinal);
                hash.Add(node.Text, StringComparer.Ordinal);
            }
        }

        return new ProjectExplorerStamp(
            normalizedQuery,
            project.Nodes.Count,
            hash.ToHashCode());
    }

    private void RefreshProperties()
    {
        var node = Graph.SelectedNode;
        var nodeTitlesById = BuildOutputTargetTitleLookup(_project, node);
        var stamp = CreateNodePropertyPanelStamp(_project, node, nodeTitlesById);
        if (_nodePropertyPanelStamp == stamp)
        {
            return;
        }

        _nodePropertyPanelStamp = stamp;
        _refreshingProperties = true;
        try
        {
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
                EditNodeScriptBlocksButton.Content = "Блоки скрипта (0)";
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
            EditNodeScriptBlocksButton.Content =
                $"Блоки скрипта ({node.ScriptBlocks.Count})";
            CharactersGrid.ItemsSource = node.Characters
                .Select(character => new CharacterView(character))
                .ToList();
            OutputsGrid.ItemsSource = node.Outputs
                .Select(output => new OutputView(
                    output,
                    ResolveNodeTitle(
                        nodeTitlesById,
                        output.TargetNodeId,
                        "не подключено")))
                .ToList();

            SetCharacterButtons(CanEditNodeCharacters(node, node.InheritCharacters));
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
        yield return EditNodeScriptBlocksButton;
        yield return OutputsGrid;
    }

    private bool ApplyProperties()
    {
        var node = Graph.SelectedNode;
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
            VisualScriptCompiler.Execute(
                ScriptBox.Text,
                node.ScriptBlocks,
                new ScriptState());
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
        if (!HasNodePropertyChanges(
                node,
                title,
                speaker,
                text,
                inheritBackground,
                background,
                inheritMusic,
                music,
                inheritCharacters,
                script))
        {
            return true;
        }

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

        var refreshGraph = HasRenderedNodePropertyChanges(
            node,
            title,
            speaker,
            text);

        node.Title = title;
        node.Speaker = speaker;
        node.Text = text;
        node.InheritBackground = inheritBackground;
        node.Background = background;
        node.InheritMusic = inheritMusic;
        node.Music = music;
        node.InheritCharacters = inheritCharacters;
        node.Script = script;
        MarkDirty(refreshGraph);
        return true;
    }

    private void EditNodeScriptBlocks_Click(object sender, RoutedEventArgs e)
    {
        if (!ApplyProperties())
        {
            return;
        }

        var node = Graph.SelectedNode;
        if (node is null)
        {
            return;
        }

        EditNodeScriptBlocks(node);
    }

    private void EditNodeScriptBlocks(NovelNode node)
    {
        var dialog = new VisualScriptBlocksWindow(
            node.ScriptBlocks,
            "Блоки скрипта при входе",
            ProjectScriptVariables.Collect(_project),
            node.Script)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!HasScriptBlockDialogChanges(
                node.ScriptBlocks,
                dialog.Blocks,
                dialog.ClearImportedScript,
                node.Script))
        {
            return;
        }

        node.ScriptBlocks.Clear();
        node.ScriptBlocks.AddRange(dialog.Blocks.Select(block => block.Clone()));
        if (dialog.ClearImportedScript)
        {
            node.Script = string.Empty;
            if (node.UsesTypeDefaults)
            {
                node.PropertyOverrides.Add("script");
            }
        }
        MarkDirty(refreshGraph: false);
        StatusText.Text =
            dialog.ClearImportedScript
                ? $"Блоки скрипта ноды «{node.Title}»: {node.ScriptBlocks.Count}, текстовый скрипт очищен"
                : $"Блоки скрипта ноды «{node.Title}»: {node.ScriptBlocks.Count}";
    }

    private void EditOutputScriptBlocks(NovelNode node, NodeOutput output)
    {
        var dialog = new VisualScriptBlocksWindow(
            output.ScriptBlocks,
            $"Блоки скрипта выхода «{output.Label}»",
            ProjectScriptVariables.Collect(_project),
            output.Script)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (!HasScriptBlockDialogChanges(
                output.ScriptBlocks,
                dialog.Blocks,
                dialog.ClearImportedScript,
                output.Script))
        {
            return;
        }

        output.ScriptBlocks.Clear();
        output.ScriptBlocks.AddRange(
            dialog.Blocks.Select(block => block.Clone()));
        if (dialog.ClearImportedScript)
        {
            output.Script = string.Empty;
        }
        MarkDirty(refreshGraph: false);
        StatusText.Text =
            dialog.ClearImportedScript
                ? $"Блоки скрипта выхода «{output.Label}» в ноде «{node.Title}»: {output.ScriptBlocks.Count}, текстовый скрипт очищен"
                : $"Блоки скрипта выхода «{output.Label}» в ноде «{node.Title}»: {output.ScriptBlocks.Count}";
    }

    internal static bool HasScriptBlockDialogChanges(
        IReadOnlyList<VisualScriptBlock> currentBlocks,
        IReadOnlyList<VisualScriptBlock> dialogBlocks,
        bool clearImportedScript,
        string textScript) =>
        !VisualScriptBlocksEqual(currentBlocks, dialogBlocks)
        || (clearImportedScript && textScript.Length > 0);

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

    internal static bool HasNodePropertyChanges(
        NovelNode node,
        string title,
        string speaker,
        string text,
        bool inheritBackground,
        string background,
        bool inheritMusic,
        string music,
        bool inheritCharacters,
        string script) =>
        !node.Title.Equals(title, StringComparison.Ordinal)
            || !node.Speaker.Equals(speaker, StringComparison.Ordinal)
            || !node.Text.Equals(text, StringComparison.Ordinal)
            || node.InheritBackground != inheritBackground
            || !node.Background.Equals(background, StringComparison.Ordinal)
            || node.InheritMusic != inheritMusic
            || !node.Music.Equals(music, StringComparison.Ordinal)
            || node.InheritCharacters != inheritCharacters
            || !node.Script.Equals(script, StringComparison.Ordinal);

    internal static bool HasRenderedNodePropertyChanges(
        NovelNode node,
        string title,
        string speaker,
        string text) =>
        !node.Title.Equals(title, StringComparison.Ordinal)
            || !node.Speaker.Equals(speaker, StringComparison.Ordinal)
            || !node.Text.Equals(text, StringComparison.Ordinal);

    internal static NodePropertyPanelStamp CreateNodePropertyPanelStamp(
        NovelProject project,
        NovelNode? node,
        IReadOnlyDictionary<string, string>? nodeTitlesById = null)
    {
        var hash = new HashCode();
        hash.Add(project.Characters.Count);

        if (node is null)
        {
            return new NodePropertyPanelStamp(null, hash.ToHashCode());
        }

        hash.Add(node.Id, StringComparer.Ordinal);
        hash.Add(node.Kind);
        hash.Add(node.Title, StringComparer.Ordinal);
        hash.Add(node.Speaker, StringComparer.Ordinal);
        hash.Add(node.Text, StringComparer.Ordinal);
        hash.Add(node.Background, StringComparer.Ordinal);
        hash.Add(node.InheritBackground);
        hash.Add(node.Music, StringComparer.Ordinal);
        hash.Add(node.InheritMusic);
        hash.Add(node.InheritCharacters);
        hash.Add(node.Script, StringComparer.Ordinal);
        hash.Add(node.ScriptBlocks.Count);
        nodeTitlesById ??= BuildOutputTargetTitleLookup(project, node);
        foreach (var character in node.Characters)
        {
            hash.Add(character.Id, StringComparer.Ordinal);
            hash.Add(character.Name, StringComparer.Ordinal);
            hash.Add(character.Position);
            hash.Add(character.Sprite, StringComparer.Ordinal);
            var voices = CharacterVoiceReferences(character);
            hash.Add(voices.Count);
            foreach (var voice in voices)
            {
                hash.Add(voice, StringComparer.OrdinalIgnoreCase);
            }
        }
        foreach (var output in node.Outputs)
        {
            hash.Add(output.Id, StringComparer.Ordinal);
            hash.Add(output.Label, StringComparer.Ordinal);
            hash.Add(output.Condition, StringComparer.Ordinal);
            hash.Add(output.TargetNodeId, StringComparer.Ordinal);
            hash.Add(
                ResolveNodeTitle(nodeTitlesById, output.TargetNodeId, string.Empty),
                StringComparer.Ordinal);
            hash.Add(output.TransitionSound, StringComparer.Ordinal);
            hash.Add(output.FadeDurationMs);
            hash.Add(output.ScriptBlocks.Count);
        }

        return new NodePropertyPanelStamp(node.Id, hash.ToHashCode());
    }

    private static IReadOnlyDictionary<string, string> BuildOutputTargetTitleLookup(
        NovelProject project,
        NovelNode? node)
    {
        if (node is null
            || !node.Outputs.Any(output => output.TargetNodeId is not null))
        {
            return EmptyNodeTitleLookup;
        }

        return BuildNodeTitleLookup(project);
    }

    private static IReadOnlyDictionary<string, string> BuildNodeTitleLookup(
        NovelProject project)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in project.Nodes)
        {
            lookup[node.Id] = node.Title;
        }
        return lookup;
    }

    private static string ResolveNodeTitle(
        IReadOnlyDictionary<string, string> nodeTitlesById,
        string? nodeId,
        string fallback)
    {
        return nodeId is not null && nodeTitlesById.TryGetValue(nodeId, out var title)
            ? title
            : fallback;
    }

    private void MarkDirty(bool refreshGraph = true)
    {
        if (!_codeHasPendingChanges)
        {
            RequestCodeRefresh(useStoredSource: false);
        }
        ClearProjectAnalysisCaches();
        var snapshot = CaptureProjectSnapshot();
        if (!_restoringProjectHistory)
        {
            snapshot = RecordProjectHistorySnapshot(Graph.SelectedNodeId, snapshot);
        }
        _dirty = !CurrentProjectMatchesSavedSnapshot(snapshot);
        RefreshWindowTitle();
        RefreshExplorer();
        RefreshProperties();
        if (refreshGraph)
        {
            Graph.RefreshGraph();
        }
        StatusText.Text = "Проект изменён";
        RequestDiagnosticsRefresh();
    }

    internal static bool ShouldRefreshGraphForDirtyChange(bool originatedFromGraphSurface) =>
        !originatedFromGraphSurface;

    private void ResetProjectHistory()
    {
        _projectHistory.Clear();
        var snapshot = CaptureProjectSnapshot();
        _projectHistory.Add(new ProjectHistoryEntry(snapshot, Graph.SelectedNodeId));
        _projectHistoryIndex = 0;
        _savedProjectSnapshot = snapshot;
        UpdateHistoryControls();
    }

    private string RecordProjectHistorySnapshot(
        string? selectedNodeId,
        string? snapshot = null)
    {
        snapshot ??= CaptureProjectSnapshot();
        if (_projectHistoryIndex >= 0
            && _projectHistory[_projectHistoryIndex].Snapshot == snapshot)
        {
            _projectHistory[_projectHistoryIndex] =
                _projectHistory[_projectHistoryIndex] with { SelectedNodeId = selectedNodeId };
            UpdateHistoryControls();
            return snapshot;
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
        return snapshot;
    }

    private string CaptureProjectSnapshot()
    {
        PreservePendingSourceCode();
        return ProjectSerializer.ToJson(_project);
    }

    private bool CurrentProjectMatchesSavedSnapshot(string? snapshot = null)
    {
        snapshot ??= CaptureProjectSnapshot();
        return _savedProjectSnapshot.Length > 0
            && snapshot == _savedProjectSnapshot;
    }

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
            ClearNodeAssetPickerCaches();
            _projectHistoryIndex = historyIndex;
            _codeHasPendingChanges = false;
            ClearProjectAnalysisCaches();
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
            if (!CodeEditor.HasCachedSourceText(code))
            {
                CodeEditor.SourceText = code;
            }
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
            VisualScriptBlockPreserver.PreserveFrom(_project, compiled);

            _project = compiled;
            ClearNodeAssetPickerCaches();
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
            var snapshot = RecordProjectHistorySnapshot(selectedNodeId);
            _dirty = !CurrentProjectMatchesSavedSnapshot(snapshot);
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
        StatusText.Text = "Код проекта изменён";
        _codeAnalysisTimer.Stop();
        if (ShouldScheduleLiveCodeAnalysis(CodeEditor.EstimatedSourceLength))
        {
            CodeStatusText.Text = "Проверка кода...";
            _codeAnalysisTimer.Start();
        }
        else
        {
            _parsedCodeSource = null;
            _parsedCodeProject = null;
            CodeStatusText.Text = "Большой файл — Ctrl+Enter применит и проверит код";
        }
    }

    private void CodeEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!ShouldReadCodeCursorSource(CodeEditor.EstimatedSourceLength))
        {
            DisableCodeCursorTrackingForLargeFile();
            return;
        }

        var source = EnsureCodeCursorCache();
        if (!ShouldReadCodeCursorSource(source.Length))
        {
            DisableCodeCursorTrackingForLargeFile();
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

    internal static bool ShouldReadCodeCursorSource(int estimatedSourceLength) =>
        CodeEditorPerformancePolicy.ShouldTrackCursorPosition(estimatedSourceLength);

    internal static bool ShouldScheduleLiveCodeAnalysis(int estimatedSourceLength) =>
        CodeEditorPerformancePolicy.ShouldRunLiveAnalysis(estimatedSourceLength);

    private void DisableCodeCursorTrackingForLargeFile()
    {
        if (_lastCodeCursorOffset == -2)
        {
            return;
        }

        _lastCodeCursorOffset = -2;
        CodeCursorText.Text = "Большой файл: позиция курсора отключена";
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
        if (!ShouldReadCodeCursorSource(source.Length))
        {
            _codeCursorSource = string.Empty;
            _codeLineStarts = [0];
            _lastCodeCursorOffset = -2;
            _codeCursorCacheDirty = false;
            CodeCursorText.Text = "Большой файл: позиция курсора отключена";
            return;
        }

        if (!_codeCursorCacheDirty
            && _codeCursorSource.Equals(source, StringComparison.Ordinal))
        {
            return;
        }

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
        if (!ShouldScheduleLiveCodeAnalysis(source.Length))
        {
            _parsedCodeSource = null;
            _parsedCodeProject = null;
            ApplyCodeHighlighting(source, null);
            CodeStatusText.Foreground = Brushes.Goldenrod;
            CodeStatusText.Text = _codeHasPendingChanges
                ? "Большой файл — Ctrl+Enter применит и проверит код"
                : "Большой файл — live-проверка отключена";
            return;
        }

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
            if (CodeEditorPerformancePolicy.ShouldApplyLiveErrorHighlighting(source.Length))
            {
                ApplyCodeHighlighting(source, error);
            }
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
        if (error is null && _codeSyntaxSpanLimitExceeded)
        {
            return;
        }
        if (error is null
            && spans.Count == 0
            && !CodeEditorPerformancePolicy.ShouldApplyFullSyntaxHighlighting(source.Length))
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
        CodeEditorPerformancePolicy.ShouldApplyFullSyntaxHighlighting(source.Length);

    private IReadOnlyList<ProjectLanguageSyntaxSpan> GetCodeSyntaxSpans(string source)
    {
        if (_codeSyntaxSource == source)
        {
            return _codeSyntaxSpans;
        }

        _codeSyntaxSource = source;
        _codeSyntaxSpanLimitExceeded = !ProjectLanguage.TryGetSyntaxSpans(
            source,
            CodeEditorPerformancePolicy.MaxHighlightedSyntaxSpans,
            out _codeSyntaxSpans);
        if (_codeSyntaxSpanLimitExceeded)
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

    private void RefreshAssets(
        bool syncFromDisk = true,
        bool externalFileEvent = false)
    {
        var filesChanged = false;
        if (syncFromDisk)
        {
            filesChanged = SyncFilesFromDisk();
        }
        if (ShouldClearAssetFileCaches(externalFileEvent, filesChanged))
        {
            ClearAssetFileCaches();
        }
        RefreshAssetFolders();
        RefreshAssetList();
        if (externalFileEvent || filesChanged)
        {
            RefreshProperties();
        }
    }

    private void ClearAssetFileCaches()
    {
        _assetSizeCache.Clear();
        _assetPreviewImageCache.Clear();
        _assetUsageCountCache = null;
        _assetListStamp = null;
        _assetPreviewStamp = null;
        _projectDiagnosticsCache = null;
        ClearNodeAssetPickerCaches();
    }

    private void RefreshAssetCatalogAfterEditorChange()
    {
        ClearNodeAssetPickerCaches();
        MarkDirty(refreshGraph: false);
        RefreshAssets(syncFromDisk: false);
    }

    internal static bool ShouldClearAssetFileCaches(
        bool externalFileEvent,
        bool diskSyncChangedProject) =>
        externalFileEvent || diskSyncChangedProject;

    private void ClearProjectAnalysisCaches()
    {
        _assetUsageCountCache = null;
        _assetListStamp = null;
        _projectDiagnosticsCache = null;
        _diagnosticsPanelStamp = null;
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

        EnsureAssetPickerCachesCurrent();
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
        EnsureAssetPickerCachesCurrent();
        var folderOptions = GetCachedFolderOptions(kind);
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

    private List<NodeAssetFolderOption> GetCachedFolderOptions(AssetKind kind)
    {
        if (_nodeAssetFolderOptionsCache.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var folderCounts = _project.Assets
            .Where(asset => asset.Kind == kind)
            .GroupBy(
                asset => asset.Folder,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var folders = _project.AssetFolders
            .Concat(_project.Assets.Select(asset => asset.Folder))
            .Select(ProjectAssets.NormalizeFolder)
            .Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var options = new List<NodeAssetFolderOption>(folders.Count + 1)
        {
            new(string.Empty, "Все папки"),
        };
        options.AddRange(
            folders.Select(folder => new NodeAssetFolderOption(
                folder,
                $"{folder} ({folderCounts.GetValueOrDefault(folder)})")));
        _nodeAssetFolderOptionsCache[kind] = options;
        return options;
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

        EnsureAssetPickerCachesCurrent();
        var assetId = reference[1..];
        return _nodeAssetByIdCache.TryGetValue(assetId, out var asset)
            ? asset
            : null;
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
            EnsureAssetPickerCachesCurrent();
            var normalizedFolder = ProjectAssets.NormalizeFolder(folder);
            var choices = GetCachedAssetChoices(kind, normalizedFolder);
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

    private List<NodeAssetChoice> GetCachedAssetChoices(
        AssetKind kind,
        string normalizedFolder)
    {
        var key = new NodeAssetChoiceCacheKey(kind, normalizedFolder);
        if (_nodeAssetChoicesCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

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
        _nodeAssetChoicesCache[key] = choices;
        return choices;
    }

    private void EnsureAssetPickerCachesCurrent()
    {
        if (!_nodeAssetPickerCachesDirty)
        {
            return;
        }

        _nodeAssetFolderOptionsCache.Clear();
        _nodeAssetChoicesCache.Clear();
        _nodeAssetByIdCache.Clear();
        foreach (var asset in _project.Assets)
        {
            _nodeAssetByIdCache[asset.Id] = asset;
        }
        _nodeAssetPickerCachesDirty = false;
    }

    private void ClearNodeAssetPickerCaches()
    {
        _nodeAssetFolderOptionsCache.Clear();
        _nodeAssetChoicesCache.Clear();
        _nodeAssetByIdCache.Clear();
        _nodeAssetPickerCachesDirty = true;
        unchecked
        {
            _assetCatalogRevision++;
        }
        _nodePropertyPanelStamp = null;
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
        if (!_filesRefreshDispatchGate.TryRequest())
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                _filesRefreshTimer.Stop();
                _filesRefreshTimer.Start();
            }
            finally
            {
                _filesRefreshDispatchGate.Complete();
            }
        });
    }

    private void RefreshAssetList()
    {
        var selectedId = (AssetsGrid.SelectedItem as AssetView)?.Id;
        var stamp = CreateAssetListStamp(
            _project.Assets.Count,
            _assetCatalogRevision,
            _selectedAssetFolder,
            AssetSearchBox.Text);
        if (_assetListStamp == stamp && AssetsGrid.ItemsSource is not null)
        {
            return;
        }

        _assetListStamp = stamp;
        var filtered = AssetListFilter.Apply(
            _project.Assets,
            _selectedAssetFolder,
            AssetSearchBox.Text);
        var usageCounts = GetAssetUsageCounts();
        var assetViews = filtered.Assets
            .Select(
                asset => new AssetView(
                    asset,
                    usageCounts.GetValueOrDefault(asset.Id),
                    AssetSize(asset)))
            .ToList();
        AssetsGrid.ItemsSource = assetViews;
        AssetSearchSummaryText.Text = filtered.Query.Length == 0
            ? string.Empty
            : $"Найдено {assetViews.Count} из {filtered.FolderAssetCount}";
        if (selectedId is not null)
        {
            AssetsGrid.SelectedItem = assetViews
                .FirstOrDefault(
                    view => view.Id.Equals(
                        selectedId,
                        StringComparison.OrdinalIgnoreCase));
        }
        RefreshAssetPreview();
    }

    internal static AssetListStamp CreateAssetListStamp(
        int assetCount,
        int assetCatalogRevision,
        string? selectedFolder,
        string query)
    {
        var normalizedFolder = selectedFolder is null
            ? null
            : ProjectAssets.NormalizeFolder(selectedFolder.Trim());
        return new AssetListStamp(
            normalizedFolder,
            query.Trim(),
            assetCount,
            assetCatalogRevision);
    }

    internal static AssetListStamp CreateAssetListStamp(
        IEnumerable<NovelAsset> assets,
        string? selectedFolder,
        string query)
    {
        var normalizedFolder = selectedFolder is null
            ? null
            : ProjectAssets.NormalizeFolder(selectedFolder.Trim());
        var hash = new HashCode();
        var count = 0;
        foreach (var asset in assets)
        {
            count++;
            hash.Add(asset.Id, StringComparer.OrdinalIgnoreCase);
            hash.Add(asset.Path, StringComparer.OrdinalIgnoreCase);
            hash.Add(asset.Folder, StringComparer.OrdinalIgnoreCase);
            hash.Add(asset.Kind);
        }

        return new AssetListStamp(
            normalizedFolder,
            query.Trim(),
            count,
            hash.ToHashCode());
    }

    private IReadOnlyDictionary<string, int> GetAssetUsageCounts() =>
        _assetUsageCountCache ??= _project.CountAssetReferencesById();

    private AssetView? FindVisibleAssetView(string assetId)
    {
        return AssetsGrid.ItemsSource is IEnumerable<AssetView> assetViews
            ? assetViews.FirstOrDefault(view => view.Id.Equals(
                assetId,
                StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private void RefreshAssetFolders()
    {
        var stamp = CreateAssetFolderTreeStamp(
            _project.AssetFolders.Count,
            _project.Assets.Count,
            _assetCatalogRevision,
            _selectedAssetFolder);
        if (_assetFolderTreeStamp == stamp && AssetFoldersTree.Items.Count > 0)
        {
            return;
        }

        _assetFolderTreeStamp = stamp;
        _refreshingAssetFolders = true;
        try
        {
            var folderCounts = CountAssetsByFolder(_project.Assets);
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
                        item = new TreeViewItem
                        {
                            Header = CreateFolderHeader(
                                segment,
                                folderCounts.GetValueOrDefault(path)),
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

    internal static AssetFolderTreeStamp CreateAssetFolderTreeStamp(
        int folderCount,
        int assetCount,
        int assetCatalogRevision,
        string? selectedFolder)
    {
        var normalizedSelectedFolder = selectedFolder is null
            ? null
            : ProjectAssets.NormalizeFolder(selectedFolder.Trim());
        return new AssetFolderTreeStamp(
            normalizedSelectedFolder,
            folderCount,
            assetCount,
            assetCatalogRevision);
    }

    private static IReadOnlyDictionary<string, int> CountAssetsByFolder(
        IEnumerable<NovelAsset> assets)
    {
        var folderCounts = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var asset in assets)
        {
            var folder = ProjectAssets.NormalizeFolder(asset.Folder);
            folderCounts[folder] = folderCounts.GetValueOrDefault(folder) + 1;
        }

        return folderCounts;
    }

    internal static AssetFolderTreeStamp CreateAssetFolderTreeStamp(
        IEnumerable<string> folders,
        IEnumerable<NovelAsset> assets,
        string? selectedFolder)
    {
        var normalizedSelectedFolder = selectedFolder is null
            ? null
            : ProjectAssets.NormalizeFolder(selectedFolder.Trim());
        var normalizedFolders = folders
            .Select(ProjectAssets.NormalizeFolder)
            .Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var folderCounts = assets
            .GroupBy(
                asset => ProjectAssets.NormalizeFolder(asset.Folder),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var assetCount = 0;
        var hash = new HashCode();
        foreach (var folder in normalizedFolders)
        {
            hash.Add(folder, StringComparer.OrdinalIgnoreCase);
            hash.Add(folderCounts.GetValueOrDefault(folder));
        }
        foreach (var count in folderCounts.Values)
        {
            assetCount += count;
        }

        return new AssetFolderTreeStamp(
            normalizedSelectedFolder,
            normalizedFolders.Length,
            assetCount,
            hash.ToHashCode());
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
        string path;
        try
        {
            path = ResolveAssetPath(asset);
        }
        catch (Exception error) when (IsAssetPathError(error))
        {
            return "некорректный путь";
        }

        if (_assetSizeCache.TryGetValue(path, out var cachedSize))
        {
            return cachedSize.Label;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(path);
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return "недоступен";
        }

        if (!fileInfo.Exists)
        {
            _assetSizeCache.Remove(path);
            return "нет файла";
        }

        long bytes;
        DateTime lastWriteTimeUtc;
        try
        {
            bytes = fileInfo.Length;
            lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            _assetSizeCache.Remove(path);
            return "недоступен";
        }

        if (_assetSizeCache.TryGetValue(path, out var cached)
            && ShouldReuseAssetSizeCache(cached, bytes, lastWriteTimeUtc))
        {
            return cached.Label;
        }

        var size = bytes switch
        {
            >= 1024L * 1024L =>
                $"{bytes / (1024d * 1024d):0.##} МБ",
            >= 1024L => $"{bytes / 1024d:0.##} КБ",
            _ => $"{bytes} Б",
        };
        _assetSizeCache[path] = new AssetSizeCacheEntry(
            bytes,
            lastWriteTimeUtc,
            size);
        return size;
    }

    internal static bool ShouldReuseAssetSizeCache(
        AssetSizeCacheEntry cached,
        long bytes,
        DateTime lastWriteTimeUtc) =>
        cached.Bytes == bytes && cached.LastWriteTimeUtc == lastWriteTimeUtc;

    private void RefreshAssetPreview()
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        var stamp = CreateAssetPreviewStamp(view?.Asset, view?.UsageCount ?? 0);
        if (_assetPreviewStamp == stamp)
        {
            return;
        }

        _assetPreviewStamp = stamp;
        StopAssetPreviewPlayback();
        _assetPreviewAudioPath = null;
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
            if (!TryResolveAssetPath(view.Asset, out var audioPath))
            {
                AssetPreviewPlaceholder.Text = "Путь аудио недоступен";
                return;
            }
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
        if (!TryResolveAssetPath(view.Asset, out var path))
        {
            AssetPreviewPlaceholder.Text = "Путь изображения недоступен";
            return;
        }
        if (!File.Exists(path))
        {
            return;
        }
        var image = LoadAssetPreviewImage(path);
        if (image is null)
        {
            AssetPreviewPlaceholder.Text = "Не удалось открыть изображение";
            return;
        }

        AssetPreviewImage.Source = image;
        AssetPreviewPlaceholder.Visibility = Visibility.Collapsed;
    }

    internal static AssetPreviewStamp CreateAssetPreviewStamp(
        NovelAsset? asset,
        int usageCount)
    {
        if (asset is null)
        {
            return new AssetPreviewStamp(null, AssetKind.Other, string.Empty, 0);
        }

        return new AssetPreviewStamp(
            asset.Id,
            asset.Kind,
            asset.Path,
            usageCount);
    }

    private BitmapImage? LoadAssetPreviewImage(string path)
    {
        if (_assetPreviewImageCache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        BitmapImage? image;
        try
        {
            image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 900;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
        }
        catch (Exception)
        {
            image = null;
        }

        _assetPreviewImageCache.Set(path, image);
        return image;
    }

    private void AssetsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshAssetPreview();

    private void AssetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleAssetListRefresh();
    }

    private void ScheduleAssetListRefresh()
    {
        _assetSearchTimer.Stop();
        _assetSearchTimer.Start();
    }

    private void AssetSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || AssetSearchBox.Text.Length == 0)
        {
            return;
        }

        AssetSearchBox.Clear();
        _assetSearchTimer.Stop();
        RefreshAssetList();
        e.Handled = true;
    }

    private void AssetsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            AssetSearchBox.Focus();
            AssetSearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        var view = AssetsGrid.SelectedItem as AssetView;
        if (view is null)
        {
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyAssetReference_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            OpenSelectedAsset_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2)
        {
            RenameAsset_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            DeleteAsset_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space
            && view.Asset.Kind == AssetKind.Audio
            && AssetPreviewPlayButton.IsEnabled)
        {
            PlayAssetPreview_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && AssetPreviewStopButton.IsEnabled)
        {
            StopAssetPreview_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
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
        if (IsInitialized && !AssetPreviewStopButton.IsEnabled)
        {
            return;
        }

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
        if (!RebuildAssetsContextMenu())
        {
            e.Handled = true;
        }
    }

    internal bool RebuildAssetsContextMenuForSmoke() =>
        RebuildAssetsContextMenu();

    internal bool BindVoiceAssetToCharacterForSmoke(
        string assetId,
        string characterId)
    {
        var asset = _project.FindAsset(assetId);
        if (asset is null)
        {
            return false;
        }

        BindVoiceAssetToCharacter(asset, characterId);
        var reference = AssetReference.Create(asset.Id);
        var node = Graph.SelectedNode;
        return node?.Characters
            .FirstOrDefault(character => character.Id == characterId)
            ?.GetVoiceSounds()
            .Contains(reference, StringComparer.OrdinalIgnoreCase)
            == true;
    }

    private bool RebuildAssetsContextMenu()
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        var menu = AssetsGrid.ContextMenu ?? new ContextMenu();
        AssetsGrid.ContextMenu = menu;
        menu.Items.Clear();
        if (view is null)
        {
            return false;
        }

        var selectedNode = Graph.SelectedNode;
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
                menu.Items.Add(CreateAssetMenuItem(
                    "Создать персонажа в библиотеке из спрайта",
                    () => CreateLibraryCharacterFromSprite(view.Asset)));
                menu.Items.Add(CreateAssetMenuItem(
                    "Добавить персонажа в выбранную ноду",
                    () => AddCharacterFromSpriteToSelectedNode(view.Asset),
                    selectedNode is not null));
                menu.Items.Add(CreateCharacterAssetVoiceMenu(view.Asset, selectedNode));
            }
        }

        if (view.Asset.Kind == AssetKind.Audio)
        {
            var selectedOutput = SelectedOutput();
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateAssetMenuItem(
                "Привязать как музыку выбранной ноды",
                () => BindAssetAsNodeMusic(view.Asset),
                selectedNode is not null));
            menu.Items.Add(CreateTransitionSoundBindingMenu(
                view.Asset,
                selectedNode,
                selectedOutput));
            if (IsInAssetFolder(view.Asset, "voices"))
            {
                menu.Items.Add(CreateVoiceBindingMenu(view.Asset, selectedNode));
                menu.Items.Add(CreateLibraryVoiceBindingMenu(view.Asset));
            }
        }

        return true;
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
            return CreateCharacterWithVoiceMenu(asset, voices);
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

    private MenuItem CreateCharacterWithVoiceMenu(
        NovelAsset spriteAsset,
        IReadOnlyList<NovelAsset> voices)
    {
        if (voices.Count == 1)
        {
            var voice = voices[0];
            return CreateAssetMenuItem(
                $"Создать персонажа с voice-блипом «{voice.Id}»",
                () => CreateLibraryCharacterFromSprite(spriteAsset, voice));
        }

        var menu = CreateHoverSubmenu("Создать персонажа с voice-блипом");
        foreach (var voice in voices)
        {
            menu.Items.Add(CreateAssetMenuItem(
                $"{voice.Id}  ·  {Path.GetFileName(voice.Path)}",
                () => CreateLibraryCharacterFromSprite(spriteAsset, voice)));
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

    private MenuItem CreateTransitionSoundBindingMenu(
        NovelAsset asset,
        NovelNode? node,
        NodeOutput? selectedOutput)
    {
        if (node is null)
        {
            return CreateDisabledAssetMenuItem(
                "Привязать звук перехода: выберите ноду");
        }

        var outputs = GetBindableTransitionSoundOutputs(asset, node);
        if (outputs.Count == 0)
        {
            return CreateDisabledAssetMenuItem(
                "Привязать звук перехода: переходов нет");
        }

        if (outputs.Count == 1)
        {
            var output = outputs[0];
            return CreateAssetMenuItem(
                $"Привязать как звук перехода «{output.Label}»",
                () => BindAssetAsOutputTransitionSound(asset, output.Id));
        }

        var selectedOutputId = selectedOutput?.Id;
        var menu = CreateHoverSubmenu("Привязать как звук перехода");
        foreach (var output in outputs)
        {
            var label = output.Id == selectedOutputId
                ? $"Выбранный: {output.Label}"
                : output.Label;
            menu.Items.Add(CreateAssetMenuItem(
                label,
                () => BindAssetAsOutputTransitionSound(asset, output.Id)));
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
        var node = Graph.SelectedNode;
        if (node is null || asset.Kind != AssetKind.Image)
        {
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        MarkOverrideIfChanged(node, "inheritBackground", node.InheritBackground, false);
        MarkOverrideIfChanged(node, "background", node.Background, reference);
        node.InheritBackground = false;
        node.Background = reference;
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
        StatusText.Text = $"Фон ноды «{node.Title}»: {reference}";
    }

    private void BindAssetAsNodeMusic(NovelAsset asset)
    {
        var node = Graph.SelectedNode;
        if (node is null || asset.Kind != AssetKind.Audio)
        {
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        MarkOverrideIfChanged(node, "inheritMusic", node.InheritMusic, false);
        MarkOverrideIfChanged(node, "music", node.Music, reference);
        node.InheritMusic = false;
        node.Music = reference;
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
        StatusText.Text = $"Музыка ноды «{node.Title}»: {reference}";
    }

    private void BindAssetAsOutputTransitionSound(NovelAsset asset, string? outputId = null)
    {
        var node = Graph.SelectedNode;
        var output = outputId is null
            ? SelectedOutput()
            : node?.Outputs.FirstOrDefault(candidate => candidate.Id == outputId);
        if (!CanBindAssetAsOutputTransitionSound(asset, node, output))
        {
            return;
        }

        var reference = AssetReference.Create(asset.Id);
        output!.TransitionSound = reference;
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
        StatusText.Text = $"Звук перехода «{output.Label}»: {reference}";
    }

    internal static IReadOnlyList<NodeOutput> GetBindableTransitionSoundOutputs(
        NovelAsset asset,
        NovelNode? node) =>
        asset.Kind == AssetKind.Audio && node is not null
            ? [.. node.Outputs]
            : [];

    internal static bool CanBindAssetAsOutputTransitionSound(
        NovelAsset asset,
        NovelNode? node,
        NodeOutput? output) =>
        asset.Kind == AssetKind.Audio
        && node is not null
        && output is not null
        && node.Outputs.Any(candidate => candidate.Id == output.Id);

    private void CreateLibraryCharacterFromSprite(
        NovelAsset asset,
        NovelAsset? initialVoice = null)
    {
        if (asset.Kind != AssetKind.Image)
        {
            return;
        }

        var suggestedVoices = initialVoice is null
            ? Array.Empty<string>()
            : [AssetReference.Create(initialVoice.Id)];
        var dialog = new CharacterEditorWindow(
            null,
            GetCharacterSpriteAssets(),
            GetVoiceBlipAssets(),
            SuggestedCharacterName(asset),
            AssetReference.Create(asset.Id),
            suggestedVoices,
            requireSprite: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var character = CreateCharacterPlacementFromDialog(
            dialog,
            CreateUniqueLibraryCharacterId(dialog.CharacterName));
        _project.Characters.Add(character);
        MarkDirty(refreshGraph: false);
        StatusText.Text =
            initialVoice is null
                ? $"Персонаж «{CharacterLabel(character)}» создан в библиотеке"
                : $"Персонаж «{CharacterLabel(character)}» создан с voice-блипом «{initialVoice.Id}»";
    }

    private void AddCharacterFromSpriteToSelectedNode(NovelAsset asset)
    {
        var node = Graph.SelectedNode;
        if (node is null || asset.Kind != AssetKind.Image)
        {
            return;
        }

        var dialog = new CharacterEditorWindow(
            null,
            GetCharacterSpriteAssets(),
            GetVoiceBlipAssets(),
            SuggestedCharacterName(asset),
            AssetReference.Create(asset.Id),
            suggestedVoices: null,
            requireSprite: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var character = AddCharacterToNodeFromDialog(node, dialog);
        MarkDirty(refreshGraph: false);
        StatusText.Text =
            $"Персонаж «{CharacterLabel(character)}» добавлен в ноду «{node.Title}»";
    }

    private void BindVoiceAssetToCharacter(NovelAsset asset, string characterId)
    {
        var node = Graph.SelectedNode;
        if (node is null || asset.Kind != AssetKind.Audio)
        {
            return;
        }

        var effectiveCharacters = node.Kind != NodeKind.Start && node.InheritCharacters
            ? GetEffectiveCharacters(node)
            : null;
        var result = TryBindVoiceAssetToNodeCharacter(
            node,
            asset,
            characterId,
            effectiveCharacters);
        if (result == NodeVoiceBindingResult.Unavailable)
        {
            StatusText.Text = "Персонаж для привязки voice-блипа не найден";
            return;
        }

        if (result == NodeVoiceBindingResult.Unchanged)
        {
            var unchangedCharacter = node.Characters.FirstOrDefault(
                    candidate => candidate.Id == characterId)
                ?? effectiveCharacters?.FirstOrDefault(
                    candidate => candidate.Id == characterId);
            var label = unchangedCharacter is null
                ? characterId
                : CharacterLabel(unchangedCharacter);
            StatusText.Text =
                $"Voice-блип уже привязан к «{label}»";
            return;
        }

        var character = node.Characters.First(candidate => candidate.Id == characterId);
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
        StatusText.Text =
            $"Voice-блипы персонажа «{CharacterLabel(character)}»: {character.VoiceSounds.Count}";
    }

    internal static NodeVoiceBindingResult TryBindVoiceAssetToNodeCharacter(
        NovelNode node,
        NovelAsset asset,
        string characterId,
        IEnumerable<CharacterPlacement>? effectiveCharacters = null)
    {
        if (asset.Kind != AssetKind.Audio)
        {
            return NodeVoiceBindingResult.Unavailable;
        }

        var reference = AssetReference.Create(asset.Id);
        CharacterPlacement? character;
        if (node.Kind != NodeKind.Start && node.InheritCharacters)
        {
            var inheritedCharacters = (effectiveCharacters ?? node.Characters)
                .Select(candidate => candidate.Clone())
                .ToList();
            character = inheritedCharacters.FirstOrDefault(
                candidate => candidate.Id == characterId);
            if (character is null)
            {
                return NodeVoiceBindingResult.Unavailable;
            }

            var inheritedVoices = CharacterVoiceReferences(character);
            if (inheritedVoices.Contains(reference, StringComparer.OrdinalIgnoreCase))
            {
                return NodeVoiceBindingResult.Unchanged;
            }

            node.Characters.Clear();
            node.Characters.AddRange(inheritedCharacters);
            node.InheritCharacters = false;
            if (node.UsesTypeDefaults)
            {
                node.PropertyOverrides.Add("inheritCharacters");
            }
        }
        else
        {
            character = node.Characters.FirstOrDefault(
                candidate => candidate.Id == characterId);
            if (character is null)
            {
                return NodeVoiceBindingResult.Unavailable;
            }
        }

        var voices = CharacterVoiceReferences(character);
        if (!voices.Contains(reference, StringComparer.OrdinalIgnoreCase))
        {
            voices.Add(reference);
        }
        else
        {
            return NodeVoiceBindingResult.Unchanged;
        }

        character.SetVoiceSounds(voices);
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("characters");
        }
        return NodeVoiceBindingResult.Changed;
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

        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
        StatusText.Text =
            $"Voice-блипы библиотечного персонажа «{CharacterLabel(character)}»: {character.VoiceSounds.Count}";
    }

    private void RefreshAssetUsageAfterBinding()
    {
        _assetUsageCountCache = null;
        _assetListStamp = null;
        _assetPreviewStamp = null;
        RefreshAssetList();
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
        if (usage.NodeId is not null)
        {
            Graph.SelectNode(usage.NodeId);
            if (Graph.SelectedNodeId == usage.NodeId)
            {
                WorkspaceTabs.SelectedItem = GraphTab;
                SelectAssetUsageDetail(usage.Location);
                StatusText.Text = $"Ассет используется в ноде: {usage.Location}";
                return;
            }
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
            var outputView = FindVisibleOutputView(outputLabel);
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

        var characterView = FindVisibleCharacterView(characterName);
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
            _ = ProjectAssets.ImportMany(_project, _projectPath!, dialog.FileNames);
            var after = _project.Assets.Count;
            var importedCount = after - before;
            if (ShouldRefreshAssetsAfterImport(before, after))
            {
                RefreshAssetCatalogAfterEditorChange();
            }
            StatusText.Text = importedCount > 0
                ? $"Импортировано файлов: {importedCount}"
                : "Новых ассетов не импортировано";
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
            RefreshAssetCatalogAfterEditorChange();
            AssetsGrid.SelectedItem = FindVisibleAssetView(asset.Id);
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
            RefreshAssetCatalogAfterEditorChange();
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
            RefreshAssetCatalogAfterEditorChange();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException)
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
            RefreshAssetCatalogAfterEditorChange();
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
            or UnauthorizedAccessException
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
            RefreshAssetCatalogAfterEditorChange();
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
        RefreshAssetCatalogAfterEditorChange();
        AssetsGrid.SelectedItem = FindVisibleAssetView(dialog.AssetId);
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
        RefreshAssetCatalogAfterEditorChange();
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

    private bool TryResolveAssetPath(NovelAsset asset, out string path)
    {
        try
        {
            path = ResolveAssetPath(asset);
            return true;
        }
        catch (Exception error) when (IsAssetPathError(error))
        {
            path = string.Empty;
            return false;
        }
    }

    private static bool IsAssetPathError(Exception error) =>
        error is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or InvalidOperationException
        or NotSupportedException;

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
        if (!ShouldRunManualSave(
                hasProjectPath: _projectPath is not null,
                dirty: _dirty,
                workspaceNeedsProjectFile: _workspaceNeedsProjectFile,
                codeHasPendingChanges: _codeHasPendingChanges))
        {
            StatusText.Text = "Нет изменений для сохранения";
            return true;
        }

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

    internal static bool ShouldRunManualSave(
        bool hasProjectPath,
        bool dirty,
        bool workspaceNeedsProjectFile,
        bool codeHasPendingChanges) =>
        !hasProjectPath || dirty || workspaceNeedsProjectFile || codeHasPendingChanges;

    internal static bool ShouldRefreshAssetsAfterImport(
        int assetCountBefore,
        int assetCountAfter) =>
        assetCountAfter != assetCountBefore;

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

    private void ProjectSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ScheduleProjectExplorerRefresh();
    }

    private void ScheduleProjectExplorerRefresh()
    {
        _projectExplorerSearchTimer.Stop();
        _projectExplorerSearchTimer.Start();
    }

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
        _projectExplorerSearchTimer.Stop();
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

        var node = Graph.SelectedNode;
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
        SetCharacterButtons(CanEditNodeCharacters(
            node,
            InheritCharactersCheck.IsChecked == true));
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
                RefreshAssetCatalogAfterEditorChange();
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
        var node = Graph.SelectedNode;
        if (!CanEditSelectedNodeCharacters(node))
        {
            return;
        }

        var dialog = new CharacterEditorWindow(
            null,
            GetCharacterSpriteAssets(),
            GetVoiceBlipAssets(),
            requireSprite: true)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddCharacterToNodeFromDialog(node!, dialog);
        MarkDirty(refreshGraph: false);
    }

    private void AddLibraryCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
        if (!CanEditSelectedNodeCharacters(node))
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

        node!.InheritCharacters = false;
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("inheritCharacters");
            node.PropertyOverrides.Add("characters");
        }
        InheritCharactersCheck.IsChecked = false;
        _project.AddCharacterClone(node.Id, dialog.SelectedCharacter);
        MarkDirty(refreshGraph: false);
        StatusText.Text = $"Персонаж «{CharacterLabel(dialog.SelectedCharacter)}» добавлен из библиотеки";
    }

    private void SaveCharacterToLibrary_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
        if (!CanEditSelectedNodeCharacters(node))
        {
            return;
        }

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
        MarkDirty(refreshGraph: false);
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
        if (!CanEditSelectedNodeCharacters(node))
        {
            return;
        }

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
        if (node?.UsesTypeDefaults == true)
        {
            node.PropertyOverrides.Add("characters");
        }
        MarkDirty(refreshGraph: false);
    }

    private List<string> NormalizeVoiceReferences(IEnumerable<string> references) =>
        references
            .Select(reference => NormalizeAssetPath(reference, "voices"))
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private CharacterPlacement AddCharacterToNodeFromDialog(
        NovelNode node,
        CharacterEditorWindow dialog)
    {
        node.InheritCharacters = false;
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("inheritCharacters");
            node.PropertyOverrides.Add("characters");
        }
        InheritCharactersCheck.IsChecked = false;

        var character = CreateCharacterPlacementFromDialog(
            dialog,
            $"character-{Guid.NewGuid():N}");
        node.Characters.Add(character);
        return character;
    }

    private CharacterPlacement CreateCharacterPlacementFromDialog(
        CharacterEditorWindow dialog,
        string characterId)
    {
        var voiceSounds = NormalizeVoiceReferences(dialog.VoiceSounds);
        return new CharacterPlacement
        {
            Id = characterId,
            Name = dialog.CharacterName,
            Sprite = NormalizeAssetPath(dialog.Sprite, "characters"),
            Position = dialog.Position,
            VoiceSound = voiceSounds.FirstOrDefault() ?? string.Empty,
            VoiceSounds = voiceSounds,
            VoicePitch = dialog.VoicePitch,
            VoiceEveryNthCharacter = dialog.VoiceEveryNthCharacter,
        };
    }

    private string CreateUniqueLibraryCharacterId(string name)
    {
        var baseId = ProjectAssets.MakeId(name);
        if (baseId.Equals("asset", StringComparison.OrdinalIgnoreCase))
        {
            baseId = "character";
        }

        var candidate = baseId;
        var suffix = 2;
        while (_project.FindCharacter(candidate) is not null)
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }
        return candidate;
    }

    private static string SuggestedCharacterName(NovelAsset asset)
    {
        var name = Path.GetFileNameWithoutExtension(asset.Path);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = asset.Id;
        }

        return name
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();
    }

    internal static bool CanEditNodeCharacters(
        NovelNode? node,
        bool inheritCharactersChecked) =>
        node is not null
        && (node.Kind == NodeKind.Start || !inheritCharactersChecked);

    private bool CanEditSelectedNodeCharacters(NovelNode? node) =>
        CanEditNodeCharacters(
            node,
            InheritCharactersCheck.IsChecked == true);

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
        var node = Graph.SelectedNode;
        var character = SelectedCharacter();
        if (!CanEditSelectedNodeCharacters(node) || character is null)
        {
            return;
        }
        node!.Characters.Remove(character);
        if (node.UsesTypeDefaults)
        {
            node.PropertyOverrides.Add("characters");
        }
        MarkDirty(refreshGraph: false);
    }

    private void DuplicateCharacter_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
        var character = SelectedCharacter();
        if (!CanEditSelectedNodeCharacters(node) || character is null)
        {
            return;
        }

        var duplicate = _project.DuplicateCharacter(node!.Id, character.Id);
        MarkDirty(refreshGraph: false);
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

        var node = Graph.SelectedNode;
        var character = SelectedCharacter();
        if (!CanEditSelectedNodeCharacters(node) || character is null)
        {
            return;
        }

        if (!_project.MoveCharacter(node!.Id, character.Id, direction))
        {
            return;
        }

        MarkDirty(refreshGraph: false);
        SelectCharacterView(character.Id);
        StatusText.Text = $"Персонаж «{CharacterLabel(character)}» перемещён в списке";
    }

    private void SetSelectedCharacterPosition(CharacterPosition position)
    {
        var node = Graph.SelectedNode;
        var character = SelectedCharacter();
        if (!CanEditSelectedNodeCharacters(node)
            || character is null
            || character.Position == position)
        {
            return;
        }

        var characterId = character.Id;
        if (!_project.SetCharacterPosition(node!.Id, character.Id, position))
        {
            return;
        }

        MarkDirty(refreshGraph: false);
        SelectCharacterView(characterId);
        StatusText.Text = $"Персонаж «{CharacterLabel(character)}» перемещён: {CharacterPositionLabel(position)}";
    }

    private void CharactersGrid_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            CharactersGrid.SelectedItem = null;
            return;
        }

        row.IsSelected = true;
        CharactersGrid.SelectedItem = row.Item;
        row.Focus();
    }

    private void CharactersGrid_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var node = Graph.SelectedNode;
        var canEdit = CanEditSelectedNodeCharacters(node);
        var character = SelectedCharacter();
        var selectedIndex = CharactersGrid.SelectedIndex;
        var characterCount = CharactersGrid.Items.Count;
        var menu = CharactersGrid.ContextMenu ?? new ContextMenu();
        CharactersGrid.ContextMenu = menu;
        menu.Items.Clear();

        if (node is null)
        {
            menu.Items.Add(CreateDisabledAssetMenuItem("Выберите ноду"));
            return;
        }

        if (!canEdit)
        {
            menu.Items.Add(CreateDisabledAssetMenuItem("Персонажи наследуются"));
            return;
        }

        menu.Items.Add(CreateCharacterMenuItem("Добавить персонажа", () => AddCharacter_Click(this, new RoutedEventArgs())));
        menu.Items.Add(CreateCharacterMenuItem(
            "Добавить из библиотеки",
            () => AddLibraryCharacter_Click(this, new RoutedEventArgs()),
            _project.Characters.Count > 0));
        if (character is null)
        {
            menu.Items.Add(CreateDisabledAssetMenuItem("Выберите персонажа"));
            return;
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateCharacterMenuItem("Изменить", () => EditCharacter_Click(this, new RoutedEventArgs())));
        menu.Items.Add(CreateCharacterMenuItem(
            "Дублировать",
            () => DuplicateCharacter_Click(this, new RoutedEventArgs())));
        menu.Items.Add(CreateCharacterMenuItem(
            "Сохранить в библиотеку",
            () => SaveCharacterToLibrary_Click(this, new RoutedEventArgs())));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateCharacterMenuItem(
            "Выше",
            () => MoveSelectedCharacter(-1),
            selectedIndex > 0));
        menu.Items.Add(CreateCharacterMenuItem(
            "Ниже",
            () => MoveSelectedCharacter(1),
            selectedIndex >= 0 && selectedIndex < characterCount - 1));

        var positionMenu = CreateHoverSubmenu("Позиция");
        positionMenu.Items.Add(CreateCharacterMenuItem(
            "Слева",
            () => SetSelectedCharacterPosition(CharacterPosition.Left)));
        positionMenu.Items.Add(CreateCharacterMenuItem(
            "Центр",
            () => SetSelectedCharacterPosition(CharacterPosition.Center)));
        positionMenu.Items.Add(CreateCharacterMenuItem(
            "Справа",
            () => SetSelectedCharacterPosition(CharacterPosition.Right)));
        menu.Items.Add(positionMenu);

        menu.Items.Add(new Separator());
        menu.Items.Add(CreateCharacterMenuItem("Удалить", () => DeleteCharacter_Click(this, new RoutedEventArgs())));
    }

    private void CharactersGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var node = Graph.SelectedNode;
        if (!CanEditSelectedNodeCharacters(node))
        {
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            EditCharacter_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DuplicateCharacter_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            DeleteCharacter_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Alt)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveSelectedCharacter(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            MoveSelectedCharacter(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            SetSelectedCharacterPosition(CharacterPosition.Left);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            SetSelectedCharacterPosition(CharacterPosition.Center);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            SetSelectedCharacterPosition(CharacterPosition.Right);
            e.Handled = true;
        }
    }

    private static MenuItem CreateCharacterMenuItem(
        string header,
        Action action,
        bool isEnabled = true) =>
        CreateAssetMenuItem(header, action, isEnabled);

    private CharacterPlacement? SelectedCharacter()
    {
        return (CharactersGrid.SelectedItem as CharacterView)?.Character;
    }

    private CharacterView? FindVisibleCharacterView(string idOrName)
    {
        return CharactersGrid.ItemsSource is IEnumerable<CharacterView> characterViews
            ? characterViews.FirstOrDefault(view =>
                view.Id.Equals(idOrName, StringComparison.OrdinalIgnoreCase)
                || view.Name.Equals(idOrName, StringComparison.Ordinal))
            : null;
    }

    private void SelectCharacterView(string characterId)
    {
        var characterView = FindVisibleCharacterView(characterId);
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
        var node = Graph.SelectedNode;
        SetCharacterButtons(CanEditSelectedNodeCharacters(node));
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
        var node = nodeId is null
            ? Graph.SelectedNode
            : _project.FindNode(nodeId);
        if (node?.Kind != NodeKind.Dialogue)
        {
            return;
        }

        var output = _project.AddChoice(node.Id);
        var dialog = new OutputEditorWindow(
            output,
            ProjectScriptVariables.Collect(_project))
        {
            Owner = this,
        };
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

        var dialog = new OutputEditorWindow(
            output,
            ProjectScriptVariables.Collect(_project))
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || !ValidateOutputDialog(dialog))
        {
            return;
        }
        if (!HasOutputEditorChanges(
                output,
                dialog.OutputLabel,
                dialog.Condition,
                dialog.ConditionExpression,
                dialog.Script,
                dialog.ScriptBlocks))
        {
            return;
        }

        var refreshGraph = HasRenderedOutputPropertyChanges(
            output,
            dialog.OutputLabel);
        dialog.ApplyTo(output);
        MarkDirty(refreshGraph);
    }

    private bool ValidateOutputDialog(OutputEditorWindow dialog)
    {
        try
        {
            if (dialog.ConditionExpression is null)
            {
                _ = VisualConditionCompiler.Evaluate(
                    dialog.Condition,
                    new ScriptState());
            }
            else
            {
                VisualConditionCompiler.Validate(dialog.ConditionExpression);
            }
            VisualScriptCompiler.Execute(
                dialog.Script,
                dialog.ScriptBlocks,
                new ScriptState());
            return true;
        }
        catch (InvalidDataException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Ошибка выхода",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private void DeleteOutput_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
        var output = SelectedOutput();
        if (node?.Kind == NodeKind.Dialogue
            && output is not null
            && _project.RemoveOutput(node.Id, output.Id))
        {
            MarkDirty();
        }
    }

    internal static bool HasOutputEditorChanges(
        NodeOutput output,
        string label,
        string condition,
        VisualConditionExpression? conditionExpression,
        string script,
        IReadOnlyList<VisualScriptBlock> scriptBlocks)
    {
        if (!output.Label.Equals(label, StringComparison.Ordinal)
            || !output.Condition.Equals(condition, StringComparison.Ordinal)
            || !output.Script.Equals(script, StringComparison.Ordinal))
        {
            return true;
        }

        var currentCondition = output.ConditionExpression is null
            ? string.Empty
            : VisualConditionCompiler.Compile(output.ConditionExpression);
        var nextCondition = conditionExpression is null
            ? string.Empty
            : VisualConditionCompiler.Compile(conditionExpression);
        if (!currentCondition.Equals(nextCondition, StringComparison.Ordinal))
        {
            return true;
        }

        return !VisualScriptBlocksEqual(output.ScriptBlocks, scriptBlocks);
    }

    internal static bool HasRenderedOutputPropertyChanges(
        NodeOutput output,
        string label) =>
        !output.Label.Equals(label, StringComparison.Ordinal);

    private static bool VisualScriptBlocksEqual(
        IReadOnlyList<VisualScriptBlock> first,
        IReadOnlyList<VisualScriptBlock> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            var left = first[index];
            var right = second[index];
            if (!left.Id.Equals(right.Id, StringComparison.Ordinal)
                || left.Kind != right.Kind
                || !left.VariableName.Equals(right.VariableName, StringComparison.Ordinal)
                || !left.Value.Equals(right.Value, StringComparison.Ordinal)
                || !left.Text.Equals(right.Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void DuplicateOutput_Click(object sender, RoutedEventArgs e)
    {
        var node = Graph.SelectedNode;
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
        var node = Graph.SelectedNode;
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

    private void OutputsGrid_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null)
        {
            OutputsGrid.SelectedItem = null;
            return;
        }

        row.IsSelected = true;
        OutputsGrid.SelectedItem = row.Item;
        row.Focus();
    }

    private void OutputsGrid_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var node = Graph.SelectedNode;
        var output = SelectedOutput();
        var canEditChoices = CanEditOutputList(node);
        var menu = OutputsGrid.ContextMenu ?? new ContextMenu();
        OutputsGrid.ContextMenu = menu;
        menu.Items.Clear();

        if (node is null)
        {
            menu.Items.Add(CreateDisabledAssetMenuItem("Выберите ноду"));
            return;
        }

        if (canEditChoices)
        {
            menu.Items.Add(CreateOutputMenuItem("Добавить вариант", () => AddOutput(node.Id)));
        }
        if (output is null)
        {
            menu.Items.Add(CreateDisabledAssetMenuItem(
                canEditChoices ? "Выберите вариант" : "Выберите переход"));
            return;
        }

        var selectedIndex = OutputsGrid.SelectedIndex;
        var outputCount = OutputsGrid.Items.Count;
        var canEditOutputDetails = CanEditOutputDetails(node, output);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateOutputMenuItem(
            "Изменить",
            () => EditOutput_Click(this, new RoutedEventArgs()),
            canEditOutputDetails));
        menu.Items.Add(CreateOutputMenuItem(
            "Блоки скрипта...",
            () => EditOutputScriptBlocks(node, output),
            canEditOutputDetails));
        if (canEditChoices)
        {
            menu.Items.Add(CreateOutputMenuItem(
                "Дублировать",
                () => DuplicateOutput_Click(this, new RoutedEventArgs())));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateOutputMenuItem(
                "Выше",
                () => MoveSelectedOutput(-1),
                selectedIndex > 0));
            menu.Items.Add(CreateOutputMenuItem(
                "Ниже",
                () => MoveSelectedOutput(1),
                selectedIndex >= 0 && selectedIndex < outputCount - 1));
            menu.Items.Add(new Separator());
        }
        else
        {
            menu.Items.Add(new Separator());
        }
        menu.Items.Add(CreateOutputMenuItem(
            "Переход...",
            () => EditSelectedOutputTransition()));
        menu.Items.Add(CreateOutputMenuItem(
            "Сбросить переход",
            () => ResetSelectedOutputTransition(),
            ShouldShowResetOutputTransition(output)));
        menu.Items.Add(CreateOutputMenuItem(
            "Разорвать переход",
            () => DisconnectOutput_Click(this, new RoutedEventArgs()),
            output.TargetNodeId is not null));
        if (canEditChoices)
        {
            menu.Items.Add(CreateOutputMenuItem(
                "Удалить",
                () => DeleteOutput_Click(this, new RoutedEventArgs())));
        }
    }

    private void OutputsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var node = Graph.SelectedNode;
        var canEditOutputList = CanEditOutputList(node);
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            EditOutput_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (IsOutputListDuplicateShortcut(
            e.Key,
            Keyboard.Modifiers,
            canEditOutputList))
        {
            DuplicateOutput_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (IsOutputListDeleteShortcut(
            e.Key,
            Keyboard.Modifiers,
            canEditOutputList))
        {
            DeleteOutput_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back && Keyboard.Modifiers == ModifierKeys.None)
        {
            DisconnectOutput_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (IsOutputScriptBlocksShortcut(e.Key, Keyboard.Modifiers))
        {
            EditSelectedOutputScriptBlocks();
            e.Handled = true;
            return;
        }

        if (IsOutputTransitionEditShortcut(e.Key, Keyboard.Modifiers))
        {
            EditSelectedOutputTransition();
            e.Handled = true;
            return;
        }

        if (IsOutputTransitionResetShortcut(e.Key, Keyboard.Modifiers))
        {
            ResetSelectedOutputTransition();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Alt || !canEditOutputList)
        {
            return;
        }

        if (IsOutputListMoveUpShortcut(
            e.Key,
            Keyboard.Modifiers,
            canEditOutputList))
        {
            MoveSelectedOutput(-1);
            e.Handled = true;
        }
        else if (IsOutputListMoveDownShortcut(
            e.Key,
            Keyboard.Modifiers,
            canEditOutputList))
        {
            MoveSelectedOutput(1);
            e.Handled = true;
        }
    }

    private static MenuItem CreateOutputMenuItem(
        string header,
        Action action,
        bool isEnabled = true) =>
        CreateAssetMenuItem(header, action, isEnabled);

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
        return (OutputsGrid.SelectedItem as OutputView)?.Output;
    }

    private OutputView? FindVisibleOutputView(string idOrLabel)
    {
        return OutputsGrid.ItemsSource is IEnumerable<OutputView> outputViews
            ? outputViews.FirstOrDefault(view =>
                view.Id.Equals(idOrLabel, StringComparison.OrdinalIgnoreCase)
                || view.Label.Equals(idOrLabel, StringComparison.Ordinal))
            : null;
    }

    private void SelectOutputView(string outputId)
    {
        var outputView = FindVisibleOutputView(outputId);
        if (outputView is null)
        {
            return;
        }

        OutputsGrid.SelectedItem = outputView;
        OutputsGrid.ScrollIntoView(outputView);
    }

    private void OutputsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var node = Graph.SelectedNode;
        SetOutputButtons(
            node?.Kind == NodeKind.Dialogue,
            OutputsGrid.SelectedItem is OutputView);
    }

    private void SetOutputButtons(bool canAdd, bool selected)
    {
        var selectedIndex = OutputsGrid.SelectedIndex;
        var outputCount = OutputsGrid.Items.Count;
        var state = CreateOutputButtonState(
            canAdd,
            selected,
            selectedIndex,
            outputCount);

        AddOutputButton.Visibility = state.ChoiceActionVisibility;
        DuplicateOutputButton.Visibility = state.ChoiceActionVisibility;
        MoveOutputUpButton.Visibility = state.ChoiceActionVisibility;
        MoveOutputDownButton.Visibility = state.ChoiceActionVisibility;
        DeleteOutputButton.Visibility = state.ChoiceActionVisibility;

        AddOutputButton.IsEnabled = state.CanAddChoice;
        EditOutputButton.IsEnabled = state.CanEditOutput;
        DuplicateOutputButton.IsEnabled = state.CanDuplicateChoice;
        MoveOutputUpButton.IsEnabled = state.CanMoveChoiceUp;
        MoveOutputDownButton.IsEnabled = state.CanMoveChoiceDown;
        EditTransitionButton.IsEnabled = state.CanEditTransition;
        DeleteOutputButton.IsEnabled = state.CanDeleteChoice;
        DisconnectOutputButton.IsEnabled = state.CanDisconnectOutput;
    }

    private void EditSelectedOutputTransition_Click(object sender, RoutedEventArgs e) =>
        EditSelectedOutputTransition();

    internal static OutputButtonState CreateOutputButtonState(
        bool canEditOutputList,
        bool selected,
        int selectedIndex,
        int outputCount)
    {
        var choiceActionVisibility = canEditOutputList
            ? Visibility.Visible
            : Visibility.Collapsed;
        return new OutputButtonState(
            choiceActionVisibility,
            CanAddChoice: canEditOutputList,
            CanEditOutput: selected,
            CanDuplicateChoice: canEditOutputList && selected,
            CanMoveChoiceUp: canEditOutputList && selected && selectedIndex > 0,
            CanMoveChoiceDown: canEditOutputList
                && selected
                && selectedIndex >= 0
                && selectedIndex < outputCount - 1,
            CanEditTransition: selected,
            CanDeleteChoice: canEditOutputList && selected,
            CanDisconnectOutput: selected);
    }

    internal static bool CanEditOutputList(NovelNode? node) =>
        node?.Kind == NodeKind.Dialogue;

    internal static bool IsOutputListDuplicateShortcut(
        Key key,
        ModifierKeys modifiers,
        bool canEditOutputList) =>
        canEditOutputList
        && key == Key.D
        && modifiers == ModifierKeys.Control;

    internal static bool IsOutputListDeleteShortcut(
        Key key,
        ModifierKeys modifiers,
        bool canEditOutputList) =>
        canEditOutputList
        && key == Key.Delete
        && modifiers == ModifierKeys.None;

    internal static bool IsOutputListMoveUpShortcut(
        Key key,
        ModifierKeys modifiers,
        bool canEditOutputList) =>
        canEditOutputList
        && key == Key.Up
        && modifiers == ModifierKeys.Alt;

    internal static bool IsOutputListMoveDownShortcut(
        Key key,
        ModifierKeys modifiers,
        bool canEditOutputList) =>
        canEditOutputList
        && key == Key.Down
        && modifiers == ModifierKeys.Alt;

    internal static bool IsOutputScriptBlocksShortcut(
        Key key,
        ModifierKeys modifiers) =>
        key == Key.B && modifiers == ModifierKeys.Control;

    internal static bool IsOutputTransitionEditShortcut(
        Key key,
        ModifierKeys modifiers) =>
        key == Key.T && modifiers == ModifierKeys.Control;

    internal static bool IsOutputTransitionResetShortcut(
        Key key,
        ModifierKeys modifiers) =>
        key == Key.R && modifiers == ModifierKeys.Control;

    internal static bool CanEditOutputDetails(
        NovelNode? node,
        NodeOutput? output) =>
        node is not null
        && output is not null
        && node.Outputs.Any(candidate => candidate.Id == output.Id);

    private void EditSelectedOutputScriptBlocks()
    {
        var node = Graph.SelectedNode;
        var output = SelectedOutput();
        if (!CanEditOutputDetails(node, output))
        {
            return;
        }

        EditOutputScriptBlocks(node!, output!);
    }

    private void EditSelectedOutputTransition()
    {
        var node = Graph.SelectedNode;
        var output = SelectedOutput();
        if (!CanEditSelectedOutputTransition(node, output))
        {
            return;
        }

        EditTransition(node!.Id, output!.Id);
    }

    private void ResetSelectedOutputTransition()
    {
        var output = SelectedOutput();
        if (!ShouldShowResetOutputTransition(output))
        {
            return;
        }

        output!.TransitionSound = string.Empty;
        output.FadeDurationMs = 350;
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
    }

    internal static bool ShouldShowResetOutputTransition(NodeOutput? output) =>
        output is not null
        && (output.TransitionSound.Length > 0 || output.FadeDurationMs != 350);

    internal static bool HasOutputTransitionChanges(
        NodeOutput output,
        string transitionSound,
        int fadeDurationMs) =>
        !output.TransitionSound.Equals(transitionSound, StringComparison.Ordinal)
        || output.FadeDurationMs != fadeDurationMs;

    internal static bool CanEditSelectedOutputTransition(
        NovelNode? node,
        NodeOutput? output) =>
        node is not null
        && output is not null
        && node.Outputs.Any(candidate => candidate.Id == output.Id);

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
        var transitionSound = NormalizeAssetPath(dialog.TransitionSound);
        if (!HasOutputTransitionChanges(
                output,
                transitionSound,
                dialog.FadeDurationMs))
        {
            return;
        }

        output.TransitionSound = transitionSound;
        output.FadeDurationMs = dialog.FadeDurationMs;
        MarkDirty(refreshGraph: false);
        RefreshAssetUsageAfterBinding();
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
        MarkDirty(refreshGraph: false);
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
        MarkDirty(refreshGraph: false);
        StatusText.Text = "Главное меню обновлено";
    }

    private string GetAssetDirectory() =>
        _projectPath is null
            ? _workspaceDirectory ?? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(_projectPath))
                ?? Environment.CurrentDirectory;

    private void AutoSaveProject()
    {
        if (!ShouldRunAutoSave(
                hasProjectPath: _projectPath is not null,
                hasWorkspaceDirectory: _workspaceDirectory is not null,
                dirty: _dirty,
                workspaceNeedsProjectFile: _workspaceNeedsProjectFile,
                codeHasPendingChanges: _codeHasPendingChanges))
        {
            return;
        }

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

    internal static bool ShouldRunAutoSave(
        bool hasProjectPath,
        bool hasWorkspaceDirectory,
        bool dirty,
        bool workspaceNeedsProjectFile,
        bool codeHasPendingChanges)
    {
        if (workspaceNeedsProjectFile)
        {
            return hasWorkspaceDirectory;
        }

        return hasProjectPath && (dirty || codeHasPendingChanges);
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
        var report = GetProjectDiagnostics();
        var stamp = CreateDiagnosticPanelStamp(report, showPanel);
        if (_diagnosticsPanelStamp == stamp && DiagnosticsGrid.ItemsSource is not null)
        {
            return report;
        }

        _diagnosticsPanelStamp = stamp;
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

    private ProjectDiagnosticReport GetProjectDiagnostics() =>
        _projectDiagnosticsCache ??= ProjectDiagnostics.Analyze(_project, _projectPath);

    internal static DiagnosticPanelStamp CreateDiagnosticPanelStamp(
        ProjectDiagnosticReport report,
        bool showPanel)
    {
        return new DiagnosticPanelStamp(
            showPanel,
            report.ErrorCount,
            report.WarningCount,
            report.InfoCount,
            report.Diagnostics.Count,
            report.Fingerprint);
    }

    private void HideDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        _diagnosticsPanelStamp = null;
        DiagnosticsPanel.Visibility = Visibility.Collapsed;
    }

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
        var assetView = FindVisibleAssetView(assetId);
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
        if (TryNavigateToDiagnosticVisualBlocks(location, node))
        {
            return true;
        }
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

    private bool TryNavigateToDiagnosticVisualBlocks(
        string location,
        NovelNode node)
    {
        if (!location.Contains("visual blocks", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        WorkspaceTabs.SelectedItem = GraphTab;
        if (location.Contains("входа", StringComparison.OrdinalIgnoreCase))
        {
            EditNodeScriptBlocks(node);
            return true;
        }

        var label = ReadQuotedSegmentAfter(location, "visual blocks ");
        var output = label is null
            ? null
            : node.Outputs.FirstOrDefault(output =>
                output.Label.Equals(label, StringComparison.Ordinal));
        if (output is null)
        {
            StatusText.Text =
                $"Выбрана нода «{DiagnosticNodeDisplay(node)}», вариант для visual blocks не найден";
            return true;
        }

        EditOutputScriptBlocks(node, output);
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

    private static string? ReadQuotedSegmentAfter(string source, string marker)
    {
        var markerStart = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerStart < 0)
        {
            return null;
        }

        var start = source.IndexOf('«', markerStart + marker.Length);
        if (start < 0)
        {
            return null;
        }

        var end = source.IndexOf('»', start + 1);
        return end < 0 ? null : source[(start + 1)..end];
    }

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
        public string TransitionSoundLabel =>
            Output.TransitionSound.Length == 0 ? "-" : Output.TransitionSound;
        public string FadeDurationLabel =>
            Output.FadeDurationMs == 350 ? "-" : $"{Output.FadeDurationMs} мс";
        public int BlockCount => Output.ScriptBlocks.Count;
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

    private readonly record struct NodeAssetChoiceCacheKey(
        AssetKind Kind,
        string Folder);

    internal readonly record struct AssetListStamp(
        string? Folder,
        string Query,
        int AssetCount,
        int Hash);

    internal readonly record struct AssetFolderTreeStamp(
        string? SelectedFolder,
        int FolderCount,
        int AssetCount,
        int Hash);

    internal readonly record struct AssetPreviewStamp(
        string? AssetId,
        AssetKind Kind,
        string Path,
        int UsageCount);

    internal readonly record struct ProjectExplorerStamp(
        string Query,
        int NodeCount,
        int Hash);

    internal readonly record struct ProjectExplorerNodeGroups(
        IReadOnlyList<NovelNode> StartNodes,
        IReadOnlyList<NovelNode> SceneNodes,
        IReadOnlyList<NovelNode> DialogueNodes)
    {
        public int TotalCount =>
            StartNodes.Count + SceneNodes.Count + DialogueNodes.Count;
    }

    internal readonly record struct NodePropertyPanelStamp(
        string? NodeId,
        int Hash);

    internal readonly record struct OutputButtonState(
        Visibility ChoiceActionVisibility,
        bool CanAddChoice,
        bool CanEditOutput,
        bool CanDuplicateChoice,
        bool CanMoveChoiceUp,
        bool CanMoveChoiceDown,
        bool CanEditTransition,
        bool CanDeleteChoice,
        bool CanDisconnectOutput);

    internal readonly record struct DiagnosticPanelStamp(
        bool ShowPanel,
        int ErrorCount,
        int WarningCount,
        int InfoCount,
        int DiagnosticCount,
        int Hash);

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
