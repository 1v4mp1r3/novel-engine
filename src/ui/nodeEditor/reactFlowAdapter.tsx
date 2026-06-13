import { Position, type Edge, type Node } from '@xyflow/react';
import type { ReactNode } from 'react';
import type { NodeGraph, Pin, VisualNode } from '../../core/models';

type GraphNodeData = {
  [key: string]: unknown;
  label: ReactNode;
};

type GraphNode = Node<GraphNodeData>;
type PinLookup = Map<string, Pin>;

function buildPinLookup(graph: NodeGraph): PinLookup {
  const pins = new Map<string, Pin>();

  for (const node of graph.nodes) {
    for (const pin of node.pins) {
      pins.set(pin.id, pin);
    }
  }

  return pins;
}

function toReactFlowNode(node: VisualNode): GraphNode {
  const inputPins = node.pins.filter((pin) => pin.direction === 'input');
  const outputPins = node.pins.filter((pin) => pin.direction === 'output');

  return {
    id: node.id,
    position: { x: node.x, y: node.y },
    data: {
      label: (
        <div className="graph-node">
          <div className="graph-node-title">{node.title}</div>
          <div className="graph-node-pins">
            <div className="graph-node-pin-column">
              {inputPins.map((pin) => (
                <span className="graph-node-pin graph-node-pin-input" key={pin.id}>
                  {pin.name}
                </span>
              ))}
            </div>
            <div className="graph-node-pin-column graph-node-pin-column-right">
              {outputPins.map((pin) => (
                <span className="graph-node-pin graph-node-pin-output" key={pin.id}>
                  {pin.name}
                </span>
              ))}
            </div>
          </div>
        </div>
      ),
    },
    sourcePosition: Position.Right,
    targetPosition: Position.Left,
  };
}

export function toReactFlowNodes(graph: NodeGraph): GraphNode[] {
  return graph.nodes.map(toReactFlowNode);
}

export function toReactFlowEdges(graph: NodeGraph): Edge[] {
  const pins = buildPinLookup(graph);

  return graph.connections.map((connection) => {
    const sourcePin = pins.get(connection.sourcePinId);
    const targetPin = pins.get(connection.targetPinId);

    if (!sourcePin || !targetPin) {
      throw new Error(`Connection ${connection.id} references a missing pin.`);
    }

    return {
      id: connection.id,
      source: sourcePin.nodeId,
      target: targetPin.nodeId,
      animated: true,
      style: { stroke: '#69d2b1', strokeWidth: 2 },
    };
  });
}
