import type { NodeGraph, VisualNode } from './models';

const startNode: VisualNode = {
  id: 'node-start',
  title: 'Start Node',
  x: 80,
  y: 120,
  pins: [
    {
      id: 'pin-start-out',
      nodeId: 'node-start',
      name: 'Next',
      direction: 'output',
    },
  ],
};

const dialogueNode: VisualNode = {
  id: 'node-dialogue',
  title: 'Dialogue Node',
  x: 420,
  y: 120,
  pins: [
    {
      id: 'pin-dialogue-in',
      nodeId: 'node-dialogue',
      name: 'In',
      direction: 'input',
    },
    {
      id: 'pin-dialogue-out',
      nodeId: 'node-dialogue',
      name: 'Next',
      direction: 'output',
    },
  ],
};

export function createStartupGraph(): NodeGraph {
  return {
    nodes: [startNode, dialogueNode],
    connections: [
      {
        id: 'connection-start-dialogue',
        sourcePinId: 'pin-start-out',
        targetPinId: 'pin-dialogue-in',
      },
    ],
  };
}
