using NovelEngine.Core;

var tests = new (string Name, Action Run)[]
{
    ("default project is valid", DefaultProjectIsValid),
    ("dialogue choices connect independently", DialogueChoicesConnectIndependently),
    ("project JSON round trip", ProjectJsonRoundTrip),
    ("background and variables flow through transitions", RuntimeStateFlows),
    ("characters flow through transitions", CharactersFlow),
    ("inherited music does not change track", InheritedMusicDoesNotChangeTrack),
    ("transition settings survive JSON round trip", TransitionSettingsRoundTrip),
    ("node preview restores inherited state", NodePreviewRestoresState),
    ("removing a node disconnects incoming outputs", RemovingNodeDisconnectsOutputs),
    ("project language compiles graph and inherited types", ProjectLanguageCompilesGraph),
    ("project language formatter round trips", ProjectLanguageFormatterRoundTrips),
    ("project language rejects cyclic inheritance", ProjectLanguageRejectsCycles),
    ("project language resolves asset references", ProjectLanguageResolvesAssets),
    ("project language rejects mismatched asset kinds", ProjectLanguageRejectsWrongAssetKind),
    ("project asset import copies and registers files", ProjectAssetImportCopiesFiles),
    ("asset folders move and rename physical files", AssetFoldersMoveFiles),
    ("project language preserves asset folders", ProjectLanguagePreservesFolders),
    ("project language exposes syntax and node locations", ProjectLanguageSyntaxAndLocations),
    ("project language suggests context completions", ProjectLanguageSuggestsCompletions),
    ("project language finds nested code scopes", ProjectLanguageFindsScopes),
    ("build compiler emits runnable package", BuildCompilerEmitsPackage),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void DefaultProjectIsValid()
{
    var project = NovelProject.CreateDefault();
    project.Validate();
    Assert(project.Nodes.Count == 3, "Expected start, scene, and dialogue nodes.");
}

static void DialogueChoicesConnectIndependently()
{
    var project = NovelProject.CreateDefault();
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    var firstTarget = project.AddNode(NodeKind.Scene, 1100, 40);
    var secondTarget = project.AddNode(NodeKind.Scene, 1100, 300);

    project.Connect(dialogue.Id, dialogue.Outputs[0].Id, firstTarget.Id);
    project.Connect(dialogue.Id, dialogue.Outputs[1].Id, secondTarget.Id);

    Assert(dialogue.Outputs[0].TargetNodeId == firstTarget.Id, "First choice target was lost.");
    Assert(dialogue.Outputs[1].TargetNodeId == secondTarget.Id, "Second choice target was lost.");
}

static void ProjectJsonRoundTrip()
{
    var project = NovelProject.CreateDefault();
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    dialogue.Script = "set met_hero = true";
    dialogue.Outputs[0].Condition = "met_hero";

    var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));

    Assert(restored.Nodes.Count == project.Nodes.Count, "Node count changed.");
    Assert(
        restored.FindNode(dialogue.Id)?.Outputs[0].Condition == "met_hero",
        "Choice condition changed.");
}

static void RuntimeStateFlows()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    scene.Background = "backgrounds/room.png";
    scene.InheritBackground = false;
    scene.Script = "set score = 1";
    dialogue.Script = "add score 2";

    var player = new NovelPlayer(project);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    player.Choose(scene.Outputs[0].Id);

    Assert(player.CurrentNode?.Id == dialogue.Id, "Runtime did not reach dialogue.");
    Assert(
        player.State.CurrentBackground == "backgrounds/room.png",
        "Dialogue did not inherit background.");
    Assert(
        Convert.ToDouble(player.State.Variables["score"]) == 3,
        "Script variables did not flow through transitions.");
}

static void RemovingNodeDisconnectsOutputs()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var start = project.Nodes.Single(node => node.Kind == NodeKind.Start);

    Assert(project.RemoveNode(scene.Id), "Scene was not removed.");
    Assert(start.Outputs[0].TargetNodeId is null, "Incoming output remained connected.");
}

