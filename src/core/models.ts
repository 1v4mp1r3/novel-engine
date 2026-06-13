export type PinDirection = 'input' | 'output';

export interface Pin {
  id: string;
  nodeId: string;
  name: string;
  direction: PinDirection;
}

export interface VisualNode {
  id: string;
  title: string;
  x: number;
  y: number;
  pins: Pin[];
}

export interface Connection {
  id: string;
  sourcePinId: string;
  targetPinId: string;
}

export interface NodeGraph {
  nodes: VisualNode[];
  connections: Connection[];
}
