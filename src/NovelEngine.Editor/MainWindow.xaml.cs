using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    public MainWindow()
    {
        InitializeComponent();

        Graph.SelectionChanged += (_, _) => HandleGraphSelection();
        Graph.ProjectChanged += (_, _) => MarkDirty();
        Graph.AddChoiceRequested += nodeId => AddOutput(nodeId);
        Graph.PreviewNodeRequested += PreviewNode;
        Graph.TransitionSettingsRequested += EditTransition;
        Closing += MainWindow_Closing;
        Loaded += (_, _) => Graph.CenterGraph();

        SetProject(_project, null);
    }

    internal void SelectPreviewNode(NodeKind kind)
    {
        var node = _project.Nodes.FirstOrDefault(candidate => candidate.Kind == kind);
        Graph.SelectNode(node?.Id);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            RunProject();
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
    }

    private static bool IsTextEditing() =>
        Keyboard.FocusedElement is TextBox;

    private void SetProject(NovelProject project, string? path)
    {
        _project = project;
        _projectPath = path;
        _dirty = false;
        Graph.SetProject(project);
        RefreshExplorer();
        RefreshProperties();
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

        node.Title = title;
        node.Speaker = node.Kind == NodeKind.Dialogue ? SpeakerBox.Text.Trim() : string.Empty;
        node.Text = BodyTextBox.Text;
        node.InheritBackground = node.Kind != NodeKind.Start
            && InheritBackgroundCheck.IsChecked == true;
        node.Background = BackgroundBox.Text.Trim();
        node.InheritMusic = node.Kind != NodeKind.Start
            && InheritMusicCheck.IsChecked == true;
        node.Music = MusicBox.Text.Trim();
        node.InheritCharacters = node.Kind != NodeKind.Start
            && InheritCharactersCheck.IsChecked == true;
        node.Script = ScriptBox.Text.Trim();
        Graph.RefreshGraph();
        MarkDirty();
        return true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        RefreshWindowTitle();
        RefreshExplorer();
        RefreshProperties();
        Graph.RefreshGraph();
        StatusText.Text = "Проект изменён";
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
            SetProject(ProjectSerializer.Load(dialog.FileName), dialog.FileName);
        }
        catch (Exception error) when (
            error is IOException
            or InvalidDataException
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

    private void SaveProject_Click(object sender, RoutedEventArgs e) => SaveProject();

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e) => SaveProjectAs();

    private bool SaveProject()
    {
        if (_projectPath is null)
        {
            return SaveProjectAs();
        }
        return WriteProject(_projectPath);
    }

    private bool SaveProjectAs()
    {
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
        }
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
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = $"{filter}|Все файлы|*.*",
        };
        return dialog.ShowDialog(this) == true
            ? MakeProjectRelative(dialog.FileName)
            : null;
    }

    private string MakeProjectRelative(string path)
    {
        if (_projectPath is null)
        {
            return path;
        }
        var directory = Path.GetDirectoryName(_projectPath);
        return directory is null ? path : Path.GetRelativePath(directory, path);
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
        InheritCharactersCheck.IsChecked = false;
        node.Characters.Add(
            new CharacterPlacement
            {
                Id = $"character-{Guid.NewGuid():N}",
                Name = dialog.CharacterName,
                Sprite = NormalizeAssetPath(dialog.Sprite),
                Position = dialog.Position,
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
        character.Position = dialog.Position;
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

    private void RunProject_Click(object sender, RoutedEventArgs e) => RunProject();

    private void PreviewNode_Click(object sender, RoutedEventArgs e) =>
        PreviewNode(Graph.SelectedNodeId);

    private void RunProject()
    {
        if (!ApplyProperties())
        {
            return;
        }
        new PreviewWindow(_project, null, GetAssetDirectory()) { Owner = this }.ShowDialog();
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
        if (!ApplyProperties())
        {
            return;
        }
        new PreviewWindow(_project, nodeId, GetAssetDirectory()) { Owner = this }.ShowDialog();
    }

    private string GetAssetDirectory() =>
        _projectPath is null
            ? Environment.CurrentDirectory
            : Path.GetDirectoryName(Path.GetFullPath(_projectPath))
                ?? Environment.CurrentDirectory;

    private string NormalizeAssetPath(string path) =>
        path.Length == 0 || !Path.IsPathRooted(path)
            ? path
            : MakeProjectRelative(path);

    private void ValidateProject_Click(object sender, RoutedEventArgs e)
    {
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
}
