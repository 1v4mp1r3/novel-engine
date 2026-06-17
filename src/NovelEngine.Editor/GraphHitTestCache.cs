using System.Windows;
using NovelEngine.Core;

namespace NovelEngine.Editor;

internal sealed class GraphHitTestCache
{
    private readonly List<GraphNodeHitArea> _nodes = [];
    private readonly List<GraphInputPortHitArea> _inputPorts = [];
    private readonly List<GraphOutputPortHitArea> _outputPorts = [];

    public void Clear()
    {
        _nodes.Clear();
        _inputPorts.Clear();
        _outputPorts.Clear();
    }

    public void AddNode(GraphNodeHitArea node) =>
        _nodes.Add(node);

    public void AddInputPort(GraphInputPortHitArea inputPort) =>
        _inputPorts.Add(inputPort);

    public void AddOutputPort(GraphOutputPortHitArea outputPort) =>
        _outputPorts.Add(outputPort);

    public NovelNode? HitNode(Point point)
    {
        for (var index = _nodes.Count - 1; index >= 0; index--)
        {
            var node = _nodes[index];
            if (node.Bounds.Contains(point))
            {
                return node.Node;
            }
        }
        return null;
    }

    public NovelNode? HitInputPort(Point point)
    {
        foreach (var inputPort in _inputPorts)
        {
            if (inputPort.HitArea.Contains(point))
            {
                return inputPort.Node;
            }
        }
        return null;
    }

    public GraphOutputPortHitArea? HitOutputPort(Point point)
    {
        foreach (var outputPort in _outputPorts)
        {
            if (outputPort.HitArea.Contains(point))
            {
                return outputPort;
            }
        }
        return null;
    }
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