static void CharactersFlow()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            Sprite = "characters/hero.png",
            Position = CharacterPosition.Left,
        });
    dialogue.InheritCharacters = true;

    var player = new NovelPlayer(project);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    player.Choose(scene.Outputs[0].Id);

    Assert(player.State.CurrentCharacters.Count == 1, "Character was not inherited.");
    Assert(player.State.CurrentCharacters[0].Name == "Герой", "Wrong character inherited.");
}

static void NodePreviewRestoresState()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    scene.Background = "backgrounds/street.png";
    scene.InheritBackground = false;
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "friend",
            Name = "Друг",
            Position = CharacterPosition.Right,
        });
    scene.Script = "set route = \"street\"";

    var player = new NovelPlayer(project);
    player.StartAt(dialogue.Id);

    Assert(
        player.State.CurrentBackground == "backgrounds/street.png",
        "Preview did not restore background.");
    Assert(player.State.CurrentCharacters.Count == 1, "Preview did not restore characters.");
    Assert(
        Convert.ToString(player.State.Variables["route"]) == "street",
        "Preview did not restore script variables.");
}

static void InheritedMusicDoesNotChangeTrack()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    scene.Music = "music/theme.mp3";
    scene.InheritMusic = false;
    dialogue.InheritMusic = true;

    var player = new NovelPlayer(project);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    var musicBeforeDialogue = player.State.CurrentMusic;
    player.Choose(scene.Outputs[0].Id);

    Assert(musicBeforeDialogue == "music/theme.mp3", "Scene music was not applied.");
    Assert(
        player.State.CurrentMusic == musicBeforeDialogue,
        "Inherited music changed while entering dialogue.");
}

static void TransitionSettingsRoundTrip()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var output = scene.Outputs[0];
    output.TransitionSound = "sounds/whoosh.wav";
    output.FadeDurationMs = 725;

    var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
    var restoredOutput = restored.FindOutput(scene.Id, output.Id);

    Assert(
        restoredOutput?.TransitionSound == "sounds/whoosh.wav",
        "Transition sound changed.");
    Assert(restoredOutput?.FadeDurationMs == 725, "Fade duration changed.");
}

static void ProjectLanguageCompilesGraph()
{
    const string source = """
        novel "Ночная история"

        type NightScene extends scene {
            background "backgrounds/night.png"
            inherit background false
        }

        type DramaticNight extends NightScene {
            music "music/drama.mp3"
            inherit music false
        }

        node start : start at (20, 180) {
            title "Начало"
            next "Дальше" -> intro
        }

        node intro : DramaticNight at (280, 120) {
            title "Ночная улица"
            text "Город уснул."
            next "К выбору" -> choice {
                fade 600
                sound "sounds/whoosh.wav"
            }
        }

        node choice : dialogue at (560, 120) {
            speaker "Герой"
            text "Куда идти?"
            choice "Домой" -> none {
                when "score >= 2"
                script "add score 1"
            }
        }
        """;

    var project = ProjectLanguage.Parse(source);
    var intro = project.FindNode("intro");
    var choice = project.FindNode("choice");

    Assert(project.Title == "Ночная история", "Project title was not compiled.");
    Assert(project.NodeTypes.Count == 2, "Custom node types were not retained.");
    Assert(intro?.Kind == NodeKind.Scene, "Derived scene type lost its polymorphic kind.");
    Assert(intro?.TypeName == "DramaticNight", "Node type name was not retained.");
    Assert(
        intro?.Background == "backgrounds/night.png",
        "Background was not inherited through the type chain.");
    Assert(intro?.Music == "music/drama.mp3", "Derived type did not override music.");
    Assert(intro?.Outputs[0].TargetNodeId == choice?.Id, "Connection was not compiled.");
    Assert(intro?.Outputs[0].FadeDurationMs == 600, "Fade setting was not compiled.");
    Assert(
        choice?.Outputs[0].Condition == "score >= 2",
        "Choice condition was not compiled.");

    intro!.X += 10;
    var formatted = ProjectLanguage.Format(project);
    Assert(
        CountOccurrences(formatted, "background \"backgrounds/night.png\"") == 1,
        "Graph synchronization flattened an inherited background into the node.");
    Assert(
        CountOccurrences(formatted, "music \"music/drama.mp3\"") == 1,
        "Graph synchronization flattened inherited music into the node.");
}

