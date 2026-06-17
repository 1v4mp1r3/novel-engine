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

Open a specific project file or project folder:

```powershell
dotnet run --project src/NovelEngine.Editor/NovelEngine.Editor.csproj -- --open-project "D:\Games\MyNovel"
```

Install the Windows Explorer folder context menu item for the current user by
double-clicking `install-context-menu.bat`, or run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/install-explorer-context-menu.ps1
```

Remove it later with `uninstall-context-menu.bat`.

After installation, right-click a project folder, empty space inside it, or a
`*.novel.json` project file and choose `Открыть с помощью Novel Engine`. The
editor will find the project file in that folder and load the project with its
adjacent managed files. Empty folders are opened as a workspace too, so you can
create a new project there with `Ctrl+S`; the editor writes the project file
into the opened folder, creates `files/` for managed game files, and keeps
timestamped backups in `autosaves/`.
The file-level Explorer entry is filtered by file name so it appears for
`*.novel.json` project files without taking over every JSON file on the system.
The startup screen lists recently opened or created projects for quick switching
between workspaces.
Use `File -> Restore autosave...` (`Файл -> Восстановить автосейв...`) to load
a timestamped snapshot from `autosaves/` as unsaved editor state before deciding
whether to overwrite the main project file.
New projects create the default managed asset folders:
`files/characters`, `files/voices`, `files/audio_fx`, `files/audio`, and
`files/backgrounds`.

## Build and test

Double-click `test.bat`, or run:

```powershell
powershell -ExecutionPolicy Bypass -File tools/test.ps1
```

The test script runs the solution build, core tests, and editor-level tests
sequentially. Keep these commands sequential because WPF generated files share
`obj` directories during build.

The C++ runtime requires a separate C++20 compiler and CMake. They are not
currently installed on this workstation.

## Editor basics

- Right-click the canvas to add a scene or dialogue node.
- Use the canvas node template submenu for common scaffolds such as a
  background scene, character line, or two-choice branch.
- Drag from an output circle to the input circle of another node to connect them.
- Use the selected node context menu to create a connected scene or dialogue
  without manually dragging a link.
- Use the selected node template submenu to create connected authoring
  scaffolds in one step.
- Use `Ctrl+D` or the node context menu to duplicate the selected scene or
  dialogue node without copying its graph connections.
- Scene nodes have one linear `Дальше` output.
- Dialogue nodes can have any number of choices.
- Duplicate a dialogue choice to reuse its condition, script, sound, and fade
  settings without copying its graph connection.
- Move dialogue choices up or down to control their in-game order.
- A node may inherit or replace the current background and character set.
- The node context menu can enable inherited background, music, or characters
  and shows what will be inherited when the player enters from each linked node.
- Selected scene characters can be moved up or down in the character list to
  control their scene order.
- Selected scene characters can be duplicated with their sprite, voice, and
  transform settings intact.
- Selected scene characters can be moved left, center, or right from the
  properties panel without opening the full character editor.
- New scene characters are picked from images in `files/characters`; import a
  sprite there first, then add it from the properties panel or from the asset
  context menu.
- A node may inherit music without restarting playback.
- Right-click a connection to configure its transition sound and fade duration.
- Press `F5` to compile and launch the game, `F6` for a debug build,
  `Shift+F5` to stop it, or `Ctrl+F5` to preview the selected node.
- Right-click any node to start its preview.
- In preview/runtime, press `Space`, `Enter`, or left-click to reveal the
  current line immediately; choices still appear only after the line is fully
  shown.
- Open the `Код` tab to author node types, nodes, connections, inheritance,
  assets, and transitions as source code. Press `Ctrl+Enter` to compile it.
- Project JSON can also store visual script blocks for node entry scripts and
  choice transition scripts. Blocks compile to the same runtime commands as the
  text script language, so a future block editor can coexist with hand-written
  code.
- In the node properties panel, use `Блоки скрипта` to edit entry-script blocks;
  in a choice editor, use the same button to attach blocks to that transition.
- Condition and visual-block dialogs suggest variables already used in scripts,
  conditions, and blocks, while still allowing new names.
- Visual-block dialogs can import simple text scripts (`set`, `add`, `unset`,
  comments) into editable blocks.
- Applying source from the `Код` tab preserves existing visual script blocks by
  matching node ids and choice outputs, so block-authored logic is not lost
  during DSL edits.
- Open the `Файлы` tab to import assets into the project and address them from
  code through stable references such as `@city_night` or `@main_theme`.
- Entry scripts and choice conditions use the language documented in
  `docs/script-language.md`.
- The complete project DSL is documented in `docs/project-language.md`.
- Double-click a graph node to open its declaration in the code editor.
- The code editor highlights language constructs and marks parser errors inline
  while you type.
- Code completion suggests properties, snippets, node targets, types, values,
  and assets. Use `Ctrl+Space` to open it explicitly, then `Tab` or `Enter`.
- The code editor avoids persistent block guide overlays so large scripts stay
  responsive while scrolling and typing.
- The top-right IDE controls compile the current project, launch the compiled
  game, launch it with runtime diagnostics, or stop the separate player process.
- Build and run commands perform project diagnostics first: errors block the
  action, while warnings ask for explicit confirmation.
- Double-click a project diagnostic to jump to the affected node, code
  declaration, asset, or visual script block editor.
- Compiled packages are written to `build/<project-name>` beside the project.
  They contain a validated runtime project, manifest, and copied files under
  the same `files/` layout used by editor projects.
- The file manager supports nested project folders; assets can be created,
  renamed, moved, and referenced without depending on their physical path.
- Use the file manager search box to filter assets by id, folder, path, file
  name, or type.
- Audio assets can be previewed directly from the file manager.
- Selected assets can be opened in Windows Explorer from the toolbar or context
  menu.
- Asset usage results can be opened directly to jump back to the node or main
  menu area that references the asset.
- Right-click an image in `files/characters` to create a library character from
  that sprite, add it to the selected node, or attach voice blips from
  `files/voices` to the matching character. If the sprite is not used by any
  character yet, the same menu can create a library character with the selected
  voice blip already attached.
