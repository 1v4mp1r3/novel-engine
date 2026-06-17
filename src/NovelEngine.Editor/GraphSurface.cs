using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelEngine.Core;

namespace NovelEngine.Editor;

public sealed class GraphSurface : FrameworkElement
{
    private const double NodeWidth = 220;
    private const double HeaderHeight = 40;
    private const double OutputRowHeight = 29;
    private const double PortRadius = 7;
    private const double GridSize = 32;
    private const double HoverHitCacheDistance = 4;
    private const double DragRenderEpsilon = 0.5;

    private static readonly Typeface NodeTypeface = new("Segoe UI");
    private static readonly Brush SurfaceBrush = FrozenBrush(15, 22, 31);
    private static readonly Brush GridBrush = FrozenBrush(26, 36, 49);
    private static readonly Brush NodeBodyBrush = FrozenBrush(25, 35, 48);
    private static readonly Brush StartHeaderBrush = FrozenBrush(68, 91, 126);
    private static readonly Brush SceneHeaderBrush = FrozenBrush(47, 94, 89);
    private static readonly Brush DialogueHeaderBrush = FrozenBrush(83, 65, 113);
    private static readonly Brush FallbackHeaderBrush = FrozenBrush(45, 57, 73);
    private static readonly Brush MutedTextBrush = FrozenBrush(113, 129, 150);
    private static readonly Brush PreviewTextBrush = FrozenBrush(184, 198, 214);
    private static readonly Brush OutputTextBrush = FrozenBrush(190, 203, 218);
    private static readonly Brush PortActiveBrush = FrozenBrush(100, 218, 183);
    private static readonly Brush PortInactiveBrush = FrozenBrush(113, 129, 150);
    private static readonly Pen GridPen = FrozenPen(GridBrush, 1);
    private static readonly Pen ConnectionPen = FrozenPen(PortActiveBrush, 2.4);
    private static readonly Pen SelectedNodePen = FrozenPen(FrozenBrush(245, 190, 80), 2.2);
    private static readonly Pen NodePen = FrozenPen(FrozenBrush(58, 75, 96), 1.2);
    private static readonly Pen PortOutlinePen = FrozenPen(SurfaceBrush, 2);
    private static readonly Pen ConnectionHitPen = FrozenPen(Brushes.Transparent, 12);
    private static readonly Pen DragConnectionPen =
        FrozenPen(FrozenBrush(245, 190, 80), 2.4, DashStyles.Dash);

    private readonly List<ConnectionVisual> _connections = [];
    private readonly Dictionary<string, NovelNode> _nodesById = [];
    private Vector _viewOffset = new(80, 80);
    private string? _dragNodeId;
    private Point _dragStart;
    private Point _dragNodeStart;
    private bool _dragMoved;
    private bool _panning;
    private Point _panStart;
    private Vector _panOffsetStart;
    private ConnectionDrag? _connectionDrag;
    private Point _contextWorldPosition;
    private bool _needsInitialCenter = true;
    private bool _renderQueued;
    private Point _lastHoverHitPoint = new(double.NaN, double.NaN);
    private Cursor? _lastHoverCursor;
    private double _pixelsPerDip = 1;

