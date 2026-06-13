import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
} from '@xyflow/react';
import { type MouseEvent as ReactMouseEvent, useState } from 'react';
import type { NodeGraph } from '../../core/models';
import { createDialogueVisualNode } from '../../core/nodeFactory';
import { toReactFlowEdges, toReactFlowNodes } from './reactFlowAdapter';

interface NodeCanvasProps {
  graph: NodeGraph;
}

export function NodeCanvas({ graph }: NodeCanvasProps) {
  return (
    <ReactFlowProvider>
      <NodeCanvasInner graph={graph} />
    </ReactFlowProvider>
  );
}

interface ContextMenuState {
  flowX: number;
  flowY: number;
  screenX: number;
  screenY: number;
}

function NodeCanvasInner({ graph }: NodeCanvasProps) {
  const [workingGraph, setWorkingGraph] = useState<NodeGraph>(graph);
  const [contextMenu, setContextMenu] = useState<ContextMenuState | null>(null);
  const { screenToFlowPosition } = useReactFlow();

  const nodes = toReactFlowNodes(workingGraph);
  const edges = toReactFlowEdges(workingGraph);

  function handleCanvasContextMenu(event: MouseEvent | ReactMouseEvent) {
    const target = event.target as HTMLElement;

    if (target.closest('.react-flow__node') || target.closest('.canvas-context-menu')) {
      return;
    }

    event.preventDefault();

    const position = screenToFlowPosition({
      x: event.clientX,
      y: event.clientY,
    });

    setContextMenu({
      flowX: position.x,
      flowY: position.y,
      screenX: event.clientX,
      screenY: event.clientY,
    });
  }

  function handleCreateDialogueNode() {
    if (!contextMenu) {
      return;
    }

    const nextIndex = workingGraph.nodes.length + 1;
    const id = `node-dialogue-${nextIndex}`;
    const node = createDialogueVisualNode({
      id,
      title: `Dialogue Node ${nextIndex}`,
      x: contextMenu.flowX,
      y: contextMenu.flowY,
    });

    setWorkingGraph((currentGraph) => ({
      ...currentGraph,
      nodes: [...currentGraph.nodes, node],
    }));
    setContextMenu(null);
  }

  return (
    <div className="node-canvas-frame" onContextMenu={handleCanvasContextMenu}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        fitView
        nodesDraggable
        nodesConnectable={false}
        elementsSelectable
        onPaneClick={() => setContextMenu(null)}
      >
        <Background color="#2f3948" gap={24} size={1} variant={BackgroundVariant.Dots} />
        <MiniMap pannable zoomable />
        <Controls position="bottom-right" />
      </ReactFlow>

      {contextMenu ? (
        <div
          className="canvas-context-menu"
          style={{ left: contextMenu.screenX, top: contextMenu.screenY }}
          onClick={(event) => event.stopPropagation()}
        >
          <button type="button" onClick={handleCreateDialogueNode}>
            Create Dialogue Node
          </button>
        </div>
      ) : null}
    </div>
  );
}
