import type { NodeGraph } from '../../core/models';
import { NodeCanvas } from '../nodeEditor/NodeCanvas';

interface MainWindowProps {
  graph: NodeGraph;
}

export function MainWindow({ graph }: MainWindowProps) {
  return (
    <main className="app-shell">
      <aside className="side-panel side-panel-left" aria-label="Project Explorer">
        <header className="panel-header">Project Explorer</header>
      </aside>

      <section className="canvas-region" aria-label="Node Canvas">
        <NodeCanvas graph={graph} />
      </section>

      <aside className="side-panel side-panel-right" aria-label="Properties">
        <header className="panel-header">Properties</header>
      </aside>
    </main>
  );
}
