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

Folders are project declarations rather than incidental UI state. Use
`folder "parent/child"` to create a nested folder and `in "parent/child"` to
assign an asset to it. Creating, renaming, deleting, and moving folders in the
file manager updates both the source representation and files under `assets`.

## Code editor

The editor highlights declarations, keywords, strings, numbers, comments, and
asset references. Parser errors are underlined and shown in the status bar as
the document changes. Double-clicking a node in the graph switches to the code
tab and selects that node's declaration.

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
}

script "set met_alice = true\nadd score 1"
```

Character positions are `left`, `center`, and `right`. Strings use JSON escape
rules. Triple-quoted strings are also accepted for multiline text:

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
