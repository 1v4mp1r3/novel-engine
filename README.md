# Novel Engine

Desktop constructor and runtime foundation for node-based visual novels.

## Architecture

- `NovelEngine.Editor` - C# WPF desktop editor.
- `NovelEngine.Core` - project model, JSON storage, script language, and managed preview runtime.
- `Novel Project Language` - full-project DSL compiled into the graph and runtime model.
- `native/NovelEngine.Runtime` - C++ runtime boundary for the future standalone player.

The editor uses WPF for the graph, property panels, animated preview, and audio
playback. C++ is isolated to the runtime, where native integration and
performance can matter.

## Run

Double-click `run.bat`, or run:

```powershell
dotnet run --project src/NovelEngine.Editor/NovelEngine.Editor.csproj
```

## Build and test

```powershell
dotnet build NovelEngine.sln
dotnet run --project tests/NovelEngine.Core.Tests/NovelEngine.Core.Tests.csproj
```

The C++ runtime requires a separate C++20 compiler and CMake. They are not
currently installed on this workstation.

## Editor basics

- Right-click the canvas to add a scene or dialogue node.
- Drag from an output circle to the input circle of another node to connect them.
- Scene nodes have one linear `Дальше` output.
- Dialogue nodes can have any number of choices.
- A node may inherit or replace the current background and character set.
- A node may inherit music without restarting playback.
- Right-click a connection to configure its transition sound and fade duration.
- Press `F5` for a full test run or `Ctrl+F5` to preview the selected node.
- Right-click any node to start its preview.
- Open the `Код` tab to author node types, nodes, connections, inheritance,
  assets, and transitions as source code. Press `Ctrl+Enter` to compile it.
- Open the `Файлы` tab to import assets into the project and address them from
  code through stable references such as `@city_night` or `@main_theme`.
- Entry scripts and choice conditions use the language documented in
  `docs/script-language.md`.
- The complete project DSL is documented in `docs/project-language.md`.
- Double-click a graph node to open its declaration in the code editor.
- The code editor highlights language constructs and marks parser errors inline
  while you type.
- The file manager supports nested project folders; assets can be created,
  renamed, moved, and referenced without depending on their physical path.
