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
    private NovelProject _project = NovelProject.CreateDefault();
    private string? _projectPath;
    private bool _dirty;
    private bool _syncingSelection;
    private bool _refreshingProperties;
    private bool _syncingCode;
    private bool _codeHasPendingChanges;
    private bool _refreshingAssetFolders;
    private bool _buildInProgress;
    private string? _selectedAssetFolder;
    private Process? _gameProcess;
    private readonly DispatcherTimer _codeAnalysisTimer;

    public MainWindow()
        : this(null)
    {
    }

    public MainWindow(string? startupProjectPath)
    {
        InitializeComponent();
        CodeEditor.CompletionProvider = ProjectLanguage.GetCompletions;

        Graph.SelectionChanged += (_, _) => HandleGraphSelection();
        Graph.ProjectChanged += (_, _) => MarkDirty();
        Graph.AddChoiceRequested += nodeId => AddOutput(nodeId);
        Graph.PreviewNodeRequested += PreviewNode;
        Graph.EditNodeSceneRequested += EditNodeScene;
        Graph.EditMainMenuRequested += EditMainMenu;
        Graph.OpenNodeCodeRequested += NavigateToNodeCode;
        Graph.TransitionSettingsRequested += EditTransition;
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
        else if (e.Key == Key.Delete && !IsTextEditing())
        {
            Graph.DeleteSelected();
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

    private void SetProject(NovelProject project, string? path)
    {
        _project = project;
        _projectPath = path;
        _dirty = false;
        _selectedAssetFolder = null;
        Graph.SetProject(project);
        RefreshExplorer();
        RefreshProperties();
        RefreshAssets();
        RefreshCodeFromProject(useStoredSource: true);
        RefreshWindowTitle();
        StatusText.Text = path is null ? "Новый проект" : $"Открыт {Path.GetFileName(path)}";
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
            ProjectTree.Items.Clear();
            var root = new TreeViewItem
            {
                Header = _project.Title,
                IsExpanded = true,
            };
            ProjectTree.Items.Add(root);
            AddNodeGroup(root, "Старт", NodeKind.Start);
            AddNodeGroup(root, "Сцены", NodeKind.Scene);
            AddNodeGroup(root, "Диалоги", NodeKind.Dialogue);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void AddNodeGroup(TreeViewItem root, string title, NodeKind kind)
    {
        var group = new TreeViewItem
        {
            Header = title,
            IsExpanded = true,
        };
        root.Items.Add(group);
        foreach (var node in _project.Nodes.Where(node => node.Kind == kind))
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
        yield return InheritBackgroundCheck;
        yield return MusicBox;
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
        _dirty = true;
        if (!_codeHasPendingChanges)
        {
            RefreshCodeFromProject(useStoredSource: false);
        }
        RefreshWindowTitle();
        RefreshExplorer();
        RefreshProperties();
        Graph.RefreshGraph();
        StatusText.Text = "Проект изменён";
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
            var compiled = ProjectLanguage.Parse(source);

            _project = compiled;
            _project.SourceCode = source;
            _codeHasPendingChanges = false;
            Graph.SetProject(_project);
            if (_project.FindNode(selectedNodeId) is not null)
            {
                Graph.SelectNode(selectedNodeId);
            }
            RefreshExplorer();
            RefreshProperties();
            RefreshAssets();
            _dirty = true;
            RefreshWindowTitle();
            CodeStatusText.Foreground = (Brush)FindResource("AccentBrush");
            CodeStatusText.Text =
                $"Код применён: {_project.NodeTypes.Count} типов, {_project.Nodes.Count} нод";
            ApplyCodeHighlighting(source, null);
            StatusText.Text = "Код скомпилирован, граф обновлён";
            Graph.CenterGraph();
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
        var source = CodeEditor.SourceText;
        var offset = Math.Clamp(CodeEditor.SourceCaretOffset, 0, source.Length);
        var line = 1;
        var lineStart = 0;
        for (var index = 0; index < offset; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }
        CodeCursorText.Text =
            $"Строка {line}, столбец {offset - lineStart + 1}";
    }

    private void AnalyzeCode()
    {
        var source = CodeEditor.SourceText;
        try
        {
            _ = ProjectLanguage.Parse(source);
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
        var wasSyncing = _syncingCode;
        _syncingCode = true;
        try
        {
            CodeEditor.ApplySyntax(
                ProjectLanguage.GetSyntaxSpans(source),
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
        if (!_codeHasPendingChanges)
        {
            RefreshCodeFromProject(useStoredSource: false);
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
            RefreshCodeFromProject(useStoredSource: false);
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

    private void RefreshAssets()
    {
        RefreshAssetFolders();
        RefreshAssetList();
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
        AssetsGrid.ItemsSource = assets
            .OrderBy(asset => asset.Kind)
            .ThenBy(asset => asset.Id, StringComparer.CurrentCultureIgnoreCase)
            .Select(
                asset => new AssetView(
                    asset,
                    _project.CountAssetReferences(asset.Id),
                    AssetSize(asset)))
            .ToList();
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

    private void RefreshAssetFolders()
    {
        _refreshingAssetFolders = true;
        try
        {
            AssetFoldersTree.Items.Clear();
            var all = new TreeViewItem
            {
                Header = $"Все файлы ({_project.Assets.Count})",
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
                            Header = $"{segment} ({count})",
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

    private string AssetSize(NovelAsset asset)
    {
        var path = ResolveAssetPath(asset);
        if (!File.Exists(path))
        {
            return "нет файла";
        }
        var bytes = new FileInfo(path).Length;
        return bytes switch
        {
            >= 1024L * 1024L =>
                $"{bytes / (1024d * 1024d):0.##} МБ",
            >= 1024L => $"{bytes / 1024d:0.##} КБ",
            _ => $"{bytes} Б",
        };
    }

    private void RefreshAssetPreview()
    {
        var view = AssetsGrid.SelectedItem as AssetView;
        var selected = view is not null;
        RenameAssetButton.IsEnabled = selected;
        MoveAssetButton.IsEnabled = selected;
        CopyAssetReferenceButton.IsEnabled = selected;
        DeleteAssetButton.IsEnabled = selected;
        AssetPreviewImage.Source = null;
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
        catch (IOException error)
        {
            MessageBox.Show(
                this,
                $"Не удалось импортировать ассет:\n\n{error.Message}",
                "Импорт файлов",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CreateAssetFolder_Click(object sender, RoutedEventArgs e)
    {
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
        catch (IOException error)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Перемещение ассета",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string ImportAssetFile(string sourcePath)
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
            _selectedAssetFolder);
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
        var assetsDirectory = Path.GetFullPath(
            Path.Combine(projectDirectory, "assets"))
            + Path.DirectorySeparatorChar;
        var path = ResolveAssetPath(asset);
        if (path.StartsWith(
                assetsDirectory,
                StringComparison.OrdinalIgnoreCase)
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
        var directory = Path.Combine(GetAssetDirectory(), "assets");
        Directory.CreateDirectory(directory);
        Process.Start(
            new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
            });
    }

    private bool EnsureProjectSavedForAssets()
    {
        if (!EnsureCodeApplied())
        {
            return false;
        }
        if (_projectPath is not null)
        {
            return true;
        }
        MessageBox.Show(
            this,
            "Сначала сохраните проект. Ассеты будут скопированы в папку assets рядом с ним.",
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
        var name = _projectPath is null ? "Без имени" : Path.GetFileName(_projectPath);
        Title = $"{name}{(_dirty ? " *" : string.Empty)} — Novel Engine WPF";
    }

    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmDiscardChanges())
        {
            SetProject(NovelProject.CreateDefault(), null);
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Открыть проект новеллы",
            Filter = "Проект Novel Engine|*.novel.json|JSON|*.json|Все файлы|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            OpenProject(dialog.FileName);
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
        var projectPath = ProjectOpenResolver.ResolveProjectPath(inputPath);
        SetProject(ProjectSerializer.Load(projectPath), projectPath);
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject();

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e) => SaveProjectAs();

    private bool SaveProject()
    {
        if (!EnsureCodeApplied())
        {
            return false;
        }
        if (_projectPath is null)
        {
            return SaveProjectAs();
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
        };
        return dialog.ShowDialog(this) == true && WriteProject(dialog.FileName);
    }

    private bool WriteProject(string path)
    {
        try
        {
            ProjectSerializer.Save(_project, path);
            _projectPath = path;
            _dirty = false;
            RefreshWindowTitle();
            StatusText.Text = $"Сохранён {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            MessageBox.Show(
                this,
                $"Не удалось сохранить проект:\n\n{error.Message}",
                "Сохранение проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
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
        StopGameProcess(silent: true);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void AddScene_Click(object sender, RoutedEventArgs e) =>
        Graph.AddNodeAtCenter(NodeKind.Scene);

    private void AddDialogue_Click(object sender, RoutedEventArgs e) =>
        Graph.AddNodeAtCenter(NodeKind.Dialogue);

    private void DeleteNode_Click(object sender, RoutedEventArgs e) =>
        Graph.DeleteSelected();

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
        MusicBox.IsEnabled = node.Kind == NodeKind.Start
            || InheritMusicCheck.IsChecked != true;
        SetCharacterButtons(
            node.Kind == NodeKind.Start || InheritCharactersCheck.IsChecked != true);
    }

    private void BrowseBackground_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseAsset("Выберите фон", "Изображения|*.png;*.jpg;*.jpeg;*.webp;*.bmp");
        if (path is not null)
        {
            InheritBackgroundCheck.IsChecked = false;
            BackgroundBox.Text = path;
        }
    }

    private void BrowseMusic_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseAsset("Выберите музыку", "Аудио|*.mp3;*.wav;*.wma;*.aac;*.m4a");
        if (path is not null)
        {
            InheritMusicCheck.IsChecked = false;
            MusicBox.Text = path;
        }
    }

    private string? BrowseAsset(string title, string filter)
    {
        if (!EnsureProjectSavedForAssets())
        {
            return null;
        }
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{filter}|Все файлы|*.*",
            InitialDirectory = Path.Combine(GetAssetDirectory(), "assets"),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }
        try
        {
            var count = _project.Assets.Count;
            var reference = ImportAssetFile(dialog.FileName);
            if (_project.Assets.Count != count)
            {
                MarkDirty();
                RefreshAssets();
            }
            return reference;
        }
        catch (IOException error)
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

        var dialog = new CharacterEditorWindow(null, GetAssetDirectory()) { Owner = this };
        if (dialog.ShowDialog() != true)
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
        node.Characters.Add(
            new CharacterPlacement
            {
                Id = $"character-{Guid.NewGuid():N}",
                Name = dialog.CharacterName,
                Sprite = NormalizeAssetPath(dialog.Sprite),
                Position = dialog.Position,
                VoiceSound = NormalizeAssetPath(dialog.VoiceSound),
                VoicePitch = dialog.VoicePitch,
                VoiceEveryNthCharacter = dialog.VoiceEveryNthCharacter,
            });
        MarkDirty();
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        var character = SelectedCharacter();
        if (character is null)
        {
            return;
        }

        var dialog = new CharacterEditorWindow(character, GetAssetDirectory()) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        character.Name = dialog.CharacterName;
        character.Sprite = NormalizeAssetPath(dialog.Sprite);
        character.VoiceSound = NormalizeAssetPath(dialog.VoiceSound);
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

    private CharacterPlacement? SelectedCharacter()
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        var view = CharactersGrid.SelectedItem as CharacterView;
        return node?.Characters.FirstOrDefault(character => character.Id == view?.Id);
    }

    private void CharactersGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        SetCharacterButtons(
            node is not null
            && (node.Kind == NodeKind.Start || InheritCharactersCheck.IsChecked != true));
    }

    private void SetCharacterButtons(bool canEdit)
    {
        CharactersGrid.IsEnabled = canEdit;
        AddCharacterButton.IsEnabled = canEdit;
        EditCharacterButton.IsEnabled = canEdit && CharactersGrid.SelectedItem is CharacterView;
        DeleteCharacterButton.IsEnabled = canEdit && CharactersGrid.SelectedItem is CharacterView;
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

    private void OutputsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var node = _project.FindNode(Graph.SelectedNodeId);
        SetOutputButtons(
            node?.Kind == NodeKind.Dialogue,
            OutputsGrid.SelectedItem is OutputView);
    }

    private void SetOutputButtons(bool canAdd, bool selected)
    {
        AddOutputButton.IsEnabled = canAdd;
        EditOutputButton.IsEnabled = selected;
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
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(_projectPath))
                ?? Environment.CurrentDirectory;

    private string NormalizeAssetPath(string path)
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
            return ImportAssetFile(path);
        }
        catch (IOException error)
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
        try
        {
            _project.Validate();
            foreach (var node in _project.Nodes)
            {
                NovelScript.Execute(node.Script, new ScriptState());
                foreach (var output in node.Outputs)
                {
                    _ = NovelScript.Evaluate(output.Condition, new ScriptState());
                    NovelScript.Execute(output.Script, new ScriptState());
                }
            }

            var disconnected = _project.Nodes
                .SelectMany(node => node.Outputs)
                .Count(output => output.TargetNodeId is null);
            MessageBox.Show(
                this,
                disconnected == 0
                    ? "Ошибок не найдено."
                    : $"Структура корректна. Неподключённых выходов: {disconnected}.",
                "Проверка проекта",
                MessageBoxButton.OK,
                disconnected == 0
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Warning);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                error.Message,
                "Ошибка проекта",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string KindName(NodeKind kind) =>
        kind switch
        {
            NodeKind.Start => "Старт",
            NodeKind.Scene => "Сцена",
            NodeKind.Dialogue => "Диалог",
            _ => kind.ToString(),
        };

    private sealed record CharacterView(CharacterPlacement Character)
    {
        public string Id => Character.Id;
        public string Name => Character.Name;
        public string Sprite => Character.Sprite;
        public string Position => Character.Position switch
        {
            CharacterPosition.Left => "Слева",
            CharacterPosition.Right => "Справа",
            _ => "По центру",
        };
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
        public string Folder => Asset.Folder;
        public string Path => Asset.Path;
    }
}
