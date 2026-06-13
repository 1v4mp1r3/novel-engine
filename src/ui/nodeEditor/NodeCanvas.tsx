import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  ReactFlowProvider,
} from '@xyflow/react';
import type { NodeGraph } from '../../core/models';
import { toReactFlowEdges, toReactFlowNodes } from './reactFlowAdapter';

interface NodeCanvasProps {
  graph: NodeGraph;
}

export function NodeCanvas({ graph }: NodeCanvasProps) {
  const nodes = toReactFlowNodes(graph);
  const edges = toReactFlowEdges(graph);

  return (
    <ReactFlowProvider>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        fitView
        nodesDraggable
        nodesConnectable={false}
        elementsSelectable
      >
        <Background color="#2f3948" gap={24} size={1} variant={BackgroundVariant.Dots} />
        <MiniMap pannable zoomable />
        <Controls position="bottom-right" />
      </ReactFlow>
    </ReactFlowProvider>
  );
}
