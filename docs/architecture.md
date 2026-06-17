# Architecture

## C# editor

The WPF editor owns authoring concerns:

- node graph interaction;
- full-project code authoring and code-to-graph compilation;
- scene, dialogue, and choice property editing;
- asset paths;
- JSON project storage;
- validation and managed preview.

Connections belong to outputs. A scene has one linear output; a dialogue has
zero or more choice outputs. Each output stores its target node, condition, and
script executed when selected.

## Project language

Novel Project Language is a lexer/parser/compiler implemented in
`NovelEngine.Core`. It is not JSON with tags: it has declarations, user-defined
types, inheritance, node instances, properties, and transition blocks.

Built-in types are `start`, `scene`, and `dialogue`. User types may derive from
scene/dialogue types through multiple inheritance levels. Compilation resolves
the type chain, applies overridden defaults, and emits concrete runtime nodes.
The graph and code editor are two representations of the same project model.

Visual script blocks are stored in project JSON on nodes and outputs as
`VisualScriptBlock` lists. They compile into the same small `NovelScript`
commands used by hand-written scripts (`set`, `add`, `unset`) and are executed
after the matching text script. This keeps a future Scratch-like block editor on
top of the same runtime semantics instead of introducing a second scripting
engine.

Choice condition authoring uses the same approach: `VisualConditionExpression`
parses and compiles the supported condition forms (`always`, variable truth,
variable false, comparisons, and AND/OR groups) through the core layer. The WPF
builder is only an editor for that model, so future condition blocks can reuse
the same runtime semantics instead of adding a UI-only parser. Runtime choice
filtering, project diagnostics, and project-language validation all evaluate
conditions through this compiler.

## Asset catalog

Each project owns a typed asset catalog. Imported files are copied into
nested logical folders below `files` next to the project file. The catalog
stores folder declarations separately from assets so empty folders survive
serialization. Code uses stable `@asset_id` references instead of physical
paths. Runtime preview resolves those references through the catalog before
loading media. Renaming an asset rewrites all references; moving or renaming a
folder updates the catalog and corresponding files together.

## Code diagnostics

The WPF code editor derives syntax spans and source locations from the project
language service in `NovelEngine.Core`. It performs debounced parsing while the
user types, highlights the token at a parser error, and exposes node declaration
locations so graph nodes can navigate directly to their source.

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
`characters` list. Each character stores a name, sprite path, a left, center,
or right preset, and an optional free transform with stage coordinates, scale,
and rotation.

A node with `inheritMusic: true` keeps the current audio player and playback
position. A node with `inheritMusic: false` replaces or stops the track.
Connections own their transition sound and fade duration.

Node preview restores inherited state by replaying the first graph path from
the start node to the selected node. A compiled run always starts at the start
node.

## Build pipeline

`NovelBuildCompiler` reparses the project language and validates the concrete
runtime graph and scripts. It copies catalog and direct-path assets into an
atomic build directory under the same `files/` layout used by editor projects,
rewrites runtime paths, reapplies visual script blocks from the saved project
model, removes source code from release builds, and emits `game.novel.json` plus
`manifest.json`.

Run and debug commands launch the editor executable in a separate
`--play-build` process. This process only loads the compiled package and can be
stopped without closing the editor. Debug builds retain source and show the
current node, inherited media state, characters, and script variables.

## C++ runtime

The native module is kept behind a C ABI. The editor must never depend directly
on C++ classes. This allows the runtime to be embedded into another engine or
shipped with a standalone player while project authoring remains stable.
