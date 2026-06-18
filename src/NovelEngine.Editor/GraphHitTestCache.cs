using System.Windows;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal sealed class GraphHitTestCache
{
    private const double SpatialCellSize = 256;

    private readonly List<GraphNodeHitArea> _nodes = [];
    private readonly List<GraphInputPortHitArea> _inputPorts = [];
    private readonly List<GraphOutputPortHitArea> _outputPorts = [];
    private readonly Dictionary<SpatialCell, List<int>> _nodeIndex = [];
    private readonly Dictionary<SpatialCell, List<int>> _inputPortIndex = [];
    private readonly Dictionary<SpatialCell, List<int>> _outputPortIndex = [];

    public void Clear()
    {
        _nodes.Clear();
        _inputPorts.Clear();
        _outputPorts.Clear();
        _nodeIndex.Clear();
        _inputPortIndex.Clear();
        _outputPortIndex.Clear();
    }

    public void AddNode(GraphNodeHitArea node)
    {
        var index = _nodes.Count;
        _nodes.Add(node);
        AddToIndex(_nodeIndex, node.Bounds, index);
    }

    public void AddInputPort(GraphInputPortHitArea inputPort)
    {
        var index = _inputPorts.Count;
        _inputPorts.Add(inputPort);
        AddToIndex(_inputPortIndex, inputPort.HitArea, index);
    }

    public void AddOutputPort(GraphOutputPortHitArea outputPort)
    {
        var index = _outputPorts.Count;
        _outputPorts.Add(outputPort);
        AddToIndex(_outputPortIndex, outputPort.HitArea, index);
    }

    public NovelNode? HitNode(Point point)
    {
        if (!_nodeIndex.TryGetValue(GetCell(point), out var indexes))
        {
            return null;
        }

        for (var hitIndex = indexes.Count - 1; hitIndex >= 0; hitIndex--)
        {
            var node = _nodes[indexes[hitIndex]];
            if (node.Bounds.Contains(point))
            {
                return node.Node;
            }
        }
        return null;
    }

    public NovelNode? HitInputPort(Point point)
    {
        if (!_inputPortIndex.TryGetValue(GetCell(point), out var indexes))
        {
            return null;
        }

        foreach (var index in indexes)
        {
            var inputPort = _inputPorts[index];
            if (inputPort.HitArea.Contains(point))
            {
                return inputPort.Node;
            }
        }
        return null;
    }

    public GraphOutputPortHitArea? HitOutputPort(Point point)
    {
        if (!_outputPortIndex.TryGetValue(GetCell(point), out var indexes))
        {
            return null;
        }

        foreach (var index in indexes)
        {
            var outputPort = _outputPorts[index];
            if (outputPort.HitArea.Contains(point))
            {
                return outputPort;
            }
        }
        return null;
    }

    private static void AddToIndex(
        Dictionary<SpatialCell, List<int>> spatialIndex,
        Rect bounds,
        int itemIndex)
    {
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var left = GetCell(bounds.Left);
        var right = GetCell(bounds.Right);
        var top = GetCell(bounds.Top);
        var bottom = GetCell(bounds.Bottom);

        for (var x = left; x <= right; x++)
        {
            for (var y = top; y <= bottom; y++)
            {
                var cell = new SpatialCell(x, y);
                if (!spatialIndex.TryGetValue(cell, out var indexes))
                {
                    indexes = [];
                    spatialIndex[cell] = indexes;
                }
                indexes.Add(itemIndex);
            }
        }
    }

    private static SpatialCell GetCell(Point point) =>
        new(GetCell(point.X), GetCell(point.Y));

    private static int GetCell(double coordinate) =>
        (int)Math.Floor(coordinate / SpatialCellSize);

    private readonly record struct SpatialCell(int X, int Y);
}

internal readonly record struct GraphNodeHitArea(
    NovelNode Node,
    Rect Bounds);

internal readonly record struct GraphInputPortHitArea(
    NovelNode Node,
    Point Center,
    Rect HitArea);

internal readonly record struct GraphOutputPortHitArea(
    string NodeId,
    string OutputId,
    Point Center,
    Rect HitArea);