    public GraphSurface()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
    }

    public NovelProject Project { get; private set; } = NovelProject.CreateDefault();
    public string? SelectedNodeId { get; private set; }

    public event EventHandler? SelectionChanged;
    public event EventHandler? ProjectChanged;
    public event Action<string>? AddChoiceRequested;
    public event Action<string>? PreviewNodeRequested;
    public event Action<string>? EditNodeSceneRequested;
    public event Action? EditMainMenuRequested;
    public event Action<string>? OpenNodeCodeRequested;
    public event Action<string, string>? TransitionSettingsRequested;

    public void SetProject(NovelProject project)
    {
        Project = project;
        SelectedNodeId = null;
        _needsInitialCenter = true;
        RebuildNodeLookup();
        RequestRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SelectNode(string? nodeId)
    {
        if (nodeId is not null && Project.FindNode(nodeId) is null)
        {
            nodeId = null;
        }
        if (SelectedNodeId == nodeId)
        {
            return;
        }

        SelectedNodeId = nodeId;
        RequestRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public NovelNode AddNodeAtCenter(NodeKind kind)
    {
        var world = ScreenToWorld(new Point(ActualWidth / 2, ActualHeight / 2));
        return AddNodeAt(world, kind);
    }

    public NovelNode AddNodeTemplateAtCenter(NodeTemplateKind template)
    {
        var world = ScreenToWorld(new Point(ActualWidth / 2, ActualHeight / 2));
        return AddNodeTemplateAt(world, template);
    }

    public void AddConnectedSceneFromSelected() =>
        AddConnectedNodeFromSelected(NodeKind.Scene);

    public void AddConnectedDialogueFromSelected() =>
        AddConnectedNodeFromSelected(NodeKind.Dialogue);

    public void DeleteSelected()
    {
        if (SelectedNodeId is null || !Project.RemoveNode(SelectedNodeId))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        SelectedNodeId = null;
        RebuildNodeLookup();
        RequestRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DuplicateSelected()
    {
        if (SelectedNodeId is null)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        try
        {
            var duplicate = Project.DuplicateNode(SelectedNodeId);
            SelectedNodeId = duplicate.Id;
            RebuildNodeLookup();
            RequestRender();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void RefreshGraph()
    {
        RebuildNodeLookup();
        RequestRender();
    }

    public void CenterGraph()
    {
        if (Project.Nodes.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var left = Project.Nodes.Min(node => node.X);
        var right = Project.Nodes.Max(node => node.X + NodeWidth);
        var top = Project.Nodes.Min(node => node.Y);
        var bottom = Project.Nodes.Max(node => node.Y + GetNodeHeight(node));
        _viewOffset = new Vector(
            ActualWidth / 2 - (left + right) / 2,
            ActualHeight / 2 - (top + bottom) / 2);
        _needsInitialCenter = false;
        RequestRender();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (_needsInitialCenter && ActualWidth > 0 && ActualHeight > 0)
        {
            CenterGraph();
        }
        drawingContext.DrawRectangle(
            SurfaceBrush,
            null,
            new Rect(RenderSize));
        DrawGrid(drawingContext);

        _connections.Clear();
        var viewport = new Rect(RenderSize);
        DrawConnections(drawingContext, viewport);
        foreach (var node in Project.Nodes)
        {
            if (!IntersectsViewport(GetNodeRectangle(node), viewport))
            {
                continue;
            }
            DrawNode(drawingContext, node);
        }

        if (_connectionDrag is not null)
        {
            var source = GetOutputPort(_connectionDrag.SourceNodeId, _connectionDrag.OutputId);
            if (source is not null)
            {
                DrawCurve(
                    drawingContext,
                    source.Value.Center,
                    _connectionDrag.Cursor,
                    DragConnectionPen);
            }
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var position = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = true;
            _panStart = position;
            _panOffsetStart = _viewOffset;
            Cursor = Cursors.ScrollAll;
            CaptureMouse();
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            ShowContextMenu(position);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var outputPort = HitOutputPort(position);
        if (outputPort is not null)
        {
            SelectNode(outputPort.Value.NodeId);
            _connectionDrag = new ConnectionDrag(
                outputPort.Value.NodeId,
                outputPort.Value.OutputId,
                position);
            CaptureMouse();
            RequestRender();
            return;
        }

        var node = HitNode(position);
        SelectNode(node?.Id);
        if (node is null)
        {
            return;
        }
        if (e.ClickCount >= 2)
        {
            if (node.Kind == NodeKind.Start)
            {
                EditMainMenuRequested?.Invoke();
            }
            else
            {
                OpenNodeCodeRequested?.Invoke(node.Id);
            }
            e.Handled = true;
            return;
        }

        _dragNodeId = node.Id;
        _dragStart = position;
        _dragNodeStart = new Point(node.X, node.Y);
        _dragMoved = false;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);

        if (_panning)
        {
            var nextOffset = _panOffsetStart + (position - _panStart);
            if (NearlyEqual(nextOffset.X, _viewOffset.X)
                && NearlyEqual(nextOffset.Y, _viewOffset.Y))
            {
                return;
            }
            _viewOffset = nextOffset;
            RequestRender();
            return;
        }

        if (_connectionDrag is not null)
        {
            if (NearlyEqual(_connectionDrag.Cursor.X, position.X)
                && NearlyEqual(_connectionDrag.Cursor.Y, position.Y))
            {
                return;
            }
            _connectionDrag = _connectionDrag with { Cursor = position };
            RequestRender();
            return;
        }

        if (_dragNodeId is null)
        {
            UpdateHoverCursor(position);
            return;
        }

        var node = Project.FindNode(_dragNodeId);
        if (node is null)
        {
            return;
        }

        var nextX = (float)(_dragNodeStart.X + position.X - _dragStart.X);
        var nextY = (float)(_dragNodeStart.Y + position.Y - _dragStart.Y);
        if (NearlyEqual(node.X, nextX) && NearlyEqual(node.Y, nextY))
        {
            return;
        }

        node.X = nextX;
        node.Y = nextY;
        _dragMoved = node.X != _dragNodeStart.X || node.Y != _dragNodeStart.Y;
        RequestRender();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        var position = e.GetPosition(this);

        if (e.ChangedButton == MouseButton.Middle)
        {
            _panning = false;
            Cursor = Cursors.Arrow;
            ResetHoverHitCache();
            ReleaseMouseCapture();
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_connectionDrag is not null)
        {
            var drag = _connectionDrag;
            _connectionDrag = null;
            ReleaseMouseCapture();
            var target = HitInputPort(position);
            if (target is not null && target.Id != drag.SourceNodeId)
            {
                Project.Connect(drag.SourceNodeId, drag.OutputId, target.Id);
                ProjectChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                System.Media.SystemSounds.Beep.Play();
            }

            RequestRender();
            return;
        }

        ReleaseMouseCapture();
        _dragNodeId = null;
        ResetHoverHitCache();
        if (_dragMoved)
        {
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        _dragMoved = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DuplicateSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            CenterGraph();
            e.Handled = true;
        }
    }

    private NovelNode AddNodeAt(Point worldPosition, NodeKind kind)
    {
        var node = Project.AddNode(
            kind,
            (float)(worldPosition.X - NodeWidth / 2),
            (float)(worldPosition.Y - 80));
        SelectCreatedNode(node);
        return node;
    }

    private NovelNode AddNodeTemplateAt(Point worldPosition, NodeTemplateKind template)
    {
        var node = Project.AddNodeTemplate(
            template,
            (float)(worldPosition.X - NodeWidth / 2),
            (float)(worldPosition.Y - 80));
        SelectCreatedNode(node);
        return node;
    }

    private void AddConnectedNodeFromSelected(NodeKind kind)
    {
        if (SelectedNodeId is null
            || Project.FindNode(SelectedNodeId) is not { } source)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        try
        {
            var node = Project.AddConnectedNode(
                source.Id,
                kind,
                source.X + (float)NodeWidth + 160,
                source.Y);
            SelectCreatedNode(node);
        }
        catch (InvalidOperationException)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private void AddConnectedNodeTemplateFromSelected(NodeTemplateKind template)
    {
        if (SelectedNodeId is null
            || Project.FindNode(SelectedNodeId) is not { } source)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        try
        {
            var node = Project.AddConnectedNodeTemplate(
                source.Id,
                template,
                source.X + (float)NodeWidth + 160,
                source.Y);
            SelectCreatedNode(node);
        }
        catch (InvalidOperationException)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    private void SelectCreatedNode(NovelNode node)
    {
        SelectedNodeId = node.Id;
        RebuildNodeLookup();
        RequestRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ShowContextMenu(Point position)
    {
        var connection = HitConnection(position);
        if (connection is not null)
        {
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(
                "Настроить переход...",
                () => TransitionSettingsRequested?.Invoke(
                    connection.SourceNodeId,
                    connection.OutputId)));
            menu.Items.Add(CreateMenuItem(
                "Разорвать связь",
                () =>
                {
                    var output = Project.FindOutput(
                        connection.SourceNodeId,
                        connection.OutputId);
                    if (output is not null)
                    {
                        output.TargetNodeId = null;
                        ProjectChanged?.Invoke(this, EventArgs.Empty);
                        RequestRender();
                    }
                }));
            OpenContextMenu(menu);
            return;
        }

        var outputPort = HitOutputPort(position);
        var node = outputPort is not null
            ? Project.FindNode(outputPort.Value.NodeId)
            : HitNode(position);
        if (node is not null)
        {
            SelectNode(node.Id);
            var menu = new ContextMenu();
            menu.Items.Add(CreateMenuItem(
                "Предпросмотр с этой ноды",
                () => PreviewNodeRequested?.Invoke(node.Id)));
            if (node.Kind == NodeKind.Start)
            {
                menu.Items.Add(CreateMenuItem(
                    "Редактировать главное меню",
                    () => EditMainMenuRequested?.Invoke()));
            }
            else
            {
                menu.Items.Add(CreateMenuItem(
                    "Редактировать сцену и персонажей",
                    () => EditNodeSceneRequested?.Invoke(node.Id)));
                menu.Items.Add(CreateMenuItem(
                    "Дублировать ноду",
                    DuplicateSelected));
            }
            if (node.Kind == NodeKind.Dialogue)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateMenuItem(
                    "Добавить вариант ответа",
                    () => AddChoiceRequested?.Invoke(node.Id)));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(CreateMenuItem(
                "Создать связанную сцену",
                AddConnectedSceneFromSelected,
                CanAddConnectedNode(node)));
            menu.Items.Add(CreateMenuItem(
                "Создать связанный диалог",
                AddConnectedDialogueFromSelected,
                CanAddConnectedNode(node)));
            menu.Items.Add(CreateConnectedTemplateMenu(node));

            if (node.Kind != NodeKind.Start)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(CreateInheritanceMenu(node));
            }

            OpenContextMenu(menu);
            return;
        }

        _contextWorldPosition = ScreenToWorld(position);
        var canvasMenu = new ContextMenu();
        canvasMenu.Items.Add(CreateMenuItem(
            "Добавить сцену",
            () => AddNodeAt(_contextWorldPosition, NodeKind.Scene)));
        canvasMenu.Items.Add(CreateMenuItem(
            "Добавить диалог",
            () => AddNodeAt(_contextWorldPosition, NodeKind.Dialogue)));
        canvasMenu.Items.Add(new Separator());
        canvasMenu.Items.Add(CreateTemplateMenu(_contextWorldPosition));
        OpenContextMenu(canvasMenu);
    }

    private void OpenContextMenu(ContextMenu menu)
    {
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(
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

    private static bool CanAddConnectedNode(NovelNode node) =>
        node.Outputs.Any(output => output.TargetNodeId is null)
        || node.Kind == NodeKind.Dialogue;

    private MenuItem CreateTemplateMenu(Point worldPosition)
    {
        var menu = new MenuItem { Header = "Шаблоны нод" };
        menu.Items.Add(CreateMenuItem(
            "Сцена с фоном",
            () => AddNodeTemplateAt(worldPosition, NodeTemplateKind.EstablishingScene)));
        menu.Items.Add(CreateMenuItem(
            "Реплика персонажа",
            () => AddNodeTemplateAt(worldPosition, NodeTemplateKind.CharacterLine)));
        menu.Items.Add(CreateMenuItem(
            "Выбор с двумя вариантами",
            () => AddNodeTemplateAt(worldPosition, NodeTemplateKind.ChoiceBranch)));
        return menu;
    }

    private MenuItem CreateConnectedTemplateMenu(NovelNode node)
    {
        var menu = new MenuItem
        {
            Header = "Создать связанное по шаблону",
            IsEnabled = CanAddConnectedNode(node),
        };
        menu.Items.Add(CreateMenuItem(
            "Сцена с фоном",
            () => AddConnectedNodeTemplateFromSelected(NodeTemplateKind.EstablishingScene),
            CanAddConnectedNode(node)));
        menu.Items.Add(CreateMenuItem(
            "Реплика персонажа",
            () => AddConnectedNodeTemplateFromSelected(NodeTemplateKind.CharacterLine),
            CanAddConnectedNode(node)));
        menu.Items.Add(CreateMenuItem(
            "Выбор с двумя вариантами",
            () => AddConnectedNodeTemplateFromSelected(NodeTemplateKind.ChoiceBranch),
            CanAddConnectedNode(node)));
        return menu;
    }

    private MenuItem CreateInheritanceMenu(NovelNode node)
    {
        var menu = new MenuItem { Header = "Наследовать" };
        menu.Items.Add(CreateInheritanceResourceMenu(
            node,
            InheritanceResource.Music,
            "Наследовать музыку"));
        menu.Items.Add(CreateInheritanceResourceMenu(
            node,
            InheritanceResource.Background,
            "Наследовать задний фон"));
        menu.Items.Add(CreateInheritanceResourceMenu(
            node,
            InheritanceResource.Characters,
            "Наследовать персонажей"));
        return menu;
    }

    private MenuItem CreateInheritanceResourceMenu(
        NovelNode node,
        InheritanceResource resource,
        string header)
    {
        var item = new MenuItem { Header = header };
        var sources = GetIncomingNodes(node).ToList();
        if (sources.Count == 0)
        {
            item.IsEnabled = false;
            item.Items.Add(new MenuItem
            {
                Header = "Нет входящих связанных нод",
                IsEnabled = false,
            });
            return item;
        }

        foreach (var source in sources)
        {
            item.Items.Add(CreateMenuItem(
                $"Из «{source.Title}»: {DescribeInheritanceSource(source, resource)}",
                () => ApplyInheritance(node, resource)));
        }

        return item;
    }

    private IEnumerable<NovelNode> GetIncomingNodes(NovelNode node) =>
        Project.Nodes
            .Where(candidate => candidate.Outputs.Any(output => output.TargetNodeId == node.Id))
            .OrderBy(candidate => candidate.Title, StringComparer.CurrentCulture);

    private string DescribeInheritanceSource(
        NovelNode source,
        InheritanceResource resource)
    {
        var resolved = ResolveInheritanceSource(
            source,
            resource,
            new HashSet<string>(StringComparer.Ordinal));
        if (resolved is null)
        {
            return "источник не найден";
        }
        if (resolved.Node.Id == source.Id)
        {
            return resolved.Description;
        }
        return $"источник «{resolved.Node.Title}»: {resolved.Description}";
    }

    private InheritanceSourceDescription? ResolveInheritanceSource(
        NovelNode node,
        InheritanceResource resource,
        HashSet<string> visited)
    {
        if (!visited.Add(node.Id))
        {
            return new InheritanceSourceDescription(
                node,
                "цепочка наследования зациклена");
        }
        if (!NodeInheritsResource(node, resource))
        {
            return new InheritanceSourceDescription(
                node,
                DescribeExplicitInheritanceValue(node, resource));
        }

        var sources = GetIncomingNodes(node).ToList();
        if (sources.Count == 0)
        {
            return new InheritanceSourceDescription(
                node,
                "нет входящей ноды-источника");
        }
        if (sources.Count > 1)
        {
            return new InheritanceSourceDescription(
                node,
                $"источник зависит от ветки: {string.Join(", ", sources.Select(source => $"«{source.Title}»"))}");
        }

        return ResolveInheritanceSource(sources[0], resource, visited);
    }

    private static bool NodeInheritsResource(
        NovelNode node,
        InheritanceResource resource) =>
        resource switch
        {
            InheritanceResource.Music => node.InheritMusic,
            InheritanceResource.Background => node.InheritBackground,
            InheritanceResource.Characters => node.InheritCharacters,
            _ => false,
        };

    private static string DescribeExplicitInheritanceValue(
        NovelNode source,
        InheritanceResource resource) =>
        resource switch
        {
            InheritanceResource.Music => EmptyFallback(source.Music, "музыка не задана"),
            InheritanceResource.Background => EmptyFallback(source.Background, "фон не задан"),
            InheritanceResource.Characters => source.Characters.Count == 0
                ? "персонажи не заданы"
                : string.Join(", ", source.Characters.Select(CharacterDisplayName)),
            _ => string.Empty,
        };

    private static string CharacterDisplayName(CharacterPlacement character) =>
        string.IsNullOrWhiteSpace(character.Name)
            ? character.Id
            : character.Name;

    private static string EmptyFallback(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void ApplyInheritance(NovelNode node, InheritanceResource resource)
    {
        switch (resource)
        {
            case InheritanceResource.Music:
                node.InheritMusic = true;
                if (node.UsesTypeDefaults)
                {
                    node.PropertyOverrides.Add("inheritMusic");
                }
                break;
            case InheritanceResource.Background:
                node.InheritBackground = true;
                if (node.UsesTypeDefaults)
                {
                    node.PropertyOverrides.Add("inheritBackground");
                }
                break;
            case InheritanceResource.Characters:
                node.InheritCharacters = true;
                if (node.UsesTypeDefaults)
                {
                    node.PropertyOverrides.Add("inheritCharacters");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
        }

        SelectedNodeId = node.Id;
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        RequestRender();
    }

    private void RequestRender()
    {
        if (_renderQueued)
        {
            return;
        }

        _renderQueued = true;
        Dispatcher.BeginInvoke(
            () =>
            {
                _renderQueued = false;
                InvalidateVisual();
            },
            DispatcherPriority.Render);
    }

    private void UpdateHoverCursor(Point position)
    {
        if (_lastHoverCursor is not null
            && DistanceSquared(position, _lastHoverHitPoint)
                <= HoverHitCacheDistance * HoverHitCacheDistance)
        {
            return;
        }

        var cursor = HitOutputPort(position) is not null
            ? Cursors.Cross
            : HitNode(position) is not null
                ? Cursors.SizeAll
                : Cursors.Arrow;
        _lastHoverHitPoint = position;
        _lastHoverCursor = cursor;
        if (!ReferenceEquals(Cursor, cursor))
        {
            Cursor = cursor;
        }
    }

    private void ResetHoverHitCache()
    {
        _lastHoverCursor = null;
        _lastHoverHitPoint = new Point(double.NaN, double.NaN);
    }

    private static double DistanceSquared(Point first, Point second)
    {
        var delta = first - second;
        return delta.X * delta.X + delta.Y * delta.Y;
    }

    private void DrawGrid(DrawingContext drawingContext)
    {
        var startX = _viewOffset.X % GridSize;
        var startY = _viewOffset.Y % GridSize;
        for (var x = startX; x < ActualWidth; x += GridSize)
        {
            drawingContext.DrawLine(GridPen, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (var y = startY; y < ActualHeight; y += GridSize)
        {
            drawingContext.DrawLine(GridPen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawConnections(DrawingContext drawingContext, Rect viewport)
    {
        EnsureNodeLookup();
        foreach (var node in Project.Nodes)
        {
            for (var outputIndex = 0; outputIndex < node.Outputs.Count; outputIndex++)
            {
                var output = node.Outputs[outputIndex];
                if (output.TargetNodeId is null
                    || !_nodesById.TryGetValue(output.TargetNodeId, out var target))
                {
                    continue;
                }

                var source = GetOutputPort(node, output, outputIndex);
                var geometry = CreateCurveGeometry(source.Center, GetInputPort(target).Center);
                if (!IntersectsViewport(geometry.Bounds, viewport))
                {
                    continue;
                }
                drawingContext.DrawGeometry(null, ConnectionPen, geometry);
                _connections.Add(new ConnectionVisual(node.Id, output.Id, geometry));
            }
        }
    }

    private void EnsureNodeLookup()
    {
        if (_nodesById.Count != Project.Nodes.Count)
        {
            RebuildNodeLookup();
        }
    }

    private void RebuildNodeLookup()
    {
        _nodesById.Clear();
        foreach (var node in Project.Nodes)
        {
            _nodesById[node.Id] = node;
        }
    }

    private static void DrawCurve(
        DrawingContext drawingContext,
        Point start,
        Point end,
        Pen pen)
    {
        drawingContext.DrawGeometry(null, pen, CreateCurveGeometry(start, end));
    }

    private static StreamGeometry CreateCurveGeometry(Point start, Point end)
    {
        var distance = Math.Max(80, Math.Abs(end.X - start.X) * 0.45);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.BezierTo(
                new Point(start.X + distance, start.Y),
                new Point(end.X - distance, end.Y),
                end,
                true,
                false);
        }
        geometry.Freeze();
        return geometry;
    }

    private void DrawNode(DrawingContext drawingContext, NovelNode node)
    {
        var rectangle = GetNodeRectangle(node);
        var selected = node.Id == SelectedNodeId;
        var headerBrush = node.Kind switch
        {
            NodeKind.Start => StartHeaderBrush,
            NodeKind.Scene => SceneHeaderBrush,
            NodeKind.Dialogue => DialogueHeaderBrush,
            _ => FallbackHeaderBrush,
        };
        drawingContext.DrawRoundedRectangle(
            NodeBodyBrush,
            selected ? SelectedNodePen : NodePen,
            rectangle,
            8,
            8);
        drawingContext.PushClip(
            new RectangleGeometry(
                new Rect(rectangle.X, rectangle.Y, rectangle.Width, HeaderHeight)));
        drawingContext.DrawRoundedRectangle(
            headerBrush,
            null,
            new Rect(rectangle.X, rectangle.Y, rectangle.Width, HeaderHeight + 8),
            8,
            8);
        drawingContext.Pop();

        DrawText(
            drawingContext,
            node.Title,
            14,
            FontWeights.SemiBold,
            Brushes.White,
            new Point(rectangle.X + 16, rectangle.Y + 10),
            rectangle.Width - 32);
        DrawText(
            drawingContext,
            KindName(node.Kind).ToUpperInvariant(),
            10,
            FontWeights.SemiBold,
            MutedTextBrush,
            new Point(rectangle.X + 16, rectangle.Y + HeaderHeight + 10),
            rectangle.Width - 32);

        var preview = node.Kind == NodeKind.Dialogue && node.Speaker.Length > 0
            ? $"{node.Speaker}: {node.Text}"
            : node.Text;
        if (preview.Length > 52)
        {
            preview = preview[..49] + "...";
        }
        DrawText(
            drawingContext,
            preview,
            12,
            FontWeights.Normal,
            PreviewTextBrush,
            new Point(rectangle.X + 16, rectangle.Y + HeaderHeight + 29),
            rectangle.Width - 32);

        if (node.Kind != NodeKind.Start)
        {
            DrawPort(drawingContext, GetInputPort(node).Center, PortActiveBrush);
        }

        for (var index = 0; index < node.Outputs.Count; index++)
        {
            var output = node.Outputs[index];
            var port = GetOutputPort(node, output, index);
            DrawText(
                drawingContext,
                output.Label,
                12,
                FontWeights.Normal,
                OutputTextBrush,
                new Point(rectangle.X + 16, port.Center.Y - 9),
                rectangle.Width - 38,
                TextAlignment.Right);
            DrawPort(
                drawingContext,
                port.Center,
                output.TargetNodeId is null ? PortInactiveBrush : PortActiveBrush);
        }
    }

    private void DrawText(
        DrawingContext drawingContext,
        string text,
        double size,
        FontWeight weight,
        Brush brush,
        Point origin,
        double maxWidth,
        TextAlignment alignment = TextAlignment.Left)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            NodeTypeface,
            size,
            brush,
            _pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        formatted.SetFontWeight(weight);
        drawingContext.DrawText(formatted, origin);
    }

    private static void DrawPort(DrawingContext drawingContext, Point center, Brush brush)
    {
        drawingContext.DrawEllipse(
            brush,
            PortOutlinePen,
            center,
            PortRadius,
            PortRadius);
    }

    private ConnectionVisual? HitConnection(Point point)
    {
        return _connections
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(connection => connection.Geometry.StrokeContains(ConnectionHitPen, point));
    }

    private static SolidColorBrush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness, DashStyle? dashStyle = null)
    {
        var pen = new Pen(brush, thickness)
        {
            DashStyle = dashStyle ?? DashStyles.Solid,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private NovelNode? HitNode(Point point) =>
        Project.Nodes
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(node => GetNodeRectangle(node).Contains(point));

    private NovelNode? HitInputPort(Point point) =>
        Project.Nodes.FirstOrDefault(
            node => node.Kind != NodeKind.Start && GetInputPort(node).HitArea.Contains(point));

    private OutputPort? HitOutputPort(Point point)
    {
        foreach (var node in Project.Nodes)
        {
            for (var index = 0; index < node.Outputs.Count; index++)
            {
                var port = GetOutputPort(node, node.Outputs[index], index);
                if (port.HitArea.Contains(point))
                {
                    return port;
                }
            }
        }
        return null;
    }

    private OutputPort? GetOutputPort(string nodeId, string outputId)
    {
        var node = Project.FindNode(nodeId);
        if (node is null)
        {
            return null;
        }
        var index = node.Outputs.FindIndex(output => output.Id == outputId);
        return index < 0 ? null : GetOutputPort(node, node.Outputs[index], index);
    }

    private OutputPort GetOutputPort(NovelNode node, NodeOutput output, int index)
    {
        var rectangle = GetNodeRectangle(node);
        var center = new Point(
            rectangle.Right,
            rectangle.Top + HeaderHeight + 76 + index * OutputRowHeight);
        return new OutputPort(node.Id, output.Id, center, MakeHitArea(center));
    }

    private InputPort GetInputPort(NovelNode node)
    {
        var rectangle = GetNodeRectangle(node);
        var center = new Point(rectangle.Left, rectangle.Top + HeaderHeight + 20);
        return new InputPort(node.Id, center, MakeHitArea(center));
    }

    private Rect GetNodeRectangle(NovelNode node) =>
        new(
            node.X + _viewOffset.X,
            node.Y + _viewOffset.Y,
            NodeWidth,
            GetNodeHeight(node));

    private static double GetNodeHeight(NovelNode node) =>
        HeaderHeight + 74 + Math.Max(1, node.Outputs.Count) * OutputRowHeight + 12;

    private static Rect MakeHitArea(Point center) =>
        new(center.X - 12, center.Y - 12, 24, 24);

    private static bool IntersectsViewport(Rect bounds, Rect viewport)
    {
        var expanded = viewport;
        expanded.Inflate(32, 32);
        return bounds.IntersectsWith(expanded);
    }

    private static bool NearlyEqual(double first, double second) =>
        Math.Abs(first - second) < DragRenderEpsilon;

    private Point ScreenToWorld(Point point) =>
        point - _viewOffset;

    private static string KindName(NodeKind kind) =>
        kind switch
        {
            NodeKind.Start => "Старт",
            NodeKind.Scene => "Сцена",
            NodeKind.Dialogue => "Диалог",
            _ => kind.ToString(),
        };

    private sealed record ConnectionDrag(
        string SourceNodeId,
        string OutputId,
        Point Cursor);

    private sealed record ConnectionVisual(
        string SourceNodeId,
        string OutputId,
        StreamGeometry Geometry);

    private readonly record struct InputPort(
        string NodeId,
        Point Center,
        Rect HitArea);

    private readonly record struct OutputPort(
        string NodeId,
        string OutputId,
        Point Center,
        Rect HitArea);

    private sealed record InheritanceSourceDescription(
        NovelNode Node,
        string Description);

    private enum InheritanceResource
    {
        Background,
        Music,
        Characters,
    }
}