static void ProjectLanguageFormatterRoundTrips()
{
    var original = NovelProject.CreateDefault();
    var code = ProjectLanguage.Format(original);
    var restored = ProjectLanguage.Parse(code);

    Assert(code.Contains("Новая новелла", StringComparison.Ordinal), "Unicode text was escaped.");
    Assert(restored.Nodes.Count == original.Nodes.Count, "Formatted node count changed.");
    Assert(
        restored.FindNode("start")?.Outputs[0].TargetNodeId == "scene-1",
        "Formatted start connection changed.");
    Assert(
        restored.FindNode("scene-1")?.Outputs[0].TargetNodeId == "dialogue-1",
        "Formatted scene connection changed.");
}

static void ProjectLanguageRejectsCycles()
{
    const string source = """
        novel "Cycle"
        type A extends B {
        }
        type B extends A {
        }
        node start : start {
            next "Дальше" -> none
        }
        """;

    try
    {
        _ = ProjectLanguage.Parse(source);
        throw new InvalidOperationException("Cyclic inheritance was accepted.");
    }
    catch (ProjectLanguageException error)
    {
        Assert(
            error.Message.Contains("Циклическое наследование", StringComparison.Ordinal),
            "Cycle diagnostic was not specific.");
    }
}

static void ProjectLanguageResolvesAssets()
{
    const string source = """
        novel "Assets"

        asset night : image "assets/images/night.png"
        asset theme : audio "assets/audio/theme.mp3"

        node start : start {
            title "Start"
            next "Go" -> scene
        }

        node scene : scene {
            title "Night"
            inherit background false
            background @night
            inherit music false
            music @theme
            next "End" -> none
        }
        """;

    var project = ProjectLanguage.Parse(source);
    var scene = project.FindNode("scene");

    Assert(project.Assets.Count == 2, "Asset declarations were not compiled.");
    Assert(
        project.ResolveAssetReference(scene!.Background) == "assets/images/night.png",
        "Background asset reference was not resolved.");
    Assert(
        project.ResolveAssetReference(scene.Music) == "assets/audio/theme.mp3",
        "Music asset reference was not resolved.");

    project.ReplaceAssetReference("night", "@city_night");
    project.FindAsset("night")!.Id = "city_night";
    Assert(scene.Background == "@city_night", "Asset rename did not update usages.");
    var formatted = ProjectLanguage.Format(project);
    Assert(formatted.Contains("background @city_night"), "Asset reference was quoted.");
}

static void ProjectLanguageRejectsWrongAssetKind()
{
    const string source = """
        novel "Wrong asset"
        asset theme : audio "assets/audio/theme.mp3"
        node start : start {
            next "Go" -> scene
        }
        node scene : scene {
            inherit background false
            background @theme
            next "End" -> none
        }
        """;

    try
    {
        _ = ProjectLanguage.Parse(source);
        throw new InvalidOperationException("Audio asset was accepted as a background.");
    }
    catch (ProjectLanguageException error)
    {
        Assert(
            error.Message.Contains("требуется image", StringComparison.Ordinal),
            "Wrong asset kind diagnostic was not specific.");
    }
}

