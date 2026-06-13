# Architecture

## C# editor

The WPF editor owns authoring concerns:

- node graph interaction;
- scene, dialogue, and choice property editing;
- asset paths;
- JSON project storage;
- validation and managed preview.

Connections belong to outputs. A scene has one linear output; a dialogue has
zero or more choice outputs. Each output stores its target node, condition, and
script executed when selected.

## Inherited runtime state

The runtime carries state through graph transitions:

- the current background;
- the current music track;
- the current list of characters and their screen positions;
- script variables;

A node with `inheritBackground: true` keeps the current background. A node with
`inheritBackground: false` replaces it with its own `background` value.

A node with `inheritCharacters: true` keeps all characters from the previous
node. A node with `inheritCharacters: false` replaces them with its own
`characters` list. Each character stores a name, sprite path, and left, center,
or right screen position.

A node with `inheritMusic: true` keeps the current audio player and playback
position. A node with `inheritMusic: false` replaces or stops the track.
Connections own their transition sound and fade duration.

Node preview restores inherited state by replaying the first graph path from
the start node to the selected node. The full test run always starts at the
start node.

## C++ runtime

The native module is kept behind a C ABI. The editor must never depend directly
on C++ classes. This allows the runtime to be embedded into another engine or
shipped with a standalone player while project authoring remains stable.
