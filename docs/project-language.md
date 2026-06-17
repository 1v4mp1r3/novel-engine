# Novel Project Language

Novel Project Language is the source representation of a complete visual novel
project. It describes node types, nodes, assets, characters, connections,
conditions, transition effects, and runtime scripts.

The editor compiles this language into the same project model used by the graph
and preview player. `Ctrl+Enter` applies code to the graph.

## Assets

Import files through the `Файлы` tab. The editor copies them into an `assets`
folder next to the project and creates stable logical names:

```text
folder "backgrounds/city"
folder "characters/alice"

asset city_night : image "assets/backgrounds/city/city-night.png" in "backgrounds/city"
asset alice_happy : image "assets/characters/alice/alice-happy.png" in "characters/alice"
asset main_theme : audio "assets/audio/main-theme.mp3"
asset door_open : audio "assets/audio/door-open.wav"
```

Use an asset from code with `@name`:

```text
background @city_night
music @main_theme

character alice {
    name "Alice"
    sprite @alice_happy
    position left
}

choice "Open the door" -> room {
    sound @door_open
}
```

Asset kinds are `image`, `audio`, and `other`. Backgrounds and sprites require
images; music and transition sounds require audio. Renaming an asset in the
manager updates every `@reference` in nodes and user-defined types.

## Character library

Reusable characters can be declared at the top level and then inserted into
nodes from the editor UI:

```text
character alice {
    name "Alice"
    sprite @alice_happy
    voice @alice_blip_1
    voice @alice_blip_2
    voice-pitch 1.1
    voice-every 1
    position left
}
```

The same character block syntax is used inside nodes and types. Top-level
characters are stored in the project library; node-level characters describe
who is currently on the scene.

Folders are project declarations rather than incidental UI state. Use
`folder "parent/child"` to create a nested folder and `in "parent/child"` to
assign an asset to it. Creating, renaming, deleting, and moving folders in the
file manager updates both the source representation and files under `assets`.

## Code editor

The editor highlights declarations, keywords, strings, numbers, comments, and
asset references. Parser errors are underlined and shown in the status bar as
the document changes. Double-clicking a node in the graph switches to the code
tab and selects that node's declaration.

Autocompletion opens while typing or through `Ctrl+Space`. It suggests language
snippets and context-specific values, including declared node targets after
`->`, asset references after `@`, and user-defined node types. Use `Up`/`Down`
to select a suggestion and `Tab` or `Enter` to insert it.

The code editor keeps rendering lightweight: syntax highlighting, parser
diagnostics, and completion are updated without drawing persistent block guide
overlays.

Visual script blocks created from the node or choice property dialogs are stored
beside the DSL source. They compile to the same runtime script commands as
hand-written `script` text and are preserved when the `Код` tab is applied back
to the graph by matching node ids and choice outputs. Generated project source
marks these locations with `# visual blocks: N` comments so block-authored logic
is visible while reading code.

Choice conditions created through the condition builder are also kept as
structured `VisualConditionExpression` data beside the emitted `when` text.
Runtime filtering, diagnostics, builds, and code-apply preservation use that
expression while the generated text still matches it; hand-edited conditions
remain valid as plain DSL strings.

## Minimal project

```text
novel "My story"

node start : start at (20, 180) {
    title "Start"
    next "Continue" -> intro
}

node intro : scene at (280, 120) {
    title "Introduction"
    text "The story begins here."
    next "Continue" -> question
}

node question : dialogue at (560, 120) {
    speaker "Hero"
    text "Where should I go?"
    choice "Home" -> home
    choice "Forest" -> forest
}
```

`start`, `scene`, and `dialogue` are built-in polymorphic node types. A project
must contain exactly one node whose final type is `start`.

## Types and inheritance

```text
type NightScene extends scene {
    background "backgrounds/night.png"
    inherit background false
    music "music/night.mp3"
    inherit music false
}

type RainyNight extends NightScene {
    background "backgrounds/rain.png"
}

node street : RainyNight at (300, 160) {
    title "Rainy street"
    text "Rain drums against the pavement."
    next "Continue" -> shelter
}
```

A type may extend `scene`, `dialogue`, or another user type. Derived types keep
base values and may override them. A node declared with a derived type still
behaves polymorphically as its final built-in kind in the runtime.

## Node properties

```text
title "Visible title"
speaker "Alice"
text "Dialogue or scene text"

inherit background true
background "backgrounds/room.png"

inherit music true
music "music/theme.mp3"

inherit characters false
character alice {
    name "Alice"
    sprite "characters/alice.png"
    position left
    placement (620, 500)
    scale 1.15
    rotation -4
}

script "set met_alice = true\nadd score 1"
```

Character positions are `left`, `center`, and `right`. A character may also
use a free transform: `placement (x, y)` stores its center on the 1920x1080
stage, `scale` changes its size, and `rotation` uses degrees. These values are
written automatically by the scene editor opened from a node's context menu.
Strings use JSON escape rules. Triple-quoted strings are also accepted for
multiline text:

```text
text """First line
Second line"""
```

## Connections and choices

```text
next "Continue" -> target

choice "Open the door" -> room {
    when "has_key"
    script "set door_open = true"
    sound "sounds/door.wav"
    fade 500
}
```

Use `none` instead of a target id for a disconnected output. `fade` is measured
in milliseconds. Conditions and scripts use the runtime language documented in
`script-language.md`.

## Comments

Both comment forms are accepted:

```text
# whole-line comment
// another comment
```