static void ProjectAssetImportCopiesFiles()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-assets-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var source = Path.Combine(directory, "Ночной фон.png");
        File.WriteAllBytes(source, [137, 80, 78, 71]);
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();

        var asset = ProjectAssets.Import(project, projectPath, source);
        var target = ProjectAssets.ResolvePath(projectPath, asset);

        Assert(asset.Kind == AssetKind.Image, "Imported image kind was not detected.");
        Assert(AssetReference.IsValidId(asset.Id), "Generated asset id is invalid.");
        Assert(File.Exists(target), "Imported file was not copied into the project.");
        Assert(
            target.Contains(
                Path.Combine("assets", "images"),
                StringComparison.OrdinalIgnoreCase),
            "Imported image was placed in the wrong folder.");
        Assert(
            ReferenceEquals(
                asset,
                ProjectAssets.Import(project, projectPath, target)),
            "Importing the registered file created a duplicate asset.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AssetFoldersMoveFiles()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-folders-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var source = Path.Combine(directory, "hero.png");
        File.WriteAllBytes(source, [137, 80, 78, 71]);
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        var asset = ProjectAssets.Import(project, projectPath, source);

        ProjectAssets.CreateFolder(project, "characters/heroes");
        ProjectAssets.MoveAsset(
            project,
            projectPath,
            asset,
            "characters/heroes");
        Assert(
            asset.Folder == "characters/heroes",
            "Asset was not moved into the nested folder.");
        Assert(
            File.Exists(ProjectAssets.ResolvePath(projectPath, asset)),
            "Physical asset disappeared after moving.");

        ProjectAssets.RenameFolder(
            project,
            projectPath,
            "characters",
            "cast");
        Assert(
            asset.Folder == "cast/heroes",
            "Nested asset folder was not updated after rename.");
        Assert(
            File.Exists(ProjectAssets.ResolvePath(projectPath, asset)),
            "Physical asset disappeared after folder rename.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectLanguagePreservesFolders()
{
    const string source = """
        novel "Folders"
        folder "characters"
        folder "characters/heroes"
        asset hero : image "assets/characters/heroes/hero.png" in "characters/heroes"
        node start : start {
            next "End" -> none
        }
        """;

    var project = ProjectLanguage.Parse(source);
    var formatted = ProjectLanguage.Format(project);
    var restored = ProjectLanguage.Parse(formatted);

    Assert(
        restored.AssetFolders.Contains("characters/heroes"),
        "Nested folder was lost during language round trip.");
    Assert(
        restored.FindAsset("hero")?.Folder == "characters/heroes",
        "Asset folder assignment was lost.");
}

static void ProjectLanguageSyntaxAndLocations()
{
    const string source = """
        novel "Syntax"
        # comment
        asset bg : image "assets/bg.png"
        node start : start {
            background @bg
            next "End" -> scene-1
        }
        node scene-1 : scene {
            next "End" -> none
        }
        """;

    var spans = ProjectLanguage.GetSyntaxSpans(source);
    var location = ProjectLanguage.FindNodeDeclaration(source, "start");

    Assert(
        spans.Any(span => span.Kind == ProjectLanguageSyntaxKind.Keyword),
        "Keyword syntax spans were not produced.");
    Assert(
        spans.Any(span => span.Kind == ProjectLanguageSyntaxKind.Comment),
        "Comment syntax spans were not produced.");
    Assert(
        spans.Any(span => span.Kind == ProjectLanguageSyntaxKind.AssetReference),
        "Asset reference syntax spans were not produced.");
    var sceneIdStart = source.IndexOf("scene-1", StringComparison.Ordinal);
    Assert(
        spans.Any(
            span => span.Start == sceneIdStart
                && span.Length == "scene-1".Length
                && span.Kind == ProjectLanguageSyntaxKind.Declaration),
        "Hyphenated identifiers were split into partially highlighted spans.");
    Assert(location is not null, "Node declaration location was not found.");
    Assert(
        source.Substring(location!.Start, location.Length) == "start",
        "Node declaration location points to the wrong text.");
}

static void ProjectLanguageSuggestsCompletions()
{
    const string source = """
        novel "Completions"
        asset city_bg : image "assets/city.png"
        node start : start {
            background @ci
            next "Go" -> dia
        }
        node dialogue-1 : dialogue {
            choice "End" -> none
        }
        """;

    var assetCaret = source.IndexOf("@ci", StringComparison.Ordinal) + 3;
    var assetCompletions = ProjectLanguage.GetCompletions(source, assetCaret);
    Assert(
        assetCompletions.Items.Any(item => item.InsertText == "@city_bg"),
        "Asset completion was not suggested after @.");

    var nodeCaret = source.IndexOf("-> dia", StringComparison.Ordinal) + 6;
    var nodeCompletions = ProjectLanguage.GetCompletions(source, nodeCaret);
    Assert(
        nodeCompletions.Items.Any(item => item.InsertText == "dialogue-1"),
        "Node completion was not suggested after an arrow.");
}

static void ProjectLanguageFindsScopes()
{
    const string source = """
        node scene-1 : scene {
            text "{ this is not a scope }"
            character hero {
                name "Hero"
            }
            # { comment }
        }
        """;

    var scopes = ProjectLanguage.GetScopeSpans(source);
    Assert(scopes.Count == 2, "Braces inside strings or comments became scopes.");
    Assert(
        scopes.Any(scope => scope.Depth == 0)
            && scopes.Any(scope => scope.Depth == 1),
        "Nested scope depths were not detected.");
}

static void BuildCompilerEmitsPackage()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-build-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var sourceAsset = Path.Combine(directory, "city.png");
        File.WriteAllBytes(sourceAsset, [137, 80, 78, 71]);
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        var asset = ProjectAssets.Import(
            project,
            projectPath,
            sourceAsset,
            "backgrounds");
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = AssetReference.Create(asset.Id);
        scene.InheritBackground = false;
        project.SourceCode = ProjectLanguage.Format(project);
        ProjectSerializer.Save(project, projectPath);

        var result = NovelBuildCompiler.Compile(
            project,
            projectPath,
            Path.Combine(directory, "build", "story"),
            debugSymbols: true);
        var loaded = NovelBuildCompiler.LoadBuild(result.ManifestPath);
        var builtAsset = loaded.Project.FindAsset(asset.Id)
            ?? throw new InvalidOperationException("Built asset is missing.");

        Assert(File.Exists(result.ManifestPath), "Build manifest was not emitted.");
        Assert(
            File.Exists(result.RuntimeProjectPath),
            "Runtime project was not emitted.");
        Assert(
            File.Exists(
                Path.Combine(
                    result.OutputDirectory,
                    builtAsset.Path.Replace(
                        '/',
                        Path.DirectorySeparatorChar))),
            "Compiled asset was not copied.");
        Assert(
            loaded.Manifest.DebugSymbols,
            "Debug build did not retain debug symbols.");
        Assert(
            loaded.Project.Nodes.Count == project.Nodes.Count,
            "Runtime project changed node count.");

        var foreignDirectory = Path.Combine(directory, "foreign-output");
        Directory.CreateDirectory(foreignDirectory);
        var foreignFile = Path.Combine(foreignDirectory, "keep.txt");
        File.WriteAllText(foreignFile, "do not delete");
        try
        {
            _ = NovelBuildCompiler.Compile(
                project,
                projectPath,
                foreignDirectory,
                debugSymbols: false);
            throw new InvalidOperationException(
                "Compiler overwrote a non-build directory.");
        }
        catch (InvalidDataException)
        {
            Assert(
                File.Exists(foreignFile),
                "Compiler deleted files from a non-build directory.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static int CountOccurrences(string source, string value)
{
    var count = 0;
    var offset = 0;
    while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
    {
        count++;
        offset += value.Length;
    }
    return count;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
