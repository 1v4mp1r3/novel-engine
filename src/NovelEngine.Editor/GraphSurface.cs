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

    private readonly List<ConnectionVisual> _connections = [];
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

    public void DeleteSelected()
    {
        if (SelectedNodeId is null || !Project.RemoveNode(SelectedNodeId))
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        SelectedNodeId = null;
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
            RequestRender();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (InvalidOperationException)
        {
            System.Media.SystemSounds.Beep.Play();
        }
    }

    public void RefreshGraph() => RequestRender();

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
        if (_needsInitialCenter && ActualWidth > 0 && ActualHeight > 0)
        {
            CenterGraph();
        }
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromRgb(15, 22, 31)),
            null,
            new Rect(RenderSize));
        DrawGrid(drawingContext);

        _connections.Clear();
        DrawConnections(drawingContext);
        foreach (var node in Project.Nodes)
        {
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
                    Color.FromRgb(245, 190, 80),
                    dashed: true);
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
            _viewOffset = _panOffsetStart + (position - _panStart);
            RequestRender();
            return;
        }

        if (_connectionDrag is not null)
        {
            _connectionDrag = _connectionDrag with { Cursor = position };
            RequestRender();
            return;
        }

        if (_dragNodeId is null)
        {
            Cursor = HitOutputPort(position) is not null
                ? Cursors.Cross
                : HitNode(position) is not null
                    ? Cursors.SizeAll
                    : Cursors.Arrow;
            return;
        }

        var node = Project.FindNode(_dragNodeId);
        if (node is null)
        {
            return;
        }

        node.X = (float)(_dragNodeStart.X + position.X - _dragStart.X);
        node.Y = (float)(_dragNodeStart.Y + position.Y - _dragStart.Y);
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
        SelectedNodeId = node.Id;
        RequestRender();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        ProjectChanged?.Invoke(this, EventArgs.Empty);
        return node;
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
        OpenContextMenu(canvasMenu);
    }

    private void OpenContextMenu(ContextMenu menu)
    {
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
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

    private static string DescribeInheritanceSource(
        NovelNode source,
        InheritanceResource resource) =>
        resource switch
        {
            InheritanceResource.Music => source.InheritMusic
                ? "сама наследует музыку"
                : EmptyFallback(source.Music, "музыка не задана"),
            InheritanceResource.Background => source.InheritBackground
                ? "сама наследует фон"
                : EmptyFallback(source.Background, "фон не задан"),
            InheritanceResource.Characters => source.InheritCharacters
                ? "сама наследует персонажей"
                : source.Characters.Count == 0
                    ? "персонажи не заданы"
                    : string.Join(", ", source.Characters.Select(character => character.Name)),
            _ => string.Empty,
        };

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

    private void DrawGrid(DrawingContext drawingContext)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(26, 36, 49)), 1);
        var startX = _viewOffset.X % GridSize;
        var startY = _viewOffset.Y % GridSize;
        for (var x = startX; x < ActualWidth; x += GridSize)
        {
            drawingContext.DrawLine(pen, new Point(x, 0), new Point(x, ActualHeight));
        }
        for (var y = startY; y < ActualHeight; y += GridSize)
        {
            drawingContext.DrawLine(pen, new Point(0, y), new Point(ActualWidth, y));
        }
    }

    private void DrawConnections(DrawingContext drawingContext)
    {
        var nodesById = Project.Nodes.ToDictionary(node => node.Id);
        foreach (var node in Project.Nodes)
        {
            for (var outputIndex = 0; outputIndex < node.Outputs.Count; outputIndex++)
            {
                var output = node.Outputs[outputIndex];
                if (output.TargetNodeId is null
                    || !nodesById.TryGetValue(output.TargetNodeId, out var target))
                {
                    continue;
                }

                var source = GetOutputPort(node, output, outputIndex);
                var geometry = CreateCurveGeometry(source.Center, GetInputPort(target).Center);
                drawingContext.DrawGeometry(
                    null,
                    new Pen(new SolidColorBrush(Color.FromRgb(100, 218, 183)), 2.4),
                    geometry);
                _connections.Add(new ConnectionVisual(node.Id, output.Id, geometry));
            }
        }
    }

    private static void DrawCurve(
        DrawingContext drawingContext,
        Point start,
        Point end,
        Color color,
        bool dashed)
    {
        var pen = new Pen(new SolidColorBrush(color), 2.4)
        {
            DashStyle = dashed ? DashStyles.Dash : DashStyles.Solid,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
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
        var headerColor = node.Kind switch
        {
            NodeKind.Start => Color.FromRgb(68, 91, 126),
            NodeKind.Scene => Color.FromRgb(47, 94, 89),
            NodeKind.Dialogue => Color.FromRgb(83, 65, 113),
            _ => Color.FromRgb(45, 57, 73),
        };
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromRgb(25, 35, 48)),
            new Pen(
                new SolidColorBrush(
                    selected
                        ? Color.FromRgb(245, 190, 80)
                        : Color.FromRgb(58, 75, 96)),
                selected ? 2.2 : 1.2),
            rectangle,
            8,
            8);
        drawingContext.PushClip(
            new RectangleGeometry(
                new Rect(rectangle.X, rectangle.Y, rectangle.Width, HeaderHeight)));
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(headerColor),
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
            new SolidColorBrush(Color.FromRgb(113, 129, 150)),
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
            new SolidColorBrush(Color.FromRgb(184, 198, 214)),
            new Point(rectangle.X + 16, rectangle.Y + HeaderHeight + 29),
            rectangle.Width - 32);

        if (node.Kind != NodeKind.Start)
        {
            DrawPort(drawingContext, GetInputPort(node).Center, Color.FromRgb(100, 218, 183));
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
                new SolidColorBrush(Color.FromRgb(190, 203, 218)),
                new Point(rectangle.X + 16, port.Center.Y - 9),
                rectangle.Width - 38,
                TextAlignment.Right);
            DrawPort(
                drawingContext,
                port.Center,
                output.TargetNodeId is null
                    ? Color.FromRgb(113, 129, 150)
                    : Color.FromRgb(100, 218, 183));
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
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
        formatted.SetFontWeight(weight);
        drawingContext.DrawText(formatted, origin);
    }

    private static void DrawPort(DrawingContext drawingContext, Point center, Color color)
    {
        drawingContext.DrawEllipse(
            new SolidColorBrush(color),
            new Pen(new SolidColorBrush(Color.FromRgb(15, 22, 31)), 2),
            center,
            PortRadius,
            PortRadius);
    }

    private ConnectionVisual? HitConnection(Point point)
    {
        var hitPen = new Pen(Brushes.Transparent, 12);
        return _connections
            .AsEnumerable()
            .Reverse()
            .FirstOrDefault(connection => connection.Geometry.StrokeContains(hitPen, point));
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

    private enum InheritanceResource
    {
        Background,
        Music,
        Characters,
    }
}
