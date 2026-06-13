import type { VisualNode } from './models';

export interface CreateNodeOptions {
  id: string;
  title: string;
  x: number;
  y: number;
}

export function createDialogueVisualNode({ id, title, x, y }: CreateNodeOptions): VisualNode {
  return {
    id,
    title,
    x,
    y,
    pins: [
      {
        id: `${id}-in`,
        nodeId: id,
        name: 'In',
        direction: 'input',
      },
      {
        id: `${id}-out`,
        nodeId: id,
        name: 'Next',
        direction: 'output',
      },
    ],
  };
}
