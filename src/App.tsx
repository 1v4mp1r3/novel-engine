import { MainWindow } from './ui/layout/MainWindow';
import { createStartupGraph } from './core/sampleGraph';

const startupGraph = createStartupGraph();

export default function App() {
  return <MainWindow graph={startupGraph} />;
}
