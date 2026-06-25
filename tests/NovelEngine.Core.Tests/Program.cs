using NovelEngine.Core;

var tests = new (string Name, Action Run)[]
{
    ("default project is valid", DefaultProjectIsValid),
    ("project diagnostics report authoring issues", ProjectDiagnosticsReportAuthoringIssues),
    ("project diagnostics check physical assets", ProjectDiagnosticsCheckPhysicalAssets),
    ("project diagnostics check character voice references", ProjectDiagnosticsCheckCharacterVoiceReferences),
    ("project diagnostics survives duplicate starts", ProjectDiagnosticsSurvivesDuplicateStarts),
    ("project diagnostics report caches summary", ProjectDiagnosticsReportCachesSummary),
    ("project diagnostics analyzes large graph quickly", ProjectDiagnosticsAnalyzesLargeGraphQuickly),
    ("character library survives JSON and DSL", CharacterLibraryRoundTrip),
    ("dialogue choices connect independently", DialogueChoicesConnectIndependently),
    ("core graph lookups use direct loops", CoreGraphLookupsUseDirectLoops),
    ("adding connected nodes preserves existing graph", AddConnectedNodePreservesExistingGraph),
    ("duplicating nodes copies authoring data safely", DuplicateNodeCopiesAuthoringDataSafely),
    ("duplicating dialogue choices copies authoring data safely", DuplicateDialogueChoiceCopiesAuthoringDataSafely),
    ("moving dialogue choices preserves data and boundaries", MoveDialogueChoicesPreservesDataAndBoundaries),
    ("node templates create authoring scaffolds", NodeTemplatesCreateAuthoringScaffolds),
    ("project JSON round trip", ProjectJsonRoundTrip),
    ("project save cleans temporary file on failure", ProjectSaveCleansTemporaryFileOnFailure),
    ("background and variables flow through transitions", RuntimeStateFlows),
    ("novel script applies numeric commands", NovelScriptAppliesNumericCommands),
    ("novel script toggles variables", NovelScriptTogglesVariables),
    ("novel script rejects invalid add operands", NovelScriptRejectsInvalidAddOperands),
    ("visual script blocks survive JSON and runtime", VisualScriptBlocksRoundTripAndRun),
    ("visual script blocks import simple scripts", VisualScriptBlocksImportSimpleScripts),
    ("visual script blocks describe semantic labels", VisualScriptBlocksDescribeSemanticLabels),
    ("visual script blocks clone for paste safely", VisualScriptBlocksCloneForPasteSafely),
    ("visual script blocks validate generated scripts", VisualScriptBlocksValidateGeneratedScripts),
    ("visual script blocks survive project language apply", VisualScriptBlocksSurviveProjectLanguageApply),
    ("project script variables collect authored names", ProjectScriptVariablesCollectAuthoredNames),
    ("visual conditions parse and compile", VisualConditionsParseAndCompile),
    ("visual condition expressions survive JSON and runtime", VisualConditionExpressionsSurviveJsonAndRuntime),
    ("characters flow through transitions", CharactersFlow),
    ("node character operations preserve data and order", NodeCharacterOperationsPreserveDataAndOrder),
    ("inherited music does not change track", InheritedMusicDoesNotChangeTrack),
    ("runtime save restores node and script state", RuntimeSaveRestoresState),
    ("transition settings survive JSON round trip", TransitionSettingsRoundTrip),
    ("node preview restores inherited state", NodePreviewRestoresState),
    ("choice availability waits for dialogue typing", ChoiceAvailabilityWaitsForDialogueTyping),
    ("novel player follows large graph quickly", NovelPlayerFollowsLargeGraphQuickly),
    ("removing a node disconnects incoming outputs", RemovingNodeDisconnectsOutputs),
    ("project language compiles graph and inherited types", ProjectLanguageCompilesGraph),
    ("project language formatter round trips", ProjectLanguageFormatterRoundTrips),
    ("character transforms and voice survive code and JSON", CharacterTransformsRoundTrip),
    ("main menu and character voice survive JSON", MainMenuAndVoiceRoundTrip),
    ("main menu and voice assets participate in asset references", MainMenuAndVoiceAssetReferences),
    ("character voice sounds normalize without linq", CharacterVoiceSoundsNormalizeWithoutLinq),
    ("asset reference lookups avoid linq pipelines", AssetReferenceLookupsAvoidLinqPipelines),
    ("removed voice assets are cleared from characters", RemovedVoiceAssetsAreClearedFromCharacters),
    ("voice blip generator emits wav files", VoiceBlipGeneratorEmitsWav),
    ("voice sound picker supports multiple blips", VoiceSoundPickerSupportsMultipleBlips),
    ("voice playback cadence skips expected characters", VoicePlaybackCadenceSkipsExpectedCharacters),
    ("project language rejects cyclic inheritance", ProjectLanguageRejectsCycles),
    ("project language resolves asset references", ProjectLanguageResolvesAssets),
    ("project language rejects mismatched asset kinds", ProjectLanguageRejectsWrongAssetKind),
    ("project validation caches asset lookup", ProjectValidationCachesAssetLookup),
    ("project asset import copies and registers files", ProjectAssetImportCopiesFiles),
    ("project asset import many reuses lookups", ProjectAssetImportManyReusesLookups),
    ("project default file structure is created", ProjectDefaultFileStructureIsCreated),
    ("project asset sync discovers files from disk", ProjectAssetSyncDiscoversFilesFromDisk),
    ("project asset sync preserves managed file paths", ProjectAssetSyncPreservesManagedFilePaths),
    ("project asset sync normalizes existing path separators", ProjectAssetSyncNormalizesExistingPathSeparators),
    ("project asset sync removes missing managed files", ProjectAssetSyncRemovesMissingManagedFiles),
    ("project asset sync removes missing files root assets", ProjectAssetSyncRemovesMissingFilesRootAssets),
    ("project asset sync caches generated lookups", ProjectAssetSyncCachesGeneratedLookups),
    ("asset folders move and rename physical files", AssetFoldersMoveFiles),
    ("asset folder delete scans directories once", AssetFolderDeleteScansDirectoriesOnce),
    ("project language preserves asset folders", ProjectLanguagePreservesFolders),
    ("project language exposes syntax and node locations", ProjectLanguageSyntaxAndLocations),
    ("project language syntax spans respect limit", ProjectLanguageSyntaxSpansRespectLimit),
    ("project language suggests context completions", ProjectLanguageSuggestsCompletions),
    ("project language completes noisy scopes quickly", ProjectLanguageCompletesNoisyScopesQuickly),
    ("project language finds nested code scopes", ProjectLanguageFindsScopes),
    ("build compiler preserves visual script blocks", BuildCompilerPreservesVisualScriptBlocks),
    ("build compiler copies visual blocks with node lookup", BuildCompilerCopiesVisualBlocksWithNodeLookup),
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

static void ProjectDiagnosticsReportAuthoringIssues()
{
    var project = NovelProject.CreateDefault();
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    dialogue.Speaker = string.Empty;
    dialogue.Script = "add score nope";
    dialogue.Outputs[0].Script = "dance";
    dialogue.Outputs[0].ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "broken-choice-block",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "bad-variable",
            Value = "1",
        });
    var orphan = project.AddNode(NodeKind.Scene, 900, 120);
    orphan.Title = "Lost scene";

    var report = ProjectDiagnostics.Analyze(project);

    Assert(report.ErrorCount > 0, "Broken choice script was not reported as an error.");
    Assert(
        report.Diagnostics.Any(
            diagnostic => diagnostic.Message.Contains("говорящий", StringComparison.Ordinal)),
        "Missing dialogue speaker warning was not reported.");
    Assert(
        report.Diagnostics.Any(
            diagnostic => diagnostic.Message.Contains("нет пути", StringComparison.Ordinal)),
        "Unreachable node warning was not reported.");
    Assert(
        report.Diagnostics.Any(
            diagnostic => diagnostic.Location.Contains("visual blocks", StringComparison.Ordinal)
                && diagnostic.Message.Contains("bad-variable", StringComparison.Ordinal)),
        "Broken visual script block location was not reported.");
    Assert(
        report.Diagnostics.Any(
            diagnostic => diagnostic.Message.Contains("ожидает число", StringComparison.Ordinal)),
        "Invalid add operand was not reported as a script diagnostic.");
}

static void ProjectDiagnosticsCheckPhysicalAssets()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-diagnostics-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        project.Assets.Add(
            new NovelAsset
            {
                Id = "missing_bg",
                Kind = AssetKind.Image,
                Path = "files/backgrounds/missing.png",
                Folder = "backgrounds",
            });
        project.AssetFolders.Add("backgrounds");
        project.MainMenu.Elements.Add(
            new MainMenuElement
            {
                Id = "missing_logo",
                Kind = MainMenuElementKind.ImageLabel,
                Image = "files/ui/missing-logo.png",
            });
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = "@missing_bg";

        var report = ProjectDiagnostics.Analyze(project, projectPath);

        Assert(report.HasErrors, "Missing physical asset was not reported as an error.");
        Assert(
            report.Diagnostics.Any(
                diagnostic => diagnostic.Message.Contains("Файл не найден", StringComparison.Ordinal)),
            "Missing physical asset diagnostic was not specific.");
        Assert(
            report.Diagnostics.Any(
                diagnostic => diagnostic.Location.Contains("Главное меню", StringComparison.Ordinal)
                    && diagnostic.Message.Contains("missing-logo.png", StringComparison.Ordinal)),
            "Missing direct main menu image was not reported.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectDiagnosticsCheckCharacterVoiceReferences()
{
    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset
        {
            Id = "alice_sprite",
            Kind = AssetKind.Image,
            Path = "files/characters/alice.png",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "alice_voice",
            Kind = AssetKind.Audio,
            Path = "files/voices/alice.wav",
        });
    project.Characters.Add(
        new CharacterPlacement
        {
            Id = "alice",
            Name = "Alice",
            Sprite = "@alice_sprite",
            VoiceSounds = ["@alice_voice", "@missing_voice"],
        });

    var report = ProjectDiagnostics.Analyze(project);

    Assert(report.HasErrors, "Missing character voice asset reference was not reported.");
    Assert(
        report.Diagnostics.Any(
            diagnostic =>
                diagnostic.Location.Contains("Проект", StringComparison.Ordinal)
                && diagnostic.Message.Contains("@missing_voice", StringComparison.Ordinal)
                && diagnostic.Message.Contains("библиотека персонажей", StringComparison.Ordinal)),
        "Missing character voice diagnostic did not point to the library character.");
}

static void ProjectDiagnosticsSurvivesDuplicateStarts()
{
    var project = NovelProject.CreateDefault();
    project.Nodes.Add(
        new NovelNode
        {
            Id = "start-two",
            Kind = NodeKind.Start,
            TypeName = "start",
            Title = "Second start",
            Text = "Broken duplicate start",
        });

    var report = ProjectDiagnostics.Analyze(project);

    Assert(
        report.HasErrors,
        "Duplicate start project should still report validation errors.");
}

static void ProjectDiagnosticsReportCachesSummary()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectDiagnostics.cs"));
    var constructorBody = ExtractMethodBody(source, "public ProjectDiagnosticReport");
    var diagnostics = new List<ProjectDiagnostic>
    {
        new(ProjectDiagnosticSeverity.Error, "Нода «start»", "Нет выхода."),
        new(ProjectDiagnosticSeverity.Warning, "Ассет @bg", "Не используется."),
        new(ProjectDiagnosticSeverity.Info, "Ассет @voice", "Можно удалить."),
    };
    var report = new ProjectDiagnosticReport(diagnostics);
    var same = new ProjectDiagnosticReport(diagnostics);
    var changed = new ProjectDiagnosticReport(
    [
        new ProjectDiagnostic(ProjectDiagnosticSeverity.Error, "Нода «start»", "Нет выхода."),
        new ProjectDiagnostic(ProjectDiagnosticSeverity.Warning, "Ассет @bg", "Не используется."),
        new ProjectDiagnostic(ProjectDiagnosticSeverity.Info, "Ассет @voice", "Другое сообщение."),
    ]);

    diagnostics.Add(
        new ProjectDiagnostic(ProjectDiagnosticSeverity.Error, "Позднее", "Не должно попасть."));

    Assert(report.ErrorCount == 1, "Report should cache error count.");
    Assert(report.WarningCount == 1, "Report should cache warning count.");
    Assert(report.InfoCount == 1, "Report should cache info count.");
    Assert(report.Diagnostics.Count == 3, "Report diagnostics should be snapshot-stable.");
    Assert(report.Fingerprint == same.Fingerprint, "Same diagnostics should keep the same fingerprint.");
    Assert(report.Fingerprint != changed.Fingerprint, "Changed diagnostics should change fingerprint.");
    Assert(
        constructorBody.Contains("var snapshot = new ProjectDiagnostic[diagnostics.Count];", StringComparison.Ordinal)
            && constructorBody.Contains("for (var index = 0; index < diagnostics.Count; index++)", StringComparison.Ordinal)
            && !constructorBody.Contains(".ToArray(", StringComparison.Ordinal)
            && !constructorBody.Contains("foreach", StringComparison.Ordinal),
        "Diagnostic report should snapshot and summarize diagnostics in one indexed pass.");
}

static void ProjectDiagnosticsAnalyzesLargeGraphQuickly()
{
    const int sceneCount = 2_000;
    var project = new NovelProject { Title = "Large diagnostics graph" };
    project.Nodes.Add(
        new NovelNode
        {
            Id = "start",
            Kind = NodeKind.Start,
            TypeName = "start",
            Title = "Start",
            Text = "Start",
            Outputs =
            {
                new NodeOutput
                {
                    Id = "out-start",
                    TargetNodeId = "scene-0",
                },
            },
        });
    for (var index = 0; index < sceneCount; index++)
    {
        project.Nodes.Add(
            new NovelNode
            {
                Id = $"scene-{index}",
                Kind = NodeKind.Scene,
                TypeName = "scene",
                Title = $"Scene {index}",
                Text = "Scene text",
                Outputs =
                {
                    new NodeOutput
                    {
                        Id = $"out-scene-{index}",
                        TargetNodeId = index + 1 < sceneCount
                            ? $"scene-{index + 1}"
                            : null,
                    },
                },
            });
    }

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectDiagnostics.cs"));
    var graphBody = ExtractMethodBody(source, "private static void AddGraphDiagnostics");
    var lookupBody = ExtractMethodBody(source, "private static IReadOnlyDictionary<string, NovelNode> BuildUniqueNodesById");
    var unreachableBody = ExtractMethodBody(source, "private static void AddUnreachableNodeDiagnostics");
    Assert(
        graphBody.Contains("BuildUniqueNodesById(project.Nodes)", StringComparison.Ordinal),
        "Graph diagnostics should use the single-pass node lookup helper.");
    Assert(
        !lookupBody.Contains("GroupBy", StringComparison.Ordinal),
        "Graph diagnostics node lookup should not allocate LINQ groups.");
    Assert(
        !unreachableBody.Contains(".SingleOrDefault(", StringComparison.Ordinal)
            && !unreachableBody.Contains(".Select(", StringComparison.Ordinal)
            && !unreachableBody.Contains(".Where(", StringComparison.Ordinal),
        "Reachability diagnostics should use direct loops and tolerate invalid graphs.");

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var report = ProjectDiagnostics.Analyze(project);
    stopwatch.Stop();

    Assert(
        !report.Diagnostics.Any(diagnostic =>
            diagnostic.Message.Contains("нет пути", StringComparison.Ordinal)),
        "Large connected graph was reported as unreachable.");
    Assert(
        stopwatch.ElapsedMilliseconds < 2_000,
        $"Large graph diagnostics are too slow: {stopwatch.ElapsedMilliseconds}ms.");
}

static void CharacterLibraryRoundTrip()
{
    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset
        {
            Id = "alice_sprite",
            Kind = AssetKind.Image,
            Path = "files/characters/alice.png",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "alice_voice",
            Kind = AssetKind.Audio,
            Path = "files/voices/alice.wav",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "alice_voice_alt",
            Kind = AssetKind.Audio,
            Path = "files/voices/alice-alt.wav",
        });
    project.Characters.Add(
        new CharacterPlacement
        {
            Id = "alice",
            Name = "Alice",
            Sprite = "@alice_sprite",
            Position = CharacterPosition.Left,
            VoiceSound = "@alice_voice",
            VoiceSounds = ["@alice_voice", "@alice_voice_alt"],
            VoicePitch = 1.2,
            VoiceEveryNthCharacter = 2,
        });

    var fromJson = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
    var formatted = ProjectLanguage.Format(project);
    var fromCode = ProjectLanguage.Parse(formatted);

    Assert(fromJson.Characters.Count == 1, "JSON lost character library entries.");
    Assert(fromCode.Characters.Count == 1, "Project language lost character library entries.");
    Assert(
        formatted.Contains("character alice", StringComparison.Ordinal),
        "Character library was not formatted as a top-level character block.");
    Assert(
        fromCode.Characters[0].Sprite == "@alice_sprite",
        "Character library sprite reference changed.");
    Assert(
        fromCode.CountAssetReferences("alice_voice") == 1,
        "Character library voice reference was not counted.");
    Assert(
        fromCode.Characters[0].VoiceSounds.Count == 2
            && fromCode.Characters[0].VoiceSounds.Contains("@alice_voice_alt"),
        "Character library lost multiple voice blips.");
    Assert(
        fromCode.CountAssetReferences("alice_voice_alt") == 1,
        "Character library alternate voice reference was not counted.");
    Assert(
        fromCode.FindAssetUsages("alice_voice").Single().Location.Contains(
            "библиотека персонажей",
            StringComparison.Ordinal),
        "Character library voice usage location was not reported.");
    Assert(
        fromCode.FindAssetUsages("alice_voice_alt").Single().Location.Contains(
            "библиотека персонажей",
            StringComparison.Ordinal),
        "Character library alternate voice usage location was not reported.");
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

static void CoreGraphLookupsUseDirectLoops()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var findAssetBody = ExtractMethodBody(
        sourceText,
        "public NovelAsset? FindAsset");
    var findCharacterBody = ExtractMethodBody(
        sourceText,
        "public CharacterPlacement? FindCharacter");
    var findNodeBody = ExtractMethodBody(
        sourceText,
        "public NovelNode? FindNode");
    var findOutputBody = ExtractMethodBody(
        sourceText,
        "public NodeOutput? FindOutput");
    var findOutputCoreBody = ExtractMethodBody(
        sourceText,
        "private static NodeOutput? FindOutput");
    var findFreeOutputBody = ExtractMethodBody(
        sourceText,
        "private static NodeOutput? FindFirstFreeOutput");
    var findNodeCharacterBody = ExtractMethodBody(
        sourceText,
        "private static CharacterPlacement? FindNodeCharacter");
    var countNodesBody = ExtractMethodBody(
        sourceText,
        "private int CountNodes");
    var addNodeBody = ExtractMethodBody(
        sourceText,
        "public NovelNode AddNode");
    var duplicateOutputBody = ExtractMethodBody(
        sourceText,
        "public NodeOutput DuplicateOutput");
    var setCharacterPositionBody = ExtractMethodBody(
        sourceText,
        "public bool SetCharacterPosition");

    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset
        {
            Id = "City_BG",
            Kind = AssetKind.Image,
            Path = "files/backgrounds/city.png",
        });
    project.Characters.Add(new CharacterPlacement { Id = "hero", Name = "Hero" });
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);

    Assert(project.FindAsset("city_bg")?.Id == "City_BG", "Asset lookup changed case-insensitive behavior.");
    Assert(project.FindCharacter("hero")?.Name == "Hero", "Library character lookup failed.");
    Assert(project.FindNode(scene.Id) == scene, "Node lookup failed.");
    Assert(project.FindOutput(scene.Id, scene.Outputs[0].Id) == scene.Outputs[0], "Output lookup failed.");
    Assert(project.FindOutput("missing", scene.Outputs[0].Id) is null, "Missing node output lookup should return null.");
    Assert(
        findAssetBody.Contains("foreach (var asset in Assets)", StringComparison.Ordinal)
            && findCharacterBody.Contains("foreach (var character in Characters)", StringComparison.Ordinal)
            && findNodeBody.Contains("foreach (var node in Nodes)", StringComparison.Ordinal)
            && findOutputCoreBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal)
            && findFreeOutputBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal)
            && findNodeCharacterBody.Contains("foreach (var character in node.Characters)", StringComparison.Ordinal)
            && countNodesBody.Contains("foreach (var node in Nodes)", StringComparison.Ordinal),
        "Core graph lookup helpers should use direct loops.");
    Assert(
        !findAssetBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findCharacterBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findNodeBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findOutputBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findOutputCoreBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findFreeOutputBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !findNodeCharacterBody.Contains("FirstOrDefault", StringComparison.Ordinal),
        "Core graph lookup helpers should avoid LINQ FirstOrDefault delegates.");
    Assert(
        addNodeBody.Contains("var index = CountNodes(kind) + 1;", StringComparison.Ordinal)
            && !addNodeBody.Contains("Nodes.Count(", StringComparison.Ordinal),
        "Node creation should count existing nodes through a direct helper.");
    Assert(
        duplicateOutputBody.Contains("var source = FindOutput(node, outputId)", StringComparison.Ordinal)
            && setCharacterPositionBody.Contains("FindNodeCharacter(node, characterId)", StringComparison.Ordinal),
        "Graph editing operations should reuse direct lookup helpers.");
}

static void AddConnectedNodePreservesExistingGraph()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);

    scene.Outputs[0].TargetNodeId = null;
    var connected = project.AddConnectedNode(scene.Id, NodeKind.Dialogue, scene.X + 400, scene.Y);

    Assert(connected.Kind == NodeKind.Dialogue, "Connected dialogue node was not created.");
    Assert(scene.Outputs[0].TargetNodeId == connected.Id, "Connected node was not wired to the free output.");
    Assert(connected.X == scene.X + 400 && connected.Y == scene.Y, "Connected node position changed.");

    var firstTarget = project.AddNode(NodeKind.Scene, 1300, 50);
    var secondTarget = project.AddNode(NodeKind.Scene, 1300, 280);
    dialogue.Outputs[0].TargetNodeId = firstTarget.Id;
    dialogue.Outputs[1].TargetNodeId = secondTarget.Id;
    var outputCount = dialogue.Outputs.Count;
    var addedChoiceTarget = project.AddConnectedNode(dialogue.Id, NodeKind.Scene, 1600, 420);

    Assert(dialogue.Outputs.Count == outputCount + 1, "Dialogue did not add a new choice for the connected node.");
    Assert(dialogue.Outputs[^1].TargetNodeId == addedChoiceTarget.Id, "New dialogue choice was not connected.");
    Assert(dialogue.Outputs[0].TargetNodeId == firstTarget.Id, "Existing first dialogue connection was overwritten.");
    Assert(dialogue.Outputs[1].TargetNodeId == secondTarget.Id, "Existing second dialogue connection was overwritten.");
    project.Validate();
}

static void DuplicateNodeCopiesAuthoringDataSafely()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var duplicateBody = ExtractMethodBody(
        sourceText,
        "public NovelNode DuplicateNode");
    var duplicateTitleBody = ExtractMethodBody(
        sourceText,
        "private string CreateDuplicateTitle");
    var project = NovelProject.CreateDefault();
    project.Assets.AddRange(
    [
        new NovelAsset { Id = "city", Kind = AssetKind.Image, Path = "city.png" },
        new NovelAsset { Id = "hero", Kind = AssetKind.Image, Path = "hero.png" },
        new NovelAsset { Id = "theme", Kind = AssetKind.Audio, Path = "theme.ogg" },
        new NovelAsset { Id = "blip", Kind = AssetKind.Audio, Path = "blip.wav" },
        new NovelAsset { Id = "click", Kind = AssetKind.Audio, Path = "click.wav" },
    ]);
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.Title = "Intro";
    scene.Text = "Hello";
    scene.Background = "@city";
    scene.Music = "@theme";
    scene.Script = "set visited = 1";
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-block",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "visited",
            Value = "1",
        });
    scene.Outputs[0].ConditionExpression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "visited",
        Operator = ">=",
        Value = "1",
    };
    VisualConditionCompiler.SyncTextFromExpression(scene.Outputs[0]);
    scene.Outputs[0].Script = "add score 1";
    scene.Outputs[0].ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "output-block",
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "score",
            Value = "1",
        });
    scene.Outputs[0].TransitionSound = "@click";
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
            Sprite = "@hero",
            VoiceSounds = ["@blip"],
        });

    var duplicate = project.DuplicateNode(scene.Id);

    Assert(duplicate.Id != scene.Id, "Duplicate node reused the source id.");
    Assert(duplicate.Title == "Intro копия", "Duplicate title was not marked as a copy.");
    Assert(duplicate.X == scene.X + 48 && duplicate.Y == scene.Y + 48, "Duplicate position offset changed.");
    Assert(duplicate.Text == scene.Text, "Duplicate text was not copied.");
    Assert(duplicate.Background == scene.Background, "Duplicate background was not copied.");
    Assert(duplicate.Music == scene.Music, "Duplicate music was not copied.");
    Assert(duplicate.Script == scene.Script, "Duplicate script was not copied.");
    Assert(
        duplicate.ScriptBlocks.Count == 1
            && duplicate.ScriptBlocks[0].Id == "scene-block"
            && !ReferenceEquals(duplicate.ScriptBlocks[0], scene.ScriptBlocks[0]),
        "Duplicate node did not clone entry visual script blocks.");
    Assert(duplicate.Outputs.Count == scene.Outputs.Count, "Duplicate outputs count changed.");
    Assert(duplicate.Outputs[0].Id != scene.Outputs[0].Id, "Duplicate output reused the source id.");
    Assert(duplicate.Outputs[0].TargetNodeId is null, "Duplicate output kept the source connection.");
    Assert(duplicate.Outputs[0].ConditionExpression is not null, "Duplicate node lost output condition expression.");
    Assert(
        duplicate.Outputs[0].ConditionExpression != scene.Outputs[0].ConditionExpression,
        "Duplicate node reused the output condition expression instance.");
    Assert(duplicate.Outputs[0].TransitionSound == "@click", "Duplicate transition sound was not copied.");
    Assert(
        duplicate.Outputs[0].ScriptBlocks.Count == 1
            && duplicate.Outputs[0].ScriptBlocks[0].Id == "output-block"
            && !ReferenceEquals(
                duplicate.Outputs[0].ScriptBlocks[0],
                scene.Outputs[0].ScriptBlocks[0]),
        "Duplicate node did not clone output visual script blocks.");
    Assert(duplicate.Characters[0].Id != scene.Characters[0].Id, "Duplicate character reused the source id.");
    Assert(duplicate.Characters[0].Sprite == "@hero", "Duplicate character data was not copied.");
    Assert(
        duplicateBody.Contains("foreach (var block in source.ScriptBlocks)", StringComparison.Ordinal)
            && duplicateBody.Contains("foreach (var character in source.Characters)", StringComparison.Ordinal)
            && duplicateBody.Contains("foreach (var output in source.Outputs)", StringComparison.Ordinal)
            && duplicateBody.Contains("foreach (var block in output.ScriptBlocks)", StringComparison.Ordinal)
            && !duplicateBody.Contains(".Select(", StringComparison.Ordinal)
            && !duplicateBody.Contains(".Zip(", StringComparison.Ordinal),
        "Node duplication should copy authoring collections with direct loops.");
    Assert(
        duplicateTitleBody.Contains("foreach (var node in Nodes)", StringComparison.Ordinal)
            && !duplicateTitleBody.Contains(".Select(", StringComparison.Ordinal)
            && !duplicateTitleBody.Contains(".ToHashSet(", StringComparison.Ordinal),
        "Duplicate node titles should build their existing-title lookup directly.");
    project.Validate();
}

static void DuplicateDialogueChoiceCopiesAuthoringDataSafely()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var duplicateOutputBody = ExtractMethodBody(
        sourceText,
        "public NodeOutput DuplicateOutput");
    var duplicateOutputLabelBody = ExtractMethodBody(
        sourceText,
        "private static string CreateDuplicateOutputLabel");
    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset { Id = "click", Kind = AssetKind.Audio, Path = "click.wav" });
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    var target = project.AddNode(NodeKind.Scene, 1100, 220);
    var source = dialogue.Outputs[0];
    source.Label = "Спросить";
    source.ConditionExpression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "trust",
        Operator = ">=",
        Value = "2",
    };
    VisualConditionCompiler.SyncTextFromExpression(source);
    source.Script = "add trust 1";
    source.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "choice-block",
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "trust",
            Value = "1",
        });
    source.TransitionSound = "@click";
    source.FadeDurationMs = 900;
    source.TargetNodeId = target.Id;

    var duplicate = project.DuplicateOutput(dialogue.Id, source.Id);

    Assert(duplicate.Id != source.Id, "Duplicate choice reused the source id.");
    Assert(duplicate.Label == "Спросить копия", "Duplicate choice label was not marked as a copy.");
    Assert(duplicate.Condition == source.Condition, "Duplicate choice condition was not copied.");
    Assert(duplicate.ConditionExpression is not null, "Duplicate choice structured condition was not copied.");
    Assert(
        duplicate.ConditionExpression != source.ConditionExpression,
        "Duplicate choice reused the structured condition instance.");
    Assert(
        duplicate.ConditionExpression?.VariableName == "trust",
        "Duplicate choice structured condition data changed.");
    Assert(duplicate.Script == source.Script, "Duplicate choice script was not copied.");
    Assert(
        duplicate.ScriptBlocks.Count == 1
            && duplicate.ScriptBlocks[0].Id == "choice-block"
            && !ReferenceEquals(duplicate.ScriptBlocks[0], source.ScriptBlocks[0]),
        "Duplicate choice did not clone visual script blocks.");
    Assert(duplicate.TransitionSound == "@click", "Duplicate choice transition sound was not copied.");
    Assert(duplicate.FadeDurationMs == source.FadeDurationMs, "Duplicate choice fade duration was not copied.");
    Assert(duplicate.TargetNodeId is null, "Duplicate choice kept the source connection.");
    Assert(
        dialogue.Outputs.IndexOf(duplicate) == dialogue.Outputs.IndexOf(source) + 1,
        "Duplicate choice was not inserted next to the source choice.");
    Assert(
        duplicateOutputBody.Contains("foreach (var block in source.ScriptBlocks)", StringComparison.Ordinal)
            && !duplicateOutputBody.Contains(".Select(", StringComparison.Ordinal)
            && !duplicateOutputBody.Contains(".ToList(", StringComparison.Ordinal),
        "Choice duplication should copy visual script blocks with a direct loop.");
    Assert(
        duplicateOutputLabelBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal)
            && !duplicateOutputLabelBody.Contains(".Select(", StringComparison.Ordinal)
            && !duplicateOutputLabelBody.Contains(".ToHashSet(", StringComparison.Ordinal),
        "Duplicate output labels should build their existing-label lookup directly.");
    project.Validate();
}

static void MoveDialogueChoicesPreservesDataAndBoundaries()
{
    var project = NovelProject.CreateDefault();
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var first = dialogue.Outputs[0];
    var second = dialogue.Outputs[1];
    var third = project.AddChoice(dialogue.Id, "Спрятаться");
    third.Condition = "courage < 2";
    third.Script = "set hidden = true";
    third.TargetNodeId = scene.Id;

    Assert(!project.MoveOutput(dialogue.Id, first.Id, -1), "First choice moved beyond the top boundary.");
    Assert(project.MoveOutput(dialogue.Id, third.Id, -1), "Third choice did not move up.");
    Assert(
        dialogue.Outputs.Select(output => output.Id).SequenceEqual([first.Id, third.Id, second.Id]),
        "Choice order after moving up is wrong.");
    Assert(third.TargetNodeId == scene.Id, "Moved choice lost its target connection.");
    Assert(third.Condition == "courage < 2", "Moved choice lost its condition.");
    Assert(project.MoveOutput(dialogue.Id, first.Id, 1), "First choice did not move down.");
    Assert(
        dialogue.Outputs.Select(output => output.Id).SequenceEqual([third.Id, first.Id, second.Id]),
        "Choice order after moving down is wrong.");
    Assert(!project.MoveOutput(scene.Id, scene.Outputs[0].Id, 1), "Scene output should not move as a dialogue choice.");
    project.Validate();
}

static void NodeTemplatesCreateAuthoringScaffolds()
{
    var project = NovelProject.CreateDefault();
    var sceneTemplate = project.AddNodeTemplate(NodeTemplateKind.EstablishingScene, 980, 80);
    var lineTemplate = project.AddNodeTemplate(NodeTemplateKind.CharacterLine, 1220, 80);
    var choiceTemplate = project.AddNodeTemplate(NodeTemplateKind.ChoiceBranch, 1460, 80);

    Assert(sceneTemplate.Kind == NodeKind.Scene, "Scene template created the wrong node kind.");
    Assert(sceneTemplate.Title == "Сцена с фоном", "Scene template title changed.");
    Assert(!sceneTemplate.InheritBackground, "Scene template should be ready to override its background.");
    Assert(sceneTemplate.Outputs.Count == 1, "Scene template should have one linear output.");
    Assert(lineTemplate.Kind == NodeKind.Dialogue, "Line template created the wrong node kind.");
    Assert(lineTemplate.Speaker == "Герой", "Line template did not set a speaker scaffold.");
    Assert(lineTemplate.Outputs is [{ Label: "Дальше" }], "Line template should have one next output.");
    Assert(choiceTemplate.Outputs.Count == 2, "Choice template should have two starter choices.");
    Assert(
        choiceTemplate.Outputs.Select(output => output.Label).SequenceEqual(["Вариант 1", "Вариант 2"]),
        "Choice template output labels changed.");

    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene && node.Id == "scene-1");
    scene.Outputs[0].TargetNodeId = null;
    var connected = project.AddConnectedNodeTemplate(
        scene.Id,
        NodeTemplateKind.CharacterLine,
        scene.X + 480,
        scene.Y);

    Assert(scene.Outputs[0].TargetNodeId == connected.Id, "Connected template was not wired from the source.");
    Assert(connected.Title == "Реплика персонажа", "Connected template did not apply the requested scaffold.");
    Assert(connected.Outputs is [{ Label: "Дальше" }], "Connected line template should have one next output.");

    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue && node.Id == "dialogue-1");
    dialogue.Outputs[0].TargetNodeId = sceneTemplate.Id;
    dialogue.Outputs[1].TargetNodeId = lineTemplate.Id;
    var connectedChoice = project.AddConnectedNodeTemplate(
        dialogue.Id,
        NodeTemplateKind.ChoiceBranch,
        dialogue.X + 480,
        dialogue.Y);
    Assert(dialogue.Outputs[^1].Label == "Новый выбор", "Choice template should add a semantic output label.");
    Assert(dialogue.Outputs[^1].TargetNodeId == connectedChoice.Id, "Choice template output was not connected.");

    var connectedLine = project.AddConnectedNodeTemplate(
        dialogue.Id,
        NodeTemplateKind.CharacterLine,
        dialogue.X + 720,
        dialogue.Y);
    Assert(dialogue.Outputs[^1].Label == "Новая реплика", "Line template should add a semantic output label.");
    Assert(dialogue.Outputs[^1].TargetNodeId == connectedLine.Id, "Line template output was not connected.");
    project.Validate();
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

static void ProjectSaveCleansTemporaryFileOnFailure()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-save-failure-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var project = NovelProject.CreateDefault();
        var blockedPath = Path.Combine(directory, "blocked.novel.json");
        Directory.CreateDirectory(blockedPath);

        try
        {
            ProjectSerializer.Save(project, blockedPath);
            throw new InvalidOperationException("Saving over a directory should fail.");
        }
        catch (Exception error) when (
            error is IOException
            or UnauthorizedAccessException)
        {
            // Expected: the target path is a directory, not a file.
        }

        Assert(
            !File.Exists(blockedPath + ".tmp"),
            "Failed project save left a temporary file behind.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
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

static void NovelScriptAppliesNumericCommands()
{
    var state = new ScriptState();
    NovelScript.Execute(
        """
        set score = 2
        multiply score 5
        divide score 2
        multiply missing 4
        divide missing 2
        """,
        state);

    Assert(
        Convert.ToDouble(state.Variables["score"]) == 5,
        "Multiply/divide commands did not update an existing numeric variable.");
    Assert(
        Convert.ToDouble(state.Variables["missing"]) == 0,
        "Multiply/divide commands did not treat a missing variable as 0.");
    AssertThrows<InvalidDataException>(
        () => NovelScript.Execute("divide score 0", new ScriptState()),
        "Divide by zero was accepted.");
}

static void NovelScriptTogglesVariables()
{
    var state = new ScriptState();
    NovelScript.Execute(
        """
        // comment syntax mirrors project DSL comments
        toggle met_hero
        toggle locked
        toggle locked
        set route = "good"
        toggle route
        """,
        state);

    Assert(
        state.Variables.TryGetValue("met_hero", out var metHero)
            && metHero is true,
        "Toggle did not create a true flag for a missing variable.");
    Assert(
        state.Variables.TryGetValue("locked", out var locked)
            && locked is false,
        "Toggle did not flip a true flag back to false.");
    Assert(
        state.Variables.TryGetValue("route", out var route)
            && route is false,
        "Toggle did not use truthiness for non-boolean values.");
}

static void NovelScriptRejectsInvalidAddOperands()
{
    AssertThrows<InvalidDataException>(
        () => NovelScript.Execute("dance now", new ScriptState()),
        "Unknown script command was silently ignored.");

    AssertThrows<InvalidDataException>(
        () => NovelScript.Execute("add score nope", new ScriptState()),
        "Add command accepted a non-numeric amount.");

    AssertThrows<InvalidDataException>(
        () => NovelScript.Execute(
            """
            set score = true
            add score 1
            """,
            new ScriptState()),
        "Add command accepted a non-numeric existing variable.");
}

static void VisualScriptBlocksRoundTripAndRun()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    var end = project.AddNode(NodeKind.Scene, 760, 120);
    dialogue.Outputs[0].TargetNodeId = end.Id;
    scene.Script = "set score = 1";
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-add-score",
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "score",
            Value = "2",
        });
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-visited-flag",
            Kind = VisualScriptBlockKind.SetFlagTrue,
            VariableName = "visited_intro",
        });
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-subtract-score",
            Kind = VisualScriptBlockKind.SubtractVariable,
            VariableName = "score",
            Value = "1",
        });
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-multiply-score",
            Kind = VisualScriptBlockKind.MultiplyVariable,
            VariableName = "score",
            Value = "3",
        });
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-divide-score",
            Kind = VisualScriptBlockKind.DivideVariable,
            VariableName = "score",
            Value = "2",
        });
    dialogue.Outputs[0].ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "choice-set-route",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "route",
            Value = "\"good\"",
        });

    var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
    var player = new NovelPlayer(restored);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    player.Choose(scene.Outputs[0].Id);
    player.Choose(dialogue.Outputs[0].Id);

    Assert(
        Convert.ToDouble(player.State.Variables["score"]) == 3,
        "Visual script blocks did not run after text script.");
    Assert(
        (string?)player.State.Variables["route"] == "good",
        "Output visual script block did not run.");
    Assert(
        player.State.Variables.TryGetValue("visited_intro", out var visitedIntro)
            && visitedIntro is true,
        "Flag visual script block did not run.");
    Assert(
        restored.FindNode(scene.Id)?.ScriptBlocks.Count == 5,
        "Visual script blocks were lost during JSON round trip.");
}

static void VisualScriptBlocksImportSimpleScripts()
{
    var blocks = VisualScriptCompiler.ParseScript(
        """
        # setup route
        // setup score
        set route = "good"
        set visited_intro = true
        set route_locked = false
        add score 2
        add score -2
        multiply score 3
        divide score 2
        toggle met_hero
        unset temporary_flag
        """);

    var script = VisualScriptCompiler.Compile(blocks);

    Assert(blocks.Count == 11, "Script import produced the wrong block count.");
    Assert(
        script.Contains("set route = \"good\"", StringComparison.Ordinal),
        "Imported set command was not compiled back.");
    Assert(
        blocks.Any(block => block.Kind == VisualScriptBlockKind.SetFlagTrue
            && block.VariableName == "visited_intro"),
        "Imported true set command did not become a flag-on block.");
    Assert(
        blocks.Any(block => block.Kind == VisualScriptBlockKind.SetFlagFalse
            && block.VariableName == "route_locked"),
        "Imported false set command did not become a flag-off block.");
    Assert(
        script.Contains("add score 2", StringComparison.Ordinal),
        "Imported add command was not compiled back.");
    Assert(
        blocks.Any(block => block.Kind == VisualScriptBlockKind.SubtractVariable
            && block.VariableName == "score"
            && block.Value == "2"),
        "Imported negative add command did not become a subtract block.");
    Assert(
        blocks.Any(block => block.Kind == VisualScriptBlockKind.MultiplyVariable
            && block.VariableName == "score"
            && block.Value == "3"),
        "Imported multiply command did not become a multiply block.");
    Assert(
        blocks.Any(block => block.Kind == VisualScriptBlockKind.DivideVariable
            && block.VariableName == "score"
            && block.Value == "2"),
        "Imported divide command did not become a divide block.");
    Assert(
        script.Contains("toggle met_hero", StringComparison.Ordinal),
        "Imported toggle command was not compiled back.");
    Assert(
        script.Contains("unset temporary_flag", StringComparison.Ordinal),
        "Imported unset command was not compiled back.");
    AssertThrows<InvalidDataException>(
        () => VisualScriptCompiler.ParseScript("dance now"),
        "Unsupported script command was imported into visual blocks.");
}

static void VisualScriptBlocksDescribeSemanticLabels()
{
    var descriptions = new[]
    {
        new VisualScriptBlock
        {
            Kind = VisualScriptBlockKind.SetFlagTrue,
            VariableName = "met_hero",
        },
        new VisualScriptBlock
        {
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "score",
            Value = "2",
        },
        new VisualScriptBlock
        {
            Kind = VisualScriptBlockKind.DivideVariable,
            VariableName = "score",
            Value = "2",
        },
        new VisualScriptBlock
        {
            Kind = VisualScriptBlockKind.Comment,
            Text = "setup route",
        },
    }.Select(VisualScriptCompiler.Describe).ToList();

    Assert(
        descriptions.SequenceEqual(
            [
                "Флаг met_hero: включить",
                "score += 2",
                "score /= 2",
                "Комментарий: setup route",
            ]),
        "Visual script block descriptions changed.");
}

static void VisualScriptBlocksCloneForPasteSafely()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "VisualScriptBlocks.cs"));
    var cloneBody = ExtractMethodBody(
        sourceText,
        "public static IReadOnlyList<VisualScriptBlock> CloneForPaste");
    var findMatchingOutputBody = ExtractMethodBody(
        sourceText,
        "private static NodeOutput? FindMatchingOutput");
    var source = new[]
    {
        new VisualScriptBlock
        {
            Id = "source-route",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "route",
            Value = "\"good\"",
        },
        new VisualScriptBlock
        {
            Id = "source-comment",
            Kind = VisualScriptBlockKind.Comment,
            Text = "after choice",
        },
    };

    var pasted = VisualScriptBlockOperations.CloneForPaste(source);

    Assert(pasted.Count == 2, "Paste clone count changed.");
    Assert(
        pasted.All(block => block.Id.StartsWith("block-", StringComparison.Ordinal)),
        "Pasted blocks did not receive editor ids.");
    Assert(
        pasted.Select(block => block.Id).Distinct(StringComparer.Ordinal).Count() == 2,
        "Pasted block ids are not unique.");
    Assert(
        pasted[0].Kind == source[0].Kind
            && pasted[0].VariableName == source[0].VariableName
            && pasted[0].Value == source[0].Value,
        "Pasted variable block lost authored data.");
    Assert(
        pasted[1].Text == source[1].Text,
        "Pasted comment block lost authored data.");

    pasted[0].VariableName = "changed";
    Assert(
        source[0].VariableName == "route",
        "Pasted block mutated the source block.");
    Assert(
        cloneBody.Contains("foreach (var block in blocks)", StringComparison.Ordinal)
            && !cloneBody.Contains(".Select(", StringComparison.Ordinal)
            && !cloneBody.Contains(".ToList(", StringComparison.Ordinal),
        "Visual script paste clones should be built with a direct loop.");
    Assert(
        findMatchingOutputBody.Contains("var sourceOutputs = sourceNode.Outputs;", StringComparison.Ordinal)
            && findMatchingOutputBody.Contains("for (var index = 0; index < sourceOutputs.Count; index++)", StringComparison.Ordinal)
            && findMatchingOutputBody.Contains("sourceOutput.Id.Equals", StringComparison.Ordinal)
            && findMatchingOutputBody.Contains("sourceOutput.Label.Equals", StringComparison.Ordinal)
            && !findMatchingOutputBody.Contains("FirstOrDefault", StringComparison.Ordinal),
        "Visual script output matching should avoid repeated LINQ searches.");
}

static void VisualScriptBlocksValidateGeneratedScripts()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "invalid-name",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "bad-name",
            Value = "1",
        });

    AssertThrows<InvalidDataException>(
        project.Validate,
        "Visual script block validation accepted an invalid generated command.");
}

static void VisualScriptBlocksSurviveProjectLanguageApply()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);

    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "scene-flag",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "visited_intro",
            Value = "true",
        });
    dialogue.Outputs[0].ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "choice-score",
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "score",
            Value = "1",
        });
    dialogue.Outputs[0].ConditionExpression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "score",
        Operator = ">=",
        Value = "1",
    };

    var source = ProjectLanguage.Format(project);
    Assert(
        source.Contains("# visual blocks: 1", StringComparison.Ordinal),
        "Project language format does not expose visual script blocks.");

    var parsed = ProjectLanguage.Parse(source);
    VisualScriptBlockPreserver.PreserveFrom(project, parsed);

    Assert(
        parsed.FindNode(scene.Id)?.ScriptBlocks.Single().Id == "scene-flag",
        "Project language apply dropped node visual script blocks.");
    Assert(
        parsed.FindNode(dialogue.Id)?.Outputs[0].ScriptBlocks.Single().Id == "choice-score",
        "Project language apply dropped output visual script blocks.");
    Assert(
        parsed.FindNode(dialogue.Id)?.Outputs[0].ConditionExpression?.VariableName == "score",
        "Project language apply dropped output visual condition expression.");
}

static void ProjectScriptVariablesCollectAuthoredNames()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectScriptVariables.cs"));
    var collectBody = ExtractMethodBody(
        sourceText,
        "public static IReadOnlyList<string> Collect");
    var project = NovelProject.CreateDefault();
    project.NodeTypes.Add(
        new NodeTypeDefinition
        {
            Name = "ScoredScene",
            BaseType = "scene",
            Defaults =
            {
                Script = "unset temporary_flag",
            },
        });
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);

    scene.Script = "set score = 1\nadd courage 2\ntoggle visited_intro\nmultiply multiplier 2\ndivide ratio 2";
    scene.ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "route-block",
            Kind = VisualScriptBlockKind.SetVariable,
            VariableName = "route",
            Value = "\"intro\"",
        });
    dialogue.Outputs[0].Condition = "!met_hero";
    dialogue.Outputs[0].ScriptBlocks.Add(
        new VisualScriptBlock
        {
            Id = "trust-block",
            Kind = VisualScriptBlockKind.AddVariable,
            VariableName = "trust",
            Value = "1",
        });
    dialogue.Outputs[1].ConditionExpression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "mood",
        Operator = "==",
        Value = "\"brave\"",
    };

    var variables = ProjectScriptVariables.Collect(project);

    Assert(variables.Contains("score"), "Script variable was not collected.");
    Assert(variables.Contains("courage"), "Add variable was not collected.");
    Assert(variables.Contains("visited_intro"), "Toggle variable was not collected.");
    Assert(variables.Contains("multiplier"), "Multiply variable was not collected.");
    Assert(variables.Contains("ratio"), "Divide variable was not collected.");
    Assert(variables.Contains("temporary_flag"), "Type script variable was not collected.");
    Assert(variables.Contains("route"), "Node visual block variable was not collected.");
    Assert(variables.Contains("met_hero"), "Condition variable was not collected.");
    Assert(variables.Contains("mood"), "Structured condition variable was not collected.");
    dialogue.Outputs[1].Condition = "route && courage >= 2";
    variables = ProjectScriptVariables.Collect(project);
    Assert(variables.Contains("route"), "Compound condition variable was not collected.");
    Assert(variables.Contains("trust"), "Output visual block variable was not collected.");
    Assert(
        collectBody.Contains("foreach (var variable in variables)", StringComparison.Ordinal)
            && !collectBody.Contains(".ToList(", StringComparison.Ordinal),
        "Project script variables should materialize the sorted set without LINQ list allocation.");
}

static void VisualConditionsParseAndCompile()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "VisualConditions.cs"));
    var cloneBody = ExtractMethodBody(
        sourceText,
        "public VisualConditionExpression Clone");
    var compileGroupBody = ExtractMethodBody(
        sourceText,
        "private static string CompileGroup");
    var evaluateAllBody = ExtractMethodBody(
        sourceText,
        "private static bool EvaluateAll");
    var evaluateAnyBody = ExtractMethodBody(
        sourceText,
        "private static bool EvaluateAny");
    var splitLogicalBody = ExtractMethodBody(
        sourceText,
        "private static List<string> SplitLogical");
    Assert(
        VisualConditionCompiler.Compile(VisualConditionCompiler.Parse(string.Empty)) == string.Empty,
        "Empty condition did not compile as always.");
    Assert(
        VisualConditionCompiler.Compile(VisualConditionCompiler.Parse("met_hero")) == "met_hero",
        "Truthy variable condition did not round trip.");
    Assert(
        VisualConditionCompiler.Compile(VisualConditionCompiler.Parse("!met_hero")) == "!met_hero",
        "False variable condition did not round trip.");
    Assert(
        VisualConditionCompiler.Compile(VisualConditionCompiler.Parse("score >= 3")) == "score >= 3",
        "Comparison condition did not round trip.");
    Assert(
        VisualConditionCompiler.Compile(VisualConditionCompiler.Parse("met_hero && score >= 3"))
            == "met_hero && score >= 3",
        "AND condition did not round trip.");
    Assert(
        VisualConditionCompiler.Compile(
            VisualConditionCompiler.Parse("met_hero && score >= 3 || route == \"good\""))
            == "met_hero && score >= 3 || route == \"good\"",
        "Mixed AND/OR condition did not round trip.");
    Assert(
        VisualConditionCompiler.Compile(
            VisualConditionCompiler.Parse("(met_hero || route == \"good\") && score >= 3"))
            == "(met_hero || route == \"good\") && score >= 3",
        "Parenthesized condition did not round trip.");

    var expression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "route",
        Operator = "==",
        Value = "\"good\"",
    };
    VisualConditionCompiler.Validate(expression);
    Assert(
        VisualConditionCompiler.Compile(expression) == "route == \"good\"",
        "Structured condition did not compile.");
    var state = new ScriptState();
    state.Variables["route"] = "good";
    Assert(
        VisualConditionCompiler.Evaluate(expression, state),
        "Structured condition did not evaluate.");
    Assert(
        VisualConditionCompiler.Evaluate("route == \"good\"", state),
        "String condition did not evaluate through visual condition compiler.");
    state.Variables["met_hero"] = true;
    state.Variables["score"] = 3;
    Assert(
        VisualConditionCompiler.Evaluate("met_hero && score >= 3", state),
        "AND condition did not evaluate.");
    state.Variables["score"] = 1;
    Assert(
        VisualConditionCompiler.Evaluate("met_hero && score >= 3 || route == \"good\"", state),
        "OR condition did not evaluate.");
    state.Variables["score"] = 3;
    Assert(
        VisualConditionCompiler.Evaluate(
            "(missing_flag || route == \"good\") && score >= 3",
            state),
        "Parenthesized condition did not evaluate.");
    AssertThrows<InvalidDataException>(
        () => VisualConditionCompiler.Validate(
            new VisualConditionExpression
            {
                Kind = VisualConditionKind.All,
                Children =
                [
                    new(),
                    new()
                    {
                        Kind = VisualConditionKind.VariableTrue,
                        VariableName = "met_hero",
                    },
                ],
            }),
        "Empty child condition in group was accepted.");
    Assert(
        cloneBody.Contains("foreach (var child in Children)", StringComparison.Ordinal)
            && !cloneBody.Contains(".Select(", StringComparison.Ordinal)
            && !cloneBody.Contains(".ToList(", StringComparison.Ordinal),
        "Visual condition cloning should copy children with a direct loop.");
    Assert(
        compileGroupBody.Contains("var builder = new StringBuilder();", StringComparison.Ordinal)
            && compileGroupBody.Contains("for (var index = 0; index < children.Count; index++)", StringComparison.Ordinal)
            && !compileGroupBody.Contains(".Select(", StringComparison.Ordinal)
            && !compileGroupBody.Contains("string.Join", StringComparison.Ordinal),
        "Visual condition groups should compile without transient enumerable chains.");
    Assert(
        evaluateAllBody.Contains("foreach (var child in children)", StringComparison.Ordinal)
            && evaluateAnyBody.Contains("foreach (var child in children)", StringComparison.Ordinal)
            && !evaluateAllBody.Contains(".All(", StringComparison.Ordinal)
            && !evaluateAnyBody.Contains(".Any(", StringComparison.Ordinal),
        "Visual condition group evaluation should avoid LINQ delegates.");
    Assert(
        splitLogicalBody.Contains("foreach (var part in parts)", StringComparison.Ordinal)
            && !splitLogicalBody.Contains("parts.Any", StringComparison.Ordinal),
        "Visual condition splitting should validate parts without LINQ delegates.");
}

static void VisualConditionExpressionsSurviveJsonAndRuntime()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    var end = project.AddNode(NodeKind.Scene, 980, 120);
    scene.Script = """
        set score = 2
        set route = "good"
        """;
    dialogue.Outputs[0].TargetNodeId = end.Id;
    dialogue.Outputs[0].ConditionExpression =
        VisualConditionCompiler.Parse("score >= 2 && route == \"good\"");
    VisualConditionCompiler.SyncTextFromExpression(dialogue.Outputs[0]);
    dialogue.Outputs[1].ConditionExpression = new VisualConditionExpression
    {
        Kind = VisualConditionKind.Comparison,
        VariableName = "score",
        Operator = "<",
        Value = "2",
    };
    VisualConditionCompiler.SyncTextFromExpression(dialogue.Outputs[1]);

    var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
    var restoredDialogue = restored.FindNode(dialogue.Id)
        ?? throw new InvalidOperationException("Restored dialogue node was not found.");
    var player = new NovelPlayer(restored);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    player.Choose(scene.Outputs[0].Id);
    var available = player.GetAvailableOutputs();

    Assert(
        restoredDialogue.Outputs[0].ConditionExpression?.Kind == VisualConditionKind.All
            && restoredDialogue.Outputs[0].ConditionExpression?.Children.Count == 2,
        "JSON round trip dropped the structured choice condition.");
    Assert(
        available.Any(output => output.Id == restoredDialogue.Outputs[0].Id),
        "Runtime did not show the choice backed by a structured condition.");
    Assert(
        available.All(output => output.Id != restoredDialogue.Outputs[1].Id),
        "Runtime showed a choice with a failing structured condition.");
    player.Choose(restoredDialogue.Outputs[0].Id);
    Assert(player.CurrentNode?.Id == end.Id, "Structured condition choice did not advance.");
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

static void NodeCharacterOperationsPreserveDataAndOrder()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var uniqueCharacterIdBody = ExtractMethodBody(
        sourceText,
        "private static string CreateUniqueCharacterId");
    var duplicateCharacterNameBody = ExtractMethodBody(
        sourceText,
        "private static string CreateDuplicateCharacterName");
    var sanitizeCharacterIdBody = ExtractMethodBody(
        sourceText,
        "private static string SanitizeCharacterId");
    var project = NovelProject.CreateDefault();
    project.AssetFolders.Add("characters");
    project.AssetFolders.Add("voices");
    project.Assets.Add(
        new NovelAsset
        {
            Id = "hero_sprite",
            Kind = AssetKind.Image,
            Path = "files/characters/hero.png",
            Folder = "characters",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "hero_voice",
            Kind = AssetKind.Audio,
            Path = "files/voices/hero.wav",
            Folder = "voices",
        });
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.UsesTypeDefaults = true;
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            Sprite = "@hero_sprite",
            Position = CharacterPosition.Left,
            HasCustomTransform = true,
            X = 640,
            Y = 470,
            Scale = 1.25,
            Rotation = -5,
            VoiceSounds = ["@hero_voice"],
            VoicePitch = 1.4,
            VoiceEveryNthCharacter = 2,
        });
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "friend",
            Name = "Друг",
            Position = CharacterPosition.Right,
        });

    var duplicate = project.DuplicateCharacter(scene.Id, "hero");

    Assert(duplicate.Id == "hero-copy", "Duplicate character id was not based on the source id.");
    Assert(duplicate.Name == "Герой копия", "Duplicate character name was not marked as a copy.");
    Assert(duplicate.Sprite == "@hero_sprite", "Duplicate character sprite was not copied.");
    Assert(
        duplicate.GetVoiceSounds().SequenceEqual(["@hero_voice"]),
        "Duplicate character voice sounds were not copied.");
    Assert(duplicate.VoicePitch == 1.4, "Duplicate character voice pitch was not copied.");
    Assert(duplicate.VoiceEveryNthCharacter == 2, "Duplicate character voice cadence was not copied.");
    Assert(
        scene.Characters.Select(character => character.Id).SequenceEqual(["hero", "hero-copy", "friend"]),
        "Duplicate character was not inserted next to the source.");
    Assert(
        scene.PropertyOverrides.Contains("characters"),
        "Character operation did not mark type-default characters as overridden.");

    Assert(project.MoveCharacter(scene.Id, "friend", -1), "Character did not move up.");
    Assert(
        scene.Characters.Select(character => character.Id).SequenceEqual(["hero", "friend", "hero-copy"]),
        "Character order after moving up is wrong.");
    Assert(!project.MoveCharacter(scene.Id, "hero", -1), "First character moved beyond the top boundary.");

    Assert(
        project.SetCharacterPosition(scene.Id, duplicate.Id, CharacterPosition.Center),
        "Character position did not change.");
    Assert(duplicate.Position == CharacterPosition.Center, "Character position was not applied.");
    Assert(
        !duplicate.HasCustomTransform && duplicate.Scale == 1 && duplicate.Rotation == 0,
        "Position change did not reset custom transform.");
    Assert(
        !project.SetCharacterPosition(scene.Id, duplicate.Id, CharacterPosition.Center),
        "Unchanged character position should not report an edit.");

    var added = project.AddCharacterClone(
        scene.Id,
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой из библиотеки",
        });
    Assert(added.Id == "hero-2", "Library character clone id was not made unique.");

    var previewCharacters = scene.Characters.Select(character => character.Clone()).ToList();
    var previewAdded = NovelProject.AddCharacterClone(
        previewCharacters,
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой для визуального редактора",
        });
    Assert(
        previewAdded.Id == "hero-3",
        "Direct character collection clone id was not made unique.");
    Assert(
        uniqueCharacterIdBody.Contains("foreach (var character in characters)", StringComparison.Ordinal)
            && duplicateCharacterNameBody.Contains("foreach (var character in characters)", StringComparison.Ordinal)
            && sanitizeCharacterIdBody.Contains("Span<char> buffer", StringComparison.Ordinal)
            && !uniqueCharacterIdBody.Contains(".Select(", StringComparison.Ordinal)
            && !duplicateCharacterNameBody.Contains(".Select(", StringComparison.Ordinal)
            && !sanitizeCharacterIdBody.Contains(".Select(", StringComparison.Ordinal),
        "Character copy helpers should avoid LINQ allocations on authoring operations.");
    project.Validate();
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

static void ChoiceAvailabilityWaitsForDialogueTyping()
{
    Assert(
        !ChoiceAvailability.CanEnable(
            choicesReady: false,
            paused: false,
            transitioning: false),
        "Choices were enabled before dialogue typing finished.");
    Assert(
        ChoiceAvailability.CanEnable(
            choicesReady: true,
            paused: false,
            transitioning: false),
        "Choices were not enabled after dialogue typing finished.");
    Assert(
        !ChoiceAvailability.CanEnable(
            choicesReady: true,
            paused: true,
            transitioning: false),
        "Choices were enabled while paused.");
    Assert(
        !ChoiceAvailability.CanEnable(
            choicesReady: true,
            paused: false,
            transitioning: true),
        "Choices were enabled during a transition.");
    Assert(
        !ChoiceAvailability.CanChooseByShortcut(
            choicesReady: false,
            paused: false,
            transitioning: false,
            choiceIndex: 0,
            choiceCount: 2),
        "Choice shortcuts were enabled before dialogue typing finished.");
    Assert(
        !ChoiceAvailability.CanChooseByShortcut(
            choicesReady: true,
            paused: true,
            transitioning: false,
            choiceIndex: 0,
            choiceCount: 2),
        "Choice shortcuts were enabled while paused.");
    Assert(
        !ChoiceAvailability.CanChooseByShortcut(
            choicesReady: true,
            paused: false,
            transitioning: true,
            choiceIndex: 0,
            choiceCount: 2),
        "Choice shortcuts were enabled during a transition.");
    Assert(
        !ChoiceAvailability.CanChooseByShortcut(
            choicesReady: true,
            paused: false,
            transitioning: false,
            choiceIndex: 2,
            choiceCount: 2),
        "Choice shortcuts accepted an out-of-range choice.");
    Assert(
        ChoiceAvailability.CanChooseByShortcut(
            choicesReady: true,
            paused: false,
            transitioning: false,
            choiceIndex: 1,
            choiceCount: 2),
        "Choice shortcuts did not accept a ready in-range choice.");
}

static void NovelPlayerFollowsLargeGraphQuickly()
{
    const int sceneCount = 3_000;
    var project = new NovelProject { Title = "Large runtime graph" };
    project.Nodes.Add(
        new NovelNode
        {
            Id = "start",
            Kind = NodeKind.Start,
            TypeName = "start",
            Title = "Start",
            Outputs =
            {
                new NodeOutput
                {
                    Id = "out-start",
                    TargetNodeId = "scene-0",
                },
            },
        });
    for (var index = 0; index < sceneCount; index++)
    {
        project.Nodes.Add(
            new NovelNode
            {
                Id = $"scene-{index}",
                Kind = NodeKind.Scene,
                TypeName = "scene",
                Title = $"Scene {index}",
                Outputs =
                {
                    new NodeOutput
                    {
                        Id = $"out-scene-{index}",
                        TargetNodeId = index + 1 < sceneCount
                            ? $"scene-{index + 1}"
                            : null,
                    },
                },
            });
    }

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "NovelPlayer.cs"));
    var constructorBody = ExtractMethodBody(
        source,
        "public NovelPlayer(");
    var lookupBody = ExtractMethodBody(
        source,
        "private static IReadOnlyDictionary<string, NovelNode> BuildNodeLookup");
    var availableOutputsBody = ExtractMethodBody(
        source,
        "public IReadOnlyList<NodeOutput> GetAvailableOutputs");
    var createSaveBody = ExtractMethodBody(
        source,
        "public RuntimeSaveState CreateSaveState");
    var restoreBody = ExtractMethodBody(
        source,
        "public NovelNode Restore");
    var chooseBody = ExtractMethodBody(
        source,
        "public NovelNode Choose");
    var enterBody = ExtractMethodBody(
        source,
        "private NovelNode Enter");

    Assert(
        source.Contains("_nodesById", StringComparison.Ordinal),
        "NovelPlayer should build a node lookup for runtime transitions.");
    Assert(
        constructorBody.Contains("BuildNodeLookup(project.Nodes, out var startNode)", StringComparison.Ordinal)
            && !constructorBody.Contains(".ToDictionary(", StringComparison.Ordinal)
            && !constructorBody.Contains(".Single(", StringComparison.Ordinal),
        "NovelPlayer constructor should build runtime lookup state in one direct pass.");
    Assert(
        lookupBody.Contains("foreach (var node in nodes)", StringComparison.Ordinal)
            && lookupBody.Contains("lookup[node.Id] = node;", StringComparison.Ordinal)
            && lookupBody.Contains("node.Kind == NodeKind.Start", StringComparison.Ordinal)
            && !lookupBody.Contains(".ToDictionary(", StringComparison.Ordinal)
            && !lookupBody.Contains(".Single(", StringComparison.Ordinal),
        "NovelPlayer node lookup builder should avoid LINQ startup scans.");
    Assert(
        !source.Contains("_project.FindNode", StringComparison.Ordinal),
        "NovelPlayer runtime paths should not use linear project node lookup.");
    Assert(
        availableOutputsBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal)
            && availableOutputsBody.Contains("available ??= new List<NodeOutput>()", StringComparison.Ordinal)
            && !availableOutputsBody.Contains(".Where(", StringComparison.Ordinal)
            && !availableOutputsBody.Contains(".ToList(", StringComparison.Ordinal),
        "NovelPlayer should collect available outputs without LINQ pipelines.");
    Assert(
        chooseBody.Contains("FindOutput(node, outputId)", StringComparison.Ordinal)
            && !chooseBody.Contains("FirstOrDefault", StringComparison.Ordinal),
        "NovelPlayer should find chosen outputs in a direct pass.");
    Assert(
        createSaveBody.Contains("CloneCharacters(State.CurrentCharacters)", StringComparison.Ordinal)
            && createSaveBody.Contains("CloneVariables(State.Variables)", StringComparison.Ordinal)
            && !createSaveBody.Contains(".Select(", StringComparison.Ordinal)
            && !createSaveBody.Contains(".ToDictionary(", StringComparison.Ordinal),
        "Runtime save state should avoid transient LINQ cloning pipelines.");
    Assert(
        restoreBody.Contains(
            "AddCharacterClones(saveState.CurrentCharacters, State.CurrentCharacters)",
            StringComparison.Ordinal)
            && !restoreBody.Contains(".Select(", StringComparison.Ordinal),
        "Runtime restore should clone characters in one direct pass.");
    Assert(
        enterBody.Contains("AddCharacterClones(node.Characters, State.CurrentCharacters)", StringComparison.Ordinal)
            && !enterBody.Contains(".Select(", StringComparison.Ordinal),
        "Runtime node entry should clone characters without LINQ pipelines.");

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var player = new NovelPlayer(project);
    var node = player.Start();
    while (node.Outputs[0].TargetNodeId is not null)
    {
        node = player.Choose(node.Outputs[0].Id);
    }
    stopwatch.Stop();

    Assert(node.Id == $"scene-{sceneCount - 1}", "Runtime did not reach the final scene.");
    Assert(
        stopwatch.ElapsedMilliseconds < 2_000,
        $"Large graph runtime traversal is too slow: {stopwatch.ElapsedMilliseconds}ms.");
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

static void RuntimeSaveRestoresState()
{
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.Background = "backgrounds/room.png";
    scene.InheritBackground = false;
    scene.Script = "set score = 5";
    var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
    dialogue.Script = "add score 2";

    var player = new NovelPlayer(project);
    var start = player.Start();
    player.Choose(start.Outputs[0].Id);
    var save = player.CreateSaveState();
    player.Choose(scene.Outputs[0].Id);
    Assert(player.CurrentNode?.Id == dialogue.Id, "Runtime did not advance before restore.");

    var restored = player.Restore(save);
    Assert(restored.Id == scene.Id, "Save did not restore current node.");
    Assert(
        Convert.ToDouble(player.State.Variables["score"]) == 5,
        "Save did not restore variables.");
    Assert(
        player.State.CurrentBackground == "backgrounds/room.png",
        "Save did not restore background.");
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

static void CharacterTransformsRoundTrip()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectLanguage.cs"));
    var copyCharactersBody = ExtractMethodBody(
        sourceText,
        "private static void CopyCharacters");
    var parseCharacterBody = ExtractMethodBody(
        sourceText,
        "private CharacterPlacement ParseCharacter");
    var project = NovelProject.CreateDefault();
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            Position = CharacterPosition.Left,
            HasCustomTransform = true,
            X = 742.5,
            Y = 476,
            Scale = 1.35,
            Rotation = -7.5,
            VoiceSound = "voices/hero.wav",
            VoiceSounds =
            [
                "voices/hero.wav",
                "voices/hero-alt.wav",
            ],
            VoicePitch = 1.25,
            VoiceEveryNthCharacter = 3,
        });

    var code = ProjectLanguage.Format(project);
    var fromCode = ProjectLanguage.Parse(code)
        .FindNode(scene.Id)!
        .Characters.Single();
    var fromJson = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project))
        .FindNode(scene.Id)!
        .Characters.Single();

    Assert(code.Contains("placement (742.5, 476)", StringComparison.Ordinal), "Placement was not formatted.");
    Assert(code.Contains("voice \"voices/hero.wav\"", StringComparison.Ordinal), "Voice sound was not formatted.");
    Assert(code.Contains("voice \"voices/hero-alt.wav\"", StringComparison.Ordinal), "Second voice sound was not formatted.");
    Assert(CountOccurrences(code, "voice \"voices/") == 2, "Voice sounds were duplicated or lost.");
    Assert(code.Contains("voice-pitch 1.25", StringComparison.Ordinal), "Voice pitch was not formatted.");
    Assert(code.Contains("voice-every 3", StringComparison.Ordinal), "Voice frequency was not formatted.");
    Assert(fromCode.HasCustomTransform, "Code parser lost the custom transform flag.");
    Assert(Math.Abs(fromCode.Scale - 1.35) < 0.001, "Code parser changed character scale.");
    Assert(Math.Abs(fromCode.Rotation + 7.5) < 0.001, "Code parser changed character rotation.");
    Assert(fromCode.VoiceSound == "voices/hero.wav", "Code parser lost character voice sound.");
    Assert(fromCode.VoiceSounds.Count == 2, "Code parser lost multiple character voice sounds.");
    Assert(fromCode.VoiceSounds.Contains("voices/hero-alt.wav"), "Code parser lost second character voice sound.");
    Assert(Math.Abs(fromCode.VoicePitch - 1.25) < 0.001, "Code parser changed character voice pitch.");
    Assert(fromCode.VoiceEveryNthCharacter == 3, "Code parser changed character voice frequency.");
    Assert(Math.Abs(fromJson.X - 742.5) < 0.001, "JSON changed character position.");
    Assert(fromJson.VoiceSounds.Count == 2, "JSON changed character voice sound list.");
    Assert(
        copyCharactersBody.Contains("for (var index = 0; index < source.Count; index++)", StringComparison.Ordinal)
            && copyCharactersBody.Contains("target.Add(source[index].Clone());", StringComparison.Ordinal)
            && !copyCharactersBody.Contains(".Select(", StringComparison.Ordinal)
            && !copyCharactersBody.Contains(".ToList(", StringComparison.Ordinal),
        "Project language character defaults should clone with a direct pass.");
    Assert(
        parseCharacterBody.Contains("VoiceSound = voiceSounds.Count > 0 ? voiceSounds[0] : string.Empty", StringComparison.Ordinal)
            && !parseCharacterBody.Contains("voiceSounds.FirstOrDefault()", StringComparison.Ordinal),
        "Project language character parsing should reuse the voice list for the primary voice.");
}

static void MainMenuAndVoiceRoundTrip()
{
    var sourceText = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var menuCloneBody = ExtractMethodBody(
        sourceText,
        "public MainMenuDesign Clone");
    var project = NovelProject.CreateDefault();
    project.MainMenu.Background = "backgrounds/menu.png";
    project.MainMenu.Elements.Add(
        new MainMenuElement
        {
            Id = "logo",
            Kind = MainMenuElementKind.ImageLabel,
            Text = "Logo",
            Image = "ui/logo.png",
            X = 320,
            Y = 80,
            Width = 420,
            Height = 120,
            CustomStyleCode = "fontWeight = bold",
        });
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            VoiceSound = "voices/hero.wav",
            VoiceSounds =
            [
                "voices/hero.wav",
                "voices/hero-soft.wav",
            ],
            VoicePitch = 1.15,
            VoiceEveryNthCharacter = 3,
        });

    var restored = ProjectSerializer.FromJson(ProjectSerializer.ToJson(project));
    var clonedMenu = project.MainMenu.Clone();
    var restoredLogo = restored.MainMenu.Elements.Single(element => element.Id == "logo");
    var restoredHero = restored.Nodes
        .Single(node => node.Id == scene.Id)
        .Characters
        .Single();

    Assert(restored.FormatVersion == 5, "New project format version was not written.");
    Assert(restored.MainMenu.Background == "backgrounds/menu.png", "Main menu background changed.");
    Assert(restoredLogo.Kind == MainMenuElementKind.ImageLabel, "Main menu element kind changed.");
    Assert(restoredLogo.Image == "ui/logo.png", "Main menu element image changed.");
    Assert(
        clonedMenu.Elements.Any(element => element.Id == "logo")
            && !ReferenceEquals(
                clonedMenu.Elements.Single(element => element.Id == "logo"),
                project.MainMenu.Elements.Single(element => element.Id == "logo")),
        "Main menu clone did not deep-copy elements.");
    Assert(
        menuCloneBody.Contains("foreach (var element in Elements)", StringComparison.Ordinal)
            && !menuCloneBody.Contains(".Select(", StringComparison.Ordinal)
            && !menuCloneBody.Contains(".ToList(", StringComparison.Ordinal),
        "Main menu clone should copy elements with a direct loop.");
    Assert(restoredHero.VoiceSound == "voices/hero.wav", "Character voice sound changed.");
    Assert(restoredHero.VoiceSounds.Count == 2, "Character voice sound list changed.");
    Assert(Math.Abs(restoredHero.VoicePitch - 1.15) < 0.001, "Character voice pitch changed.");
    Assert(restoredHero.VoiceEveryNthCharacter == 3, "Character voice frequency changed.");
}

static void MainMenuAndVoiceAssetReferences()
{
    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset
        {
            Id = "menu_bg",
            Kind = AssetKind.Image,
            Path = "assets/images/menu.png",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "voice_hero",
            Kind = AssetKind.Audio,
            Path = "assets/audio/hero.wav",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "voice_hero_alt",
            Kind = AssetKind.Audio,
            Path = "assets/audio/hero-alt.wav",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "transition_click",
            Kind = AssetKind.Audio,
            Path = "assets/audio/click.wav",
        });
    project.MainMenu.Background = "@menu_bg";
    project.MainMenu.Elements.Add(
        new MainMenuElement
        {
            Id = "banner",
            Kind = MainMenuElementKind.ImageLabel,
            Image = "@menu_bg",
            Width = 420,
            Height = 160,
        });
    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            VoiceSound = "@voice_hero",
            VoiceSounds =
            [
                "@voice_hero",
                "@voice_hero_alt",
            ],
        });
    scene.Outputs[0].TransitionSound = "@transition_click";

    project.Validate();
    Assert(project.CountAssetReferences("menu_bg") == 2, "Main menu image refs were not counted.");
    Assert(project.CountAssetReferences("voice_hero") == 1, "Voice refs were not counted.");
    Assert(project.CountAssetReferences("voice_hero_alt") == 1, "Second voice refs were not counted.");
    Assert(project.CountAssetReferences("transition_click") == 1, "Transition sound refs were not counted.");
    var referenceCounts = project.CountAssetReferencesById();
    Assert(referenceCounts["menu_bg"] == 2, "Batch main menu image refs were not counted.");
    Assert(referenceCounts["voice_hero"] == 1, "Batch voice refs were not counted.");
    Assert(referenceCounts["voice_hero_alt"] == 1, "Batch second voice refs were not counted.");
    Assert(referenceCounts["transition_click"] == 1, "Batch transition refs were not counted.");
    var menuUsages = project.FindAssetUsages("menu_bg");
    Assert(menuUsages.Count == 2, "Main menu image usages were not listed.");
    Assert(
        menuUsages.Any(usage => usage.Location.Contains("главное меню", StringComparison.Ordinal)),
        "Main menu image usage location was not reported.");
    var voiceUsage = project.FindAssetUsages("voice_hero").Single();
    Assert(
        voiceUsage.Location.Contains("голос персонажа", StringComparison.Ordinal),
        "Voice usage location was not reported.");
    Assert(voiceUsage.NodeId == scene.Id, "Voice usage node id was not reported.");
    var transitionUsage = project.FindAssetUsages("transition_click").Single();
    Assert(
        transitionUsage.Location.Contains("переход", StringComparison.Ordinal),
        "Transition sound usage location was not reported.");
    Assert(
        transitionUsage.NodeId == scene.Id,
        "Transition sound usage node id was not reported.");

    project.ReplaceAssetReference("voice_hero", "@voice_main");
    Assert(
        scene.Characters.Single().VoiceSound == "@voice_main",
        "Voice asset reference was not replaced.");
    Assert(
        scene.Characters.Single().VoiceSounds.Contains("@voice_main"),
        "Voice asset list reference was not replaced.");
    project.ReplaceAssetReference("voice_hero_alt", "@voice_main");
    Assert(
        scene.Characters.Single().VoiceSounds.Count(
            value => value.Equals("@voice_main", StringComparison.OrdinalIgnoreCase)) == 1,
        "Replacing voice assets with the same target should not store duplicates.");
}

static void AssetReferenceLookupsAvoidLinqPipelines()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var countBody = ExtractMethodBody(
        source,
        "public int CountAssetReferences");
    var batchBody = ExtractMethodBody(
        source,
        "public IReadOnlyDictionary<string, int> CountAssetReferencesById");
    var usagesBody = ExtractMethodBody(
        source,
        "public IReadOnlyList<AssetUsage> FindAssetUsages");
    var removeNodeBody = ExtractMethodBody(
        source,
        "public bool RemoveNode");
    var validateBody = ExtractMethodBody(
        source,
        "public void Validate");
    var visitorBody = ExtractMethodBody(
        source,
        "private void VisitTypedAssetValues");
    var voiceVisitorBody = ExtractMethodBody(
        source,
        "private static void VisitCharacterVoiceValues");
    var isValidIdBody = ExtractMethodBody(
        source,
        "public static bool IsValidId");

    Assert(
        !source.Contains("private IEnumerable<string> EnumerateAssetValues", StringComparison.Ordinal),
        "Asset reference lookups should not allocate a string-only enumerable wrapper.");
    Assert(
        !source.Contains("IEnumerable<AssetValue>", StringComparison.Ordinal)
            && !source.Contains("yield return", StringComparison.Ordinal)
            && !source.Contains("new AssetValue(", StringComparison.Ordinal)
            && !source.Contains("private sealed record AssetValue", StringComparison.Ordinal),
        "Asset reference traversal should not allocate iterator or AssetValue objects.");
    Assert(
        !source.Contains("Nodes.SelectMany", StringComparison.Ordinal),
        "Core graph paths should not allocate SelectMany iterator chains over node outputs.");
    Assert(
        countBody.Contains("VisitTypedAssetValues((value, _, _, _) =>", StringComparison.Ordinal)
            && !countBody.Contains(".Count(", StringComparison.Ordinal),
        "Single asset reference counts should use the direct typed-value visitor.");
    Assert(
        batchBody.Contains("VisitTypedAssetValues((value, _, _, _) =>", StringComparison.Ordinal)
            && batchBody.Contains("AssetReference.TryGetId(value, out var id)", StringComparison.Ordinal),
        "Batch asset reference counts should reuse the direct typed-value visitor.");
    Assert(
        usagesBody.Contains("var usages = new List<AssetUsage>();", StringComparison.Ordinal)
            && usagesBody.Contains(
                "VisitTypedAssetValues((value, expectedKind, owner, nodeId) =>",
                StringComparison.Ordinal)
            && !usagesBody.Contains(".Where(", StringComparison.Ordinal)
            && !usagesBody.Contains(".Select(", StringComparison.Ordinal),
        "Asset usage lookup should build results in one direct pass.");
    Assert(
        removeNodeBody.Contains("foreach (var candidate in Nodes)", StringComparison.Ordinal)
            && removeNodeBody.Contains("foreach (var output in candidate.Outputs)", StringComparison.Ordinal),
        "Node removal should disconnect outputs through direct nested loops.");
    Assert(
        validateBody.Contains("foreach (var node in Nodes)", StringComparison.Ordinal)
            && validateBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal),
        "Project validation should inspect output targets through direct nested loops.");
    Assert(
        validateBody.Contains("CountNodes(NodeKind.Start) != 1", StringComparison.Ordinal)
            && !validateBody.Contains("Nodes.Count(", StringComparison.Ordinal),
        "Project validation should count start nodes through the direct node counter.");
    Assert(
        validateBody.Contains(
            "VisitTypedAssetValues((value, expectedKind, owner, _) =>",
            StringComparison.Ordinal),
        "Project validation should validate asset references through the direct visitor.");
    Assert(
        visitorBody.Contains("foreach (var character in Characters)", StringComparison.Ordinal)
            && visitorBody.Contains("foreach (var type in NodeTypes)", StringComparison.Ordinal)
            && visitorBody.Contains("foreach (var element in MainMenu.Elements)", StringComparison.Ordinal)
            && visitorBody.Contains("foreach (var node in Nodes)", StringComparison.Ordinal)
            && visitorBody.Contains("foreach (var output in node.Outputs)", StringComparison.Ordinal)
            && visitorBody.Contains("VisitCharacterAssetValues(", StringComparison.Ordinal),
        "Typed asset traversal should walk project collections directly.");
    Assert(
        voiceVisitorBody.Contains(
            "for (var index = 0; index < character.VoiceSounds.Count; index++)",
            StringComparison.Ordinal)
            && !voiceVisitorBody.Contains("GetVoiceSounds", StringComparison.Ordinal),
        "Character voice traversal should not allocate a normalized sound list.");
    Assert(
        isValidIdBody.Contains("for (var index = 1; index < value.Length; index++)", StringComparison.Ordinal)
            && !isValidIdBody.Contains(".Skip(", StringComparison.Ordinal)
            && !isValidIdBody.Contains(".All(", StringComparison.Ordinal),
        "Asset reference id validation should scan characters directly.");
}

static void CharacterVoiceSoundsNormalizeWithoutLinq()
{
    var character = new CharacterPlacement
    {
        Id = "hero",
        VoiceSound = " voices/hero-main.wav ",
        VoiceSounds =
        [
            "voices/hero-a.wav",
            "",
            "VOICES/HERO-A.WAV",
            "voices/hero-b.wav",
            "   ",
        ],
    };

    var sounds = character.GetVoiceSounds();

    Assert(
        sounds.SequenceEqual(
            [
                "voices/hero-a.wav",
                "voices/hero-b.wav",
                " voices/hero-main.wav ",
            ]),
        "Character voice sounds should keep first-seen unique values and append the primary voice.");

    character.SetVoiceSounds(
        [
            " voices/hero-c.wav ",
            "voices/hero-c.wav",
            "",
            "voices/hero-d.wav",
        ]);

    Assert(character.VoiceSound == " voices/hero-c.wav ", "Primary voice should be the first normalized sound.");
    Assert(
        character.VoiceSounds.SequenceEqual(
            [
                " voices/hero-c.wav ",
                "voices/hero-c.wav",
                "voices/hero-d.wav",
            ]),
        "SetVoiceSounds should keep unique non-blank values without changing text.");

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var getBody = ExtractMethodBody(source, "public List<string> GetVoiceSounds");
    var setBody = ExtractMethodBody(source, "public void SetVoiceSounds");
    var collectBody = ExtractMethodBody(
        source,
        "private static List<string> CollectDistinctVoiceSounds");
    var addBody = ExtractMethodBody(
        source,
        "private static void AddDistinctVoiceSound");
    var containsBody = ExtractMethodBody(
        source,
        "private static bool ContainsVoiceSound");

    Assert(
        getBody.Contains("CollectDistinctVoiceSounds(VoiceSounds)", StringComparison.Ordinal)
            && !getBody.Contains(".Where(", StringComparison.Ordinal)
            && !getBody.Contains(".Distinct(", StringComparison.Ordinal)
            && !getBody.Contains(".ToList(", StringComparison.Ordinal),
        "GetVoiceSounds should use the direct normalizer instead of LINQ pipelines.");
    Assert(
        setBody.Contains("CollectDistinctVoiceSounds(sounds)", StringComparison.Ordinal)
            && setBody.Contains("VoiceSounds.Count == 0 ? string.Empty : VoiceSounds[0]", StringComparison.Ordinal)
            && !setBody.Contains(".Where(", StringComparison.Ordinal)
            && !setBody.Contains(".Distinct(", StringComparison.Ordinal)
            && !setBody.Contains(".FirstOrDefault(", StringComparison.Ordinal),
        "SetVoiceSounds should use the direct normalizer and index the primary voice.");
    Assert(
        collectBody.Contains("foreach (var sound in sounds)", StringComparison.Ordinal),
        "Voice sound normalization should use one direct pass.");
    Assert(
        addBody.Contains("!ContainsVoiceSound(values, sound)", StringComparison.Ordinal)
            && !addBody.Contains(".Contains(", StringComparison.Ordinal),
        "Voice sound normalization should check duplicates without LINQ Contains.");
    Assert(
        containsBody.Contains("for (var index = 0; index < values.Count; index++)", StringComparison.Ordinal)
            && containsBody.Contains("string.Equals(values[index], sound, StringComparison.OrdinalIgnoreCase)", StringComparison.Ordinal),
        "Voice duplicate checks should scan normalized sounds directly.");
}

static void RemovedVoiceAssetsAreClearedFromCharacters()
{
    var project = NovelProject.CreateDefault();
    project.Assets.Add(
        new NovelAsset
        {
            Id = "voice_hero",
            Kind = AssetKind.Audio,
            Path = "assets/audio/hero.wav",
        });
    project.Assets.Add(
        new NovelAsset
        {
            Id = "voice_hero_alt",
            Kind = AssetKind.Audio,
            Path = "assets/audio/hero-alt.wav",
        });

    var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
    scene.InheritCharacters = false;
    scene.Characters.Add(
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Герой",
            VoiceSound = "@voice_hero",
            VoiceSounds =
            [
                "@voice_hero",
                "@voice_hero_alt",
            ],
        });
    project.Characters.Add(
        new CharacterPlacement
        {
            Id = "library_hero",
            Name = "Герой из библиотеки",
            VoiceSound = "@voice_hero",
            VoiceSounds =
            [
                "@voice_hero",
                "@voice_hero_alt",
            ],
        });

    project.ReplaceAssetReference("voice_hero", string.Empty);

    var nodeCharacter = scene.Characters.Single();
    var libraryCharacter = project.Characters.Single();
    Assert(nodeCharacter.VoiceSound.Length == 0, "Removed node voice stayed as primary voice.");
    Assert(
        !nodeCharacter.VoiceSounds.Contains("@voice_hero", StringComparer.OrdinalIgnoreCase),
        "Removed node voice stayed in the voice list.");
    Assert(
        nodeCharacter.VoiceSounds.Contains("@voice_hero_alt", StringComparer.OrdinalIgnoreCase),
        "Other node voice was removed with the deleted voice.");
    Assert(libraryCharacter.VoiceSound.Length == 0, "Removed library voice stayed as primary voice.");
    Assert(
        !libraryCharacter.VoiceSounds.Contains("@voice_hero", StringComparer.OrdinalIgnoreCase),
        "Removed library voice stayed in the voice list.");
    Assert(
        libraryCharacter.VoiceSounds.Contains("@voice_hero_alt", StringComparer.OrdinalIgnoreCase),
        "Other library voice was removed with the deleted voice.");
}

static void VoiceBlipGeneratorEmitsWav()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-blip-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "hero.wav");
        var options = new VoiceBlipOptions
        {
            Pitch = 64,
            Volume = 80,
            Gender = 42,
            Tone = 70,
            Speed = 58,
            Randomness = 24,
        };

        VoiceBlipGenerator.WriteWaveFile(path, options);
        var bytes = File.ReadAllBytes(path);
        var memoryBytes = VoiceBlipGenerator.CreateWaveBytes(options);

        Assert(File.Exists(path), "Generated wav file was not created.");
        Assert(bytes.Length > 44, "Generated wav file has no sample data.");
        Assert(
            System.Text.Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF",
            "Generated wav is missing RIFF header.");
        Assert(
            System.Text.Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE",
            "Generated wav is missing WAVE marker.");
        Assert(
            memoryBytes.Length == bytes.Length,
            "Memory and file wave rendering diverged.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void VoiceSoundPickerSupportsMultipleBlips()
{
    var single = VoiceSoundPicker.Pick(["voices/hero.wav"], new Random(7));
    Assert(single == "voices/hero.wav", "Single voice sound was not preserved.");

    var sounds = new[]
    {
        "voices/hero-a.wav",
        "voices/hero-b.wav",
        "voices/hero-c.wav",
    };
    var random = new Random(7);
    var picked = Enumerable
        .Range(0, 24)
        .Select(_ => VoiceSoundPicker.Pick(sounds, random))
        .ToList();

    Assert(
        picked.All(sound => sounds.Contains(sound)),
        "Voice sound picker returned an unknown sound.");
    Assert(
        picked.Distinct(StringComparer.Ordinal).Count() > 1,
        "Voice sound picker did not vary multiple blips.");
}

static void VoicePlaybackCadenceSkipsExpectedCharacters()
{
    Assert(
        VoicePlaybackCadence.CountsAsVoicedCharacter('Г'),
        "Voice cadence should count letters as voiced characters.");
    Assert(
        VoicePlaybackCadence.CountsAsVoicedCharacter('!'),
        "Voice cadence should count punctuation as voiced characters.");
    Assert(
        !VoicePlaybackCadence.CountsAsVoicedCharacter(' '),
        "Voice cadence should skip spaces.");
    Assert(
        !VoicePlaybackCadence.CountsAsVoicedCharacter('\n'),
        "Voice cadence should skip line breaks.");
    Assert(
        VoicePlaybackCadence.ShouldPlay(1, 1),
        "Voice cadence should play the first voiced character when frequency is one.");
    Assert(
        !VoicePlaybackCadence.ShouldPlay(1, 3),
        "Voice cadence played too early for every third character.");
    Assert(
        VoicePlaybackCadence.ShouldPlay(3, 3),
        "Voice cadence did not play the third voiced character.");
    Assert(
        !VoicePlaybackCadence.ShouldPlay(4, 3),
        "Voice cadence played between expected beats.");
    Assert(
        VoicePlaybackCadence.ShouldPlay(1, 0),
        "Voice cadence should clamp too-small frequencies to one.");
    Assert(
        !VoicePlaybackCadence.ShouldPlay(0, 1),
        "Voice cadence should ignore zero voiced characters.");
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

static void ProjectValidationCachesAssetLookup()
{
    const int sceneCount = 2_000;
    var project = new NovelProject { Title = "Large asset validation" };
    project.Nodes.Add(
        new NovelNode
        {
            Id = "start",
            Kind = NodeKind.Start,
            TypeName = "start",
            Title = "Start",
            Outputs =
            {
                new NodeOutput
                {
                    Id = "out-start",
                    TargetNodeId = "scene-0",
                },
            },
        });

    for (var index = 0; index < sceneCount; index++)
    {
        var assetId = $"bg-{index}";
        project.Assets.Add(
            new NovelAsset
            {
                Id = assetId,
                Kind = AssetKind.Image,
                Path = $"files/backgrounds/{assetId}.png",
            });
        project.Nodes.Add(
            new NovelNode
            {
                Id = $"scene-{index}",
                Kind = NodeKind.Scene,
                TypeName = "scene",
                Title = $"Scene {index}",
                Background = AssetReference.Create(assetId),
                Outputs =
                {
                    new NodeOutput
                    {
                        Id = $"out-scene-{index}",
                        TargetNodeId = index + 1 < sceneCount
                            ? $"scene-{index + 1}"
                            : null,
                    },
                },
            });
    }

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "Models.cs"));
    var validateBody = ExtractMethodBody(source, "public void Validate()");
    var assetReferenceBody = ExtractMethodBody(
        source,
        "private static void ValidateAssetReference");
    var normalizedValidateBody = validateBody.Replace("\r\n", "\n");
    Assert(
        validateBody.Contains("assetsById.TryAdd", StringComparison.Ordinal),
        "Project validation should build a single asset lookup dictionary.");
    Assert(
        normalizedValidateBody.Contains(
            "ValidateAssetReference(\n                value,\n                expectedKind,\n                owner,\n                assetsById)",
            StringComparison.Ordinal),
        "Project validation should pass the lookup into asset reference validation.");
    Assert(
        assetReferenceBody.Contains("assetsById.TryGetValue", StringComparison.Ordinal),
        "Asset reference validation should use dictionary lookup.");
    Assert(
        !assetReferenceBody.Contains("FindAsset(", StringComparison.Ordinal),
        "Asset reference validation should not use linear asset lookup.");

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    project.Validate();
    stopwatch.Stop();

    Assert(
        stopwatch.ElapsedMilliseconds < 2_000,
        $"Large asset validation is too slow: {stopwatch.ElapsedMilliseconds}ms.");
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
        var secondSourceDirectory = Path.Combine(directory, "external");
        Directory.CreateDirectory(secondSourceDirectory);
        var secondSource = Path.Combine(secondSourceDirectory, "Ночной фон.png");
        File.WriteAllBytes(secondSource, [137, 80, 78, 71, 2]);
        var secondAsset = ProjectAssets.Import(project, projectPath, secondSource);
        var secondTarget = ProjectAssets.ResolvePath(projectPath, secondAsset);

        Assert(asset.Kind == AssetKind.Image, "Imported image kind was not detected.");
        Assert(AssetReference.IsValidId(asset.Id), "Generated asset id is invalid.");
        Assert(File.Exists(target), "Imported file was not copied into the project.");
        Assert(File.Exists(secondTarget), "Second imported file was not copied into the project.");
        Assert(asset.Id != secondAsset.Id, "Second imported asset reused the first id.");
        Assert(asset.Path != secondAsset.Path, "Second imported asset reused the first path.");
        Assert(
            Path.GetFileName(secondTarget).Contains("-2", StringComparison.Ordinal),
            "Second imported asset did not receive a unique file name.");

        var directoryConflictSource = Path.Combine(directory, "Castle.png");
        File.WriteAllBytes(directoryConflictSource, [137, 80, 78, 71, 4]);
        Directory.CreateDirectory(Path.Combine(
            directory,
            "files",
            "backgrounds",
            "Castle.png"));
        var directoryConflictAsset = ProjectAssets.Import(
            project,
            projectPath,
            directoryConflictSource);
        Assert(
            Path.GetFileName(ProjectAssets.ResolvePath(projectPath, directoryConflictAsset))
                == "Castle-2.png",
            "Imported asset reused a path occupied by an existing directory.");
        Assert(
            target.Contains(
                Path.Combine("files", "backgrounds"),
                StringComparison.OrdinalIgnoreCase),
            "Imported image was placed in the wrong folder.");
        Assert(
            ReferenceEquals(
                asset,
                ProjectAssets.Import(project, projectPath, target)),
            "Importing the registered file created a duplicate asset.");

        var managedCharacterDirectory = Path.Combine(directory, "files", "characters");
        Directory.CreateDirectory(managedCharacterDirectory);
        var managedCharacterSource = Path.Combine(managedCharacterDirectory, "Alice.png");
        File.WriteAllBytes(managedCharacterSource, [137, 80, 78, 71, 3]);
        var managedCharacterAsset = ProjectAssets.Import(
            project,
            projectPath,
            managedCharacterSource);

        Assert(
            managedCharacterAsset.Folder == "characters",
            "Importing a file already in files/characters did not keep its folder.");
        Assert(
            managedCharacterAsset.Path == "files/characters/Alice.png",
            "Importing a managed file changed its path.");
        Assert(
            Directory.GetFiles(Path.Combine(directory, "files", "backgrounds"), "Alice*.png").Length == 0,
            "Importing a managed character file copied it into backgrounds.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetImportManyReusesLookups()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-assets-many-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var first = Path.Combine(directory, "one.png");
        var second = Path.Combine(directory, "two.png");
        File.WriteAllBytes(first, [137, 80, 78, 71]);
        File.WriteAllBytes(second, [137, 80, 78, 71, 2]);
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();

        var imported = ProjectAssets.ImportMany(project, projectPath, [first, second, first]);

        Assert(imported.Count == 3, "Batch import did not return one result per requested source.");
        Assert(project.Assets.Count == 2, "Batch import duplicated a repeated source file.");
        Assert(
            ReferenceEquals(imported[0], imported[2]),
            "Batch import did not reuse the already registered asset for a repeated source.");
        Assert(
            project.Assets.Select(asset => asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "Batch import produced duplicate asset ids.");

        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "NovelEngine.Core",
            "ProjectAssets.cs"));
        var importManyBody = ExtractMethodBody(
            source,
            "public static IReadOnlyList<NovelAsset> ImportMany");
        var importCoreBody = ExtractMethodBody(
            source,
            "private static NovelAsset ImportCore");
        var uniquePathBody = ExtractMethodBody(source, "private static string UniquePath");
        var pathExistsBody = ExtractMethodBody(
            source,
            "private static bool FileSystemPathExists");

        Assert(
            CountOccurrences(importManyBody, "BuildAssetIdSet(project)") == 1
                && CountOccurrences(importManyBody, "BuildAssetFolderSet(project)") == 1
                && CountOccurrences(importManyBody, "BuildAssetFullPathMap(project, projectPath)") == 1
                && importManyBody.Contains("ImportCore(", StringComparison.Ordinal),
            "Batch import should build lookup state once and reuse it for all files.");
        Assert(
            importCoreBody.Contains("knownAssetsByFullPath.TryGetValue(source, out var existing)", StringComparison.Ordinal)
                && importCoreBody.Contains("EnsureFolder(project, folder, knownAssetFolders)", StringComparison.Ordinal)
                && importCoreBody.Contains("CreateUniqueAssetId(", StringComparison.Ordinal)
                && importCoreBody.Contains("knownAssetIds", StringComparison.Ordinal)
                && importCoreBody.Contains("knownAssetsByFullPath[source] = asset", StringComparison.Ordinal)
                && importCoreBody.Contains("knownAssetsByFullPath[Path.GetFullPath(target)] = asset", StringComparison.Ordinal),
            "Shared import implementation should use cached path, folder, and id lookups.");
        Assert(
            importCoreBody.Contains("FileSystemPathExists(target)", StringComparison.Ordinal)
                && uniquePathBody.Contains("FileSystemPathExists(target)", StringComparison.Ordinal)
                && pathExistsBody.Contains("File.Exists(path) || Directory.Exists(path)", StringComparison.Ordinal),
            "Asset path collision checks should avoid both existing files and directories.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectDefaultFileStructureIsCreated()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-structure-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();

        var changes = ProjectAssets.EnsureDefaultStructure(project, projectPath);

        Assert(changes == ProjectAssets.DefaultProjectFolders.Count, "Default folders were not registered.");
        foreach (var folder in ProjectAssets.DefaultProjectFolders)
        {
            Assert(
                project.AssetFolders.Contains(folder),
                $"Folder {folder} was not added to the project.");
            Assert(
                Directory.Exists(Path.Combine(directory, "files", folder)),
                $"Physical folder {folder} was not created.");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncDiscoversFilesFromDisk()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-sync-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var physicalFolder = Path.Combine(directory, "files", "backgrounds");
        var emptyNestedFolder = Path.Combine(directory, "files", "characters", "heroes");
        Directory.CreateDirectory(physicalFolder);
        Directory.CreateDirectory(emptyNestedFolder);
        var imagePath = Path.Combine(physicalFolder, "MountFuji.jpg");
        File.WriteAllBytes(imagePath, [255, 216, 255, 217]);
        var voiceFolder = Path.Combine(directory, "files", "voices");
        Directory.CreateDirectory(voiceFolder);
        var voicePath = Path.Combine(voiceFolder, "hero-blip.wav");
        File.WriteAllBytes(voicePath, [82, 73, 70, 70, 0, 0, 0, 0, 87, 65, 86, 69]);
        var project = NovelProject.CreateDefault();

        var changes = ProjectAssets.SyncFromDisk(project, projectPath);

        Assert(changes >= 4, "Sync did not report the discovered folders and files.");
        Assert(project.AssetFolders.Contains("backgrounds"), "Physical folder was not registered.");
        Assert(project.AssetFolders.Contains("characters"), "Physical parent folder was not registered.");
        Assert(project.AssetFolders.Contains("characters/heroes"), "Physical empty nested folder was not registered.");
        Assert(project.AssetFolders.Contains("voices"), "Physical voices folder was not registered.");
        Assert(project.Assets.Count == 2, "Physical files were not registered.");
        var image = project.Assets.Single(asset => asset.Path.EndsWith("MountFuji.jpg", StringComparison.Ordinal));
        var voice = project.Assets.Single(asset => asset.Path.EndsWith("hero-blip.wav", StringComparison.Ordinal));
        Assert(image.Kind == AssetKind.Image, "Discovered image kind was not detected.");
        Assert(image.Folder == "backgrounds", "Discovered image folder was wrong.");
        Assert(
            image.Path == "files/backgrounds/MountFuji.jpg",
            "Discovered image path should point into files.");
        Assert(voice.Kind == AssetKind.Audio, "Discovered voice kind was not detected.");
        Assert(voice.Folder == "voices", "Discovered voice folder was wrong.");
        Assert(
            voice.Path == "files/voices/hero-blip.wav",
            "Discovered voice path should point into files.");
        Assert(
            ProjectAssets.SyncFromDisk(project, projectPath) == 0,
            "Second sync created duplicate records.");
        Assert(project.Assets.Count == 2, "Second sync duplicated an asset.");
        Assert(
            Directory.GetFiles(physicalFolder, "MountFuji-*.jpg").Length == 0,
            "Sync created duplicate physical image files.");
        Assert(
            Directory.GetFiles(voiceFolder, "hero-blip-*.wav").Length == 0,
            "Sync created duplicate physical voice files.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncPreservesManagedFilePaths()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-sync-paths-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var backgroundFolder = Path.Combine(directory, "files", "backgrounds");
        var characterFolder = Path.Combine(directory, "files", "characters");
        Directory.CreateDirectory(backgroundFolder);
        Directory.CreateDirectory(characterFolder);
        File.WriteAllBytes(Path.Combine(backgroundFolder, "hero.png"), [137, 80, 78, 71]);
        File.WriteAllBytes(Path.Combine(characterFolder, "hero.png"), [137, 80, 78, 71, 2]);
        var project = NovelProject.CreateDefault();

        var changes = ProjectAssets.SyncFromDisk(project, projectPath);

        Assert(changes >= 4, "Sync did not report discovered folders and files.");
        Assert(project.Assets.Count == 2, "Sync did not register both managed files.");
        Assert(
            project.Assets.Select(asset => asset.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2,
            "Sync reused the same asset id for files with matching names.");
        Assert(
            project.Assets.Any(asset =>
                asset.Folder == "backgrounds"
                && asset.Path == "files/backgrounds/hero.png"),
            "Sync changed the managed background file path.");
        Assert(
            project.Assets.Any(asset =>
                asset.Folder == "characters"
                && asset.Path == "files/characters/hero.png"),
            "Sync changed the managed character file path.");
        Assert(
            Directory.GetFiles(backgroundFolder, "hero-*.png").Length == 0
                && Directory.GetFiles(characterFolder, "hero-*.png").Length == 0,
            "Sync copied managed files instead of registering them in place.");
        Assert(
            ProjectAssets.SyncFromDisk(project, projectPath) == 0,
            "Second sync duplicated managed files.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncNormalizesExistingPathSeparators()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-sync-separators-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var backgroundFolder = Path.Combine(directory, "files", "backgrounds");
        Directory.CreateDirectory(backgroundFolder);
        File.WriteAllBytes(Path.Combine(backgroundFolder, "hero.png"), [137, 80, 78, 71]);
        var project = NovelProject.CreateDefault();
        project.Assets.Add(new NovelAsset
        {
            Id = "hero",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = @"files\backgrounds\hero.png",
        });

        var changes = ProjectAssets.SyncFromDisk(project, projectPath);

        Assert(
            changes == 1,
            "Sync should only register the missing folder for an existing backslash path.");
        Assert(project.AssetFolders.Contains("backgrounds"), "Sync did not register the physical folder.");
        Assert(project.Assets.Count == 1, "Sync duplicated an existing backslash path.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncRemovesMissingManagedFiles()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-sync-prune-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var existingFolder = Path.Combine(directory, "files", "backgrounds");
        Directory.CreateDirectory(existingFolder);
        File.WriteAllBytes(Path.Combine(existingFolder, "existing.png"), [137, 80, 78, 71]);
        var project = NovelProject.CreateDefault();
        ProjectAssets.CreateFolder(project, "custom/empty");
        ProjectAssets.CreateFolder(project, "external");
        project.Assets.Add(new NovelAsset
        {
            Id = "missing_bg",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = "files/backgrounds/missing.png",
        });
        project.Assets.Add(new NovelAsset
        {
            Id = "missing_voice",
            Kind = AssetKind.Audio,
            Folder = "voices",
            Path = "files/voices/missing.wav",
        });
        project.Assets.Add(new NovelAsset
        {
            Id = "external_ref",
            Kind = AssetKind.Image,
            Folder = "external",
            Path = "assets/external/missing.png",
        });

        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = "@missing_bg";
        scene.InheritCharacters = false;
        scene.Characters.Add(new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
            VoiceSound = "@missing_voice",
            VoiceSounds = ["@missing_voice"],
        });
        project.Characters.Add(new CharacterPlacement
        {
            Id = "library_hero",
            Name = "Library Hero",
            VoiceSound = "@missing_voice",
            VoiceSounds = ["@missing_voice"],
        });

        var changes = ProjectAssets.SyncFromDisk(project, projectPath);

        Assert(changes >= 3, "Sync did not report removed and discovered managed files.");
        Assert(project.FindAsset("missing_bg") is null, "Missing managed image asset stayed registered.");
        Assert(project.FindAsset("missing_voice") is null, "Missing managed voice asset stayed registered.");
        Assert(project.FindAsset("external_ref") is not null, "Non-managed external asset was pruned.");
        Assert(
            !project.AssetFolders.Contains("custom")
                && !project.AssetFolders.Contains("custom/empty"),
            "Missing custom physical folders stayed registered.");
        Assert(project.AssetFolders.Contains("external"), "Folder with a non-managed asset was pruned.");
        Assert(scene.Background.Length == 0, "Deleted managed background reference stayed on the node.");
        Assert(scene.Characters[0].VoiceSound.Length == 0, "Deleted managed voice stayed as node primary voice.");
        Assert(scene.Characters[0].VoiceSounds.Count == 0, "Deleted managed voice stayed in node voice list.");
        Assert(project.Characters[0].VoiceSound.Length == 0, "Deleted managed voice stayed as library primary voice.");
        Assert(project.Characters[0].VoiceSounds.Count == 0, "Deleted managed voice stayed in library voice list.");
        Assert(
            project.Assets.Any(asset => asset.Path == "files/backgrounds/existing.png"),
            "Existing managed file was not discovered after pruning missing assets.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncRemovesMissingFilesRootAssets()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-sync-root-prune-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        ProjectAssets.CreateFolder(project, "backgrounds");
        ProjectAssets.CreateFolder(project, "custom/empty");
        ProjectAssets.CreateFolder(project, "external");
        project.Assets.Add(new NovelAsset
        {
            Id = "missing_bg",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = "files/backgrounds/missing.png",
        });
        project.Assets.Add(new NovelAsset
        {
            Id = "missing_voice",
            Kind = AssetKind.Audio,
            Folder = "voices",
            Path = "files/voices/missing.wav",
        });
        project.Assets.Add(new NovelAsset
        {
            Id = "external_ref",
            Kind = AssetKind.Image,
            Folder = "external",
            Path = "assets/external/missing.png",
        });

        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = "@missing_bg";
        scene.InheritCharacters = false;
        scene.Characters.Add(new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
            VoiceSound = "@missing_voice",
            VoiceSounds = ["@missing_voice"],
        });

        var changes = ProjectAssets.SyncFromDisk(project, projectPath);

        Assert(changes == 4, "Sync did not report missing managed assets and folders under a missing files root.");
        Assert(project.FindAsset("missing_bg") is null, "Missing files root image asset stayed registered.");
        Assert(project.FindAsset("missing_voice") is null, "Missing files root voice asset stayed registered.");
        Assert(project.FindAsset("external_ref") is not null, "Missing files root pruned a non-managed asset.");
        Assert(project.AssetFolders.Contains("backgrounds"), "Missing files root pruned a default folder.");
        Assert(
            !project.AssetFolders.Contains("custom")
                && !project.AssetFolders.Contains("custom/empty"),
            "Missing files root kept custom folders without files or assets.");
        Assert(project.AssetFolders.Contains("external"), "Missing files root pruned a folder with a non-managed asset.");
        Assert(scene.Background.Length == 0, "Missing files root background reference stayed on the node.");
        Assert(scene.Characters[0].VoiceSound.Length == 0, "Missing files root voice stayed as primary voice.");
        Assert(scene.Characters[0].VoiceSounds.Count == 0, "Missing files root voice stayed in the voice list.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectAssetSyncCachesGeneratedLookups()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectAssets.cs"));
    var syncBody = ExtractMethodBody(source, "public static int SyncFromDisk");
    var registerBody = ExtractMethodBody(
        source,
        "private static NovelAsset RegisterManagedFile");
    var removeMissingBody = ExtractMethodBody(
        source,
        "private static int RemoveMissingManagedAssets");
    var removeFoldersBody = ExtractMethodBody(
        source,
        "private static int RemoveMissingManagedFolders");
    var defaultFolderBody = ExtractMethodBody(
        source,
        "private static bool IsDefaultProjectFolder");
    var ensureFolderBody = ExtractMethodBody(
        source,
        "private static int EnsureFolder");
    var createBody = ExtractMethodBody(
        source,
        "private static string CreateUniqueAssetId");
    var idSetBody = ExtractMethodBody(
        source,
        "private static HashSet<string> BuildAssetIdSet");
    var folderSetBody = ExtractMethodBody(
        source,
        "private static HashSet<string> BuildAssetFolderSet");
    var pruneIndex = syncBody.IndexOf(
        "var changes = RemoveMissingManagedAssets(project, projectPath);",
        StringComparison.Ordinal);
    var pruneFoldersIndex = syncBody.IndexOf(
        "changes += RemoveMissingManagedFolders(project, root);",
        StringComparison.Ordinal);
    var missingRootIndex = syncBody.IndexOf(
        "if (!Directory.Exists(root))",
        StringComparison.Ordinal);

    Assert(
        syncBody.Contains("BuildAssetIdSet(project)", StringComparison.Ordinal),
        "Asset sync should build the known id set once.");
    Assert(
        syncBody.Contains("RemoveMissingManagedAssets(project, projectPath)", StringComparison.Ordinal),
        "Asset sync should prune missing managed assets before rebuilding lookup sets.");
    Assert(
        pruneIndex >= 0
            && pruneFoldersIndex > pruneIndex
            && missingRootIndex > pruneFoldersIndex,
        "Asset sync should prune stale managed assets even when the files root is missing.");
    Assert(
        syncBody.Contains("BuildAssetFolderSet(project)", StringComparison.Ordinal),
        "Asset sync should build the known folder set once.");
    Assert(
        syncBody.Contains("knownAssetIds", StringComparison.Ordinal)
            && syncBody.Contains("knownAssetFolders", StringComparison.Ordinal)
            && syncBody.Contains("RegisterManagedFile(", StringComparison.Ordinal),
        "Asset sync should pass known lookup sets through file registration.");
    Assert(
        syncBody.Contains(
            "var knownRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);",
            StringComparison.Ordinal)
            && syncBody.Contains("foreach (var asset in project.Assets)", StringComparison.Ordinal)
            && syncBody.Contains("knownRelativePaths.Add(NormalizeRelativeAssetPath(asset.Path));", StringComparison.Ordinal)
            && !syncBody.Contains(".Select(", StringComparison.Ordinal)
            && !syncBody.Contains(".ToHashSet(", StringComparison.Ordinal),
        "Asset sync should build known relative paths directly without LINQ pipelines.");
    Assert(
        registerBody.Contains(
            "CreateUniqueAssetId(\r\n            MakeId(Path.GetFileNameWithoutExtension(fullPath)),\r\n            knownAssetIds)",
            StringComparison.Ordinal)
            || registerBody.Contains(
                "CreateUniqueAssetId(\n            MakeId(Path.GetFileNameWithoutExtension(fullPath)),\n            knownAssetIds)",
                StringComparison.Ordinal),
        "Managed file registration should use the shared id set.");
    Assert(
        registerBody.Contains(
            "EnsureFolder(project, folder, knownAssetFolders)",
            StringComparison.Ordinal),
        "Managed file registration should use the shared folder set.");
    Assert(
        !syncBody.Contains("FindAsset(", StringComparison.Ordinal)
            && !registerBody.Contains("FindAsset(", StringComparison.Ordinal),
        "Asset sync should not use linear asset lookup while registering files.");
    Assert(
        removeMissingBody.Contains(
            "for (var index = project.Assets.Count - 1; index >= 0; index--)",
            StringComparison.Ordinal)
            && removeMissingBody.Contains(
                "project.ReplaceAssetReference(asset.Id, string.Empty);",
                StringComparison.Ordinal)
            && removeMissingBody.Contains("project.Assets.RemoveAt(index);", StringComparison.Ordinal)
            && !removeMissingBody.Contains("RemoveAll(", StringComparison.Ordinal),
        "Missing managed asset pruning should use a direct reverse pass and clean references.");
    Assert(
        removeFoldersBody.Contains(
            "for (var index = project.AssetFolders.Count - 1; index >= 0; index--)",
            StringComparison.Ordinal)
            && removeFoldersBody.Contains("IsDefaultProjectFolder(folder)", StringComparison.Ordinal)
            && removeFoldersBody.Contains("HasAssetInFolder(project, folder)", StringComparison.Ordinal)
            && removeFoldersBody.Contains("Directory.Exists(GetManagedFolderDirectory(root, folder))", StringComparison.Ordinal)
            && removeFoldersBody.Contains("project.AssetFolders.RemoveAt(index);", StringComparison.Ordinal)
            && !removeFoldersBody.Contains("RemoveAll(", StringComparison.Ordinal),
        "Missing custom folder pruning should use a direct reverse pass and preserve folders with assets.");
    Assert(
        defaultFolderBody.Contains("for (var index = 0; index < DefaultProjectFolders.Count; index++)", StringComparison.Ordinal)
            && !defaultFolderBody.Contains(".Contains(", StringComparison.Ordinal),
        "Default folder checks should use one direct pass over the fixed folder list.");
    Assert(
        !ensureFolderBody.Contains("AssetFolders.Contains", StringComparison.Ordinal),
        "Folder registration should use the cached folder set.");
    Assert(
        createBody.Contains("knownAssetIds.Add(id)", StringComparison.Ordinal),
        "Unique asset id creation should claim ids through the hash set.");
    Assert(
        ensureFolderBody.Contains("knownFolders.Add(current)", StringComparison.Ordinal),
        "Folder registration should claim folders through the hash set.");
    Assert(
        idSetBody.Contains(
            "new HashSet<string>(StringComparer.OrdinalIgnoreCase)",
            StringComparison.Ordinal)
            && idSetBody.Contains("foreach (var asset in project.Assets)", StringComparison.Ordinal)
            && idSetBody.Contains("ids.Add(asset.Id)", StringComparison.Ordinal)
            && !idSetBody.Contains(".Select(", StringComparison.Ordinal)
            && !idSetBody.Contains(".ToHashSet(", StringComparison.Ordinal),
        "Asset id set should be built in one direct pass.");
    Assert(
        folderSetBody.Contains("foreach (var folder in project.AssetFolders)", StringComparison.Ordinal)
            && folderSetBody.Contains("folders.Add(folder)", StringComparison.Ordinal)
            && !folderSetBody.Contains(".ToHashSet(", StringComparison.Ordinal),
        "Asset folder set should be built in one direct pass.");
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
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = AssetReference.Create(asset.Id);

        ProjectAssets.CreateFolder(project, "characters/heroes");
        var conflictingTarget = Path.Combine(
            directory,
            "files",
            "characters",
            "heroes",
            "hero.png");
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingTarget)!);
        File.WriteAllBytes(conflictingTarget, [137, 80, 78, 71, 2]);
        Directory.CreateDirectory(Path.Combine(
            Path.GetDirectoryName(conflictingTarget)!,
            "hero-2.png"));
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
        Assert(File.Exists(conflictingTarget), "Existing target file was overwritten.");
        Assert(
            Path.GetFileName(ProjectAssets.ResolvePath(projectPath, asset))
                == "hero-3.png",
            "Moved asset reused a target path occupied by an existing directory.");
        Assert(
            project.ResolveAssetReference(scene.Background) == asset.Path,
            "Moved asset reference did not resolve to the new asset path.");

        var missingAsset = new NovelAsset
        {
            Id = "missing_move",
            Kind = AssetKind.Image,
            Path = "files/backgrounds/missing.png",
            Folder = "backgrounds",
        };
        project.Assets.Add(missingAsset);
        AssertThrows<FileNotFoundException>(
            () => ProjectAssets.MoveAsset(
                project,
                projectPath,
                missingAsset,
                "characters/heroes"),
            "Moving a missing physical asset should fail.");
        Assert(
            missingAsset.Folder == "backgrounds"
                && missingAsset.Path == "files/backgrounds/missing.png",
            "Failed asset move changed the project model.");

        var physicalRenameConflict = Path.Combine(directory, "files", "cast");
        Directory.CreateDirectory(physicalRenameConflict);
        AssertThrows<InvalidOperationException>(
            () => ProjectAssets.RenameFolder(
                project,
                projectPath,
                "characters",
                "cast"),
            "Renaming into an existing physical folder should fail.");
        Assert(
            asset.Folder == "characters/heroes"
                && Directory.Exists(Path.Combine(directory, "files", "characters")),
            "Failed folder rename changed the project model or moved files.");
        Directory.Delete(physicalRenameConflict);

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
        Assert(
            !Directory.Exists(Path.Combine(directory, "files", "characters")),
            "Old physical folder was left behind after rename.");
        Assert(
            File.Exists(Path.Combine(directory, "files", "cast", "heroes", "hero.png")),
            "Untracked physical file did not move with the renamed folder.");
        AssertThrows<InvalidOperationException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, "cast/heroes"),
            "Deleting a folder with registered assets should fail.");
        AssertThrows<InvalidOperationException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, "cast"),
            "Deleting a parent folder with registered nested assets should fail.");

        ProjectAssets.CreateFolder(project, "unused/child");
        var unusedDirectory = Path.Combine(directory, "files", "unused");
        var unusedChildDirectory = Path.Combine(unusedDirectory, "child");
        Directory.CreateDirectory(unusedChildDirectory);
        AssertThrows<InvalidOperationException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, "unused"),
            "Deleting a folder with nested folders should fail.");

        var looseFile = Path.Combine(unusedChildDirectory, "loose.txt");
        File.WriteAllText(looseFile, "not imported yet");
        AssertThrows<InvalidOperationException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, "unused/child"),
            "Deleting a physical folder with untracked files should fail.");
        Assert(
            project.AssetFolders.Contains("unused/child"),
            "Folder was removed from the model after a failed physical delete.");
        File.Delete(looseFile);

        ProjectAssets.DeleteFolder(project, projectPath, "unused/child");
        ProjectAssets.DeleteFolder(project, projectPath, "unused");
        Assert(
            !project.AssetFolders.Contains("unused/child")
                && !project.AssetFolders.Contains("unused"),
            "Deleted empty folders were left in the project model.");
        Assert(
            !Directory.Exists(unusedChildDirectory)
                && !Directory.Exists(unusedDirectory),
            "Deleted empty folders were left on disk.");
        var filesRoot = Path.Combine(directory, "files");
        AssertThrows<InvalidDataException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, string.Empty),
            "Deleting an empty folder name should fail.");
        AssertThrows<InvalidDataException>(
            () => ProjectAssets.DeleteFolder(project, projectPath, "/"),
            "Deleting a root-like folder name should fail.");
        Assert(
            Directory.Exists(filesRoot),
            "Failed root-like folder deletion removed the managed files root.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AssetFolderDeleteScansDirectoriesOnce()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectAssets.cs"));
    var body = ExtractMethodBody(source, "public static void DeleteFolder");
    var renameBody = ExtractMethodBody(source, "public static void RenameFolder");
    var folderExistsBody = ExtractMethodBody(source, "private static bool FolderExists");
    var hasAssetBody = ExtractMethodBody(source, "private static bool HasAssetInFolder");
    var hasNestedBody = ExtractMethodBody(source, "private static bool HasNestedFolder");
    var directoryHasEntriesBody = ExtractMethodBody(source, "private static bool DirectoryHasEntries");
    var removeFolderBody = ExtractMethodBody(source, "private static void RemoveFolder");
    var pathExistsBody = ExtractMethodBody(source, "private static bool FileSystemPathExists");
    var pathsEqualBody = ExtractMethodBody(source, "private static bool PathsEqual");
    var managedDirectoryCalls = CountOccurrences(
        body,
        "ManagedFolderDirectories(projectPath, folder)");

    Assert(
        renameBody.Contains("FolderExists(project, target)", StringComparison.Ordinal)
            && renameBody.Contains("!PathsEqual(sourceDirectory, targetDirectory)", StringComparison.Ordinal)
            && renameBody.Contains("FileSystemPathExists(targetDirectory)", StringComparison.Ordinal)
            && !renameBody.Contains(".Any(", StringComparison.Ordinal),
        "RenameFolder should check model and physical target folders before moving.");
    Assert(
        managedDirectoryCalls == 1,
        "DeleteFolder should enumerate managed and legacy directories once.");
    Assert(
        body.Contains("var directories = new List<string>();", StringComparison.Ordinal)
            && body.Contains("directories.Add(directory);", StringComparison.Ordinal),
        "DeleteFolder should collect existing empty directories during validation.");
    Assert(
        !body.Contains(".Where(Directory.Exists)", StringComparison.Ordinal)
            && !body.Contains(".ToList()", StringComparison.Ordinal)
            && !body.Contains(".Any(", StringComparison.Ordinal)
            && !body.Contains("RemoveAll(", StringComparison.Ordinal),
        "DeleteFolder should not allocate a filtered directory list before validation.");
    Assert(
        body.Contains("HasAssetInFolder(project, folder)", StringComparison.Ordinal)
            && body.Contains("HasNestedFolder(project, folder)", StringComparison.Ordinal)
            && body.Contains("DirectoryHasEntries(directory)", StringComparison.Ordinal)
            && body.Contains("RemoveFolder(project, folder)", StringComparison.Ordinal),
        "DeleteFolder should route folder checks and removal through direct helpers.");
    Assert(
        folderExistsBody.Contains("for (var index = 0; index < project.AssetFolders.Count; index++)", StringComparison.Ordinal)
            && hasAssetBody.Contains("foreach (var asset in project.Assets)", StringComparison.Ordinal)
            && hasNestedBody.Contains("foreach (var candidate in project.AssetFolders)", StringComparison.Ordinal)
            && directoryHasEntriesBody.Contains(".GetEnumerator()", StringComparison.Ordinal)
            && removeFolderBody.Contains("for (var index = project.AssetFolders.Count - 1; index >= 0; index--)", StringComparison.Ordinal)
            && !folderExistsBody.Contains(".Any(", StringComparison.Ordinal)
            && !hasAssetBody.Contains(".Any(", StringComparison.Ordinal)
            && !hasNestedBody.Contains(".Any(", StringComparison.Ordinal)
            && !directoryHasEntriesBody.Contains(".Any(", StringComparison.Ordinal)
            && !removeFolderBody.Contains("RemoveAll(", StringComparison.Ordinal),
        "Asset folder helpers should use direct loops without LINQ predicates.");
    Assert(
        pathExistsBody.Contains("File.Exists(path) || Directory.Exists(path)", StringComparison.Ordinal)
            && pathsEqualBody.Contains("Path.GetFullPath(left).TrimEnd(", StringComparison.Ordinal)
            && pathsEqualBody.Contains("Path.GetFullPath(right).TrimEnd(", StringComparison.Ordinal),
        "Physical path helpers should compare normalized paths and detect files or directories.");
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

static void ProjectLanguageSyntaxSpansRespectLimit()
{
    const string source = """
        novel "Syntax"
        asset bg : image "assets/bg.png"
        node start : start {
            background @bg
            next "End" -> none
        }
        """;

    var allSpans = ProjectLanguage.GetSyntaxSpans(source);

    Assert(allSpans.Count > 4, "Test source should produce enough syntax spans.");
    Assert(
        !ProjectLanguage.TryGetSyntaxSpans(source, 4, out var limitedSpans),
        "Limited syntax span collection should report overflow.");
    Assert(
        limitedSpans.Count == 0,
        "Overflowed syntax span collection should not return partial highlights.");
    Assert(
        ProjectLanguage.TryGetSyntaxSpans(
            source,
            allSpans.Count,
            out var exactSpans),
        "Syntax span collection should accept the exact boundary.");
    Assert(
        allSpans.SequenceEqual(exactSpans),
        "Limited syntax span collection changed the highlighted spans.");
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
            character hero {
                pla
            }
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

    var characterCaret = source.IndexOf("pla", StringComparison.Ordinal) + 3;
    var characterCompletions = ProjectLanguage.GetCompletions(source, characterCaret);
    Assert(
        characterCompletions.Items.Any(
            item => item.InsertText == "placement (960, 500)"),
        "Character transform completion was not suggested.");
}

static void ProjectLanguageCompletesNoisyScopesQuickly()
{
    var builder = new System.Text.StringBuilder();
    builder.AppendLine("novel \"Noisy Completions\"");
    for (var index = 0; index < 1_000; index++)
    {
        builder.AppendLine($"# ignored {{ comment scope {index} }}");
        builder.AppendLine(
            $"asset bg_{index} : image \"files/backgrounds/{{ignored-{index}}}.png\"");
    }
    builder.AppendLine("node start : scene {");
    builder.AppendLine("    te");
    builder.AppendLine("}");

    var source = builder.ToString();
    var caret = source.LastIndexOf("te", StringComparison.Ordinal) + 2;

    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var completions = ProjectLanguage.GetCompletions(source, caret);
    stopwatch.Stop();

    Assert(
        completions.Items.Any(item => item.InsertText == "text \"\""),
        "Node body text completion was not suggested in a noisy document.");
    Assert(
        stopwatch.ElapsedMilliseconds < 1_500,
        $"Noisy DSL completion is too slow: {stopwatch.ElapsedMilliseconds}ms.");

    var projectLanguageSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectLanguage.cs"));
    var completionBody = ExtractMethodBody(
        projectLanguageSource,
        "public static ProjectLanguageCompletionContext GetCompletions");
    var ignoredBody = ExtractMethodBody(
        projectLanguageSource,
        "private static IReadOnlyList<ProjectLanguageSyntaxSpan> GetIgnoredSyntaxSpans");
    var completionItemsBody = ExtractMethodBody(
        projectLanguageSource,
        "private static IReadOnlyList<ProjectLanguageCompletion> BuildCompletionItems");

    Assert(
        completionBody.Contains("GetIgnoredSyntaxSpans(source)", StringComparison.Ordinal)
            && completionBody.Contains("BuildCompletionItems(candidates, normalizedPrefix)", StringComparison.Ordinal)
            && !completionBody.Contains("GetSyntaxSpans(source)", StringComparison.Ordinal)
            && !completionBody.Contains(".DistinctBy(", StringComparison.Ordinal)
            && !completionBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !completionBody.Contains(".Take(", StringComparison.Ordinal),
        "Completions should scan ignored text directly instead of running full syntax highlighting.");
    Assert(
        completionItemsBody.Contains(
            "new HashSet<string>(StringComparer.OrdinalIgnoreCase)",
            StringComparison.Ordinal)
            && completionItemsBody.Contains("directMatches.Sort(CompareCompletionLabels)", StringComparison.Ordinal)
            && completionItemsBody.Contains("normalizedMatches.Sort(CompareCompletionLabels)", StringComparison.Ordinal)
            && completionItemsBody.Contains("AddCompletionItems(items, directMatches, limit)", StringComparison.Ordinal)
            && completionItemsBody.Contains("AddCompletionItems(items, normalizedMatches, limit)", StringComparison.Ordinal)
            && !completionItemsBody.Contains(".Where(", StringComparison.Ordinal)
            && !completionItemsBody.Contains(".DistinctBy(", StringComparison.Ordinal)
            && !completionItemsBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !completionItemsBody.Contains(".Take(", StringComparison.Ordinal),
        "Completion items should be filtered, deduplicated, and limited without LINQ pipelines.");
    Assert(
        ignoredBody.Contains("while (index < source.Length)", StringComparison.Ordinal)
            && ignoredBody.Contains("ProjectLanguageSyntaxKind.Comment", StringComparison.Ordinal)
            && ignoredBody.Contains("ProjectLanguageSyntaxKind.String", StringComparison.Ordinal)
            && !ignoredBody.Contains(".Where(", StringComparison.Ordinal)
            && !ignoredBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Ignored syntax scan should collect comments and strings in one direct pass.");
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

    var sourceCode = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "ProjectLanguage.cs"));
    var scopeBody = ExtractMethodBody(
        sourceCode,
        "public static IReadOnlyList<ProjectLanguageScopeSpan> GetScopeSpans");
    Assert(
        scopeBody.Contains("GetIgnoredSyntaxSpans(source)", StringComparison.Ordinal)
            && !scopeBody.Contains("GetSyntaxSpans(source)", StringComparison.Ordinal)
            && !scopeBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Scope spans should avoid full syntax highlighting and LINQ sorting.");
}

static void BuildCompilerPreservesVisualScriptBlocks()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        $"novel-engine-visual-block-build-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        var dialogue = project.Nodes.Single(node => node.Kind == NodeKind.Dialogue);
        scene.ScriptBlocks.Add(
            new VisualScriptBlock
            {
                Id = "scene-route",
                Kind = VisualScriptBlockKind.SetVariable,
                VariableName = "route",
                Value = "\"intro\"",
            });
        dialogue.Outputs[0].ScriptBlocks.Add(
            new VisualScriptBlock
            {
                Id = "choice-score",
                Kind = VisualScriptBlockKind.AddVariable,
                VariableName = "score",
                Value = "1",
            });
        dialogue.Outputs[0].ConditionExpression = new VisualConditionExpression
        {
            Kind = VisualConditionKind.Comparison,
            VariableName = "score",
            Operator = ">=",
            Value = "1",
        };
        project.SourceCode = ProjectLanguage.Format(project);
        ProjectSerializer.Save(project, projectPath);

        var result = NovelBuildCompiler.Compile(
            project,
            projectPath,
            Path.Combine(directory, "build", "story"),
            debugSymbols: true);
        var built = NovelBuildCompiler.LoadBuild(result.ManifestPath).Project;

        Assert(
            built.FindNode(scene.Id)?.ScriptBlocks.Single().Id == "scene-route",
            "Build compiler dropped node visual script blocks.");
        Assert(
            built.FindNode(dialogue.Id)?.Outputs[0].ScriptBlocks.Single().Id == "choice-score",
            "Build compiler dropped output visual script blocks.");
        Assert(
            built.FindNode(dialogue.Id)?.Outputs[0].ConditionExpression?.VariableName == "score",
            "Build compiler dropped output visual condition expressions.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void BuildCompilerCopiesVisualBlocksWithNodeLookup()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Core",
        "NovelBuildCompiler.cs"));
    var body = ExtractMethodBody(source, "private static void CopyVisualScriptBlocks");
    var dictionaryIndex = body.IndexOf(
        "var destinationNodesById = new Dictionary<string, NovelNode>",
        StringComparison.Ordinal);
    var loopIndex = body.IndexOf(
        "foreach (var sourceNode in source.Nodes)",
        StringComparison.Ordinal);

    Assert(
        dictionaryIndex >= 0 && loopIndex > dictionaryIndex,
        "Build compiler should prepare destination node lookup before copying blocks.");
    Assert(
        body.Contains("destinationNodesById.TryGetValue", StringComparison.Ordinal),
        "Build compiler should use the destination node lookup while copying blocks.");
    Assert(
        !body.Contains("destination.FindNode", StringComparison.Ordinal),
        "Build compiler should not linearly search destination nodes for every source node.");
    Assert(
        !body.Contains(".Select(", StringComparison.Ordinal)
            && !body.Contains(".ToList(", StringComparison.Ordinal)
            && !body.Contains(".ToArray(", StringComparison.Ordinal),
        "Build compiler should clone visual blocks with direct loops.");
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
        var sourceVoice = Path.Combine(directory, "voice.mp3");
        File.WriteAllBytes(sourceVoice, [73, 68, 51, 3]);
        var sourceVoiceAlt = Path.Combine(directory, "voice-alt.wav");
        File.WriteAllBytes(sourceVoiceAlt, [82, 73, 70, 70, 40, 0, 0, 0]);
        var sourceLogo = Path.Combine(directory, "logo.png");
        File.WriteAllBytes(sourceLogo, [137, 80, 78, 71, 13]);
        var projectPath = Path.Combine(directory, "story.novel.json");
        var project = NovelProject.CreateDefault();
        var asset = ProjectAssets.Import(
            project,
            projectPath,
            sourceAsset,
            "backgrounds");
        var voiceAsset = ProjectAssets.Import(
            project,
            projectPath,
            sourceVoice,
            "voices");
        var voiceAltAsset = ProjectAssets.Import(
            project,
            projectPath,
            sourceVoiceAlt,
            "voices");
        var scene = project.Nodes.Single(node => node.Kind == NodeKind.Scene);
        scene.Background = AssetReference.Create(asset.Id);
        scene.InheritBackground = false;
        project.MainMenu.Background = AssetReference.Create(asset.Id);
        project.Characters.Add(
            new CharacterPlacement
            {
                Id = "library_hero",
                Name = "Library Hero",
                Sprite = sourceAsset,
                VoiceSound = sourceVoiceAlt,
                VoiceSounds = [sourceVoiceAlt],
            });
        project.MainMenu.Elements.Clear();
        project.MainMenu.Elements.Add(
            new MainMenuElement
            {
                Id = "custom-title",
                Kind = MainMenuElementKind.ImageLabel,
                Text = "Custom title",
                Image = sourceLogo,
                X = 120,
                Y = 80,
                CustomStyleCode = "align left; italic true",
            });
        scene.InheritCharacters = false;
        scene.Characters.Add(
            new CharacterPlacement
            {
                Id = "hero",
                Name = "Hero",
                VoiceSound = AssetReference.Create(voiceAsset.Id),
                VoiceSounds =
                [
                    AssetReference.Create(voiceAsset.Id),
                    AssetReference.Create(voiceAltAsset.Id),
                ],
                VoicePitch = 1.1,
                VoiceEveryNthCharacter = 2,
            });
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
        var builtVoiceAsset = loaded.Project.FindAsset(voiceAsset.Id)
            ?? throw new InvalidOperationException("Built voice asset is missing.");
        var builtVoiceAltAsset = loaded.Project.FindAsset(voiceAltAsset.Id)
            ?? throw new InvalidOperationException("Built alternate voice asset is missing.");
        var builtCharacter = loaded.Project
            .FindNode(scene.Id)!
            .Characters
            .Single(character => character.Id == "hero");
        var builtLibraryCharacter = loaded.Project.Characters.Single(
            character => character.Id == "library_hero");

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
            File.Exists(
                Path.Combine(
                    result.OutputDirectory,
                    builtVoiceAsset.Path.Replace(
                        '/',
                        Path.DirectorySeparatorChar))),
            "Compiled voice asset was not copied.");
        Assert(
            File.Exists(
                Path.Combine(
                    result.OutputDirectory,
                    builtVoiceAltAsset.Path.Replace(
                        '/',
                        Path.DirectorySeparatorChar))),
            "Compiled alternate voice asset was not copied.");
        Assert(
            builtCharacter.VoiceSound == AssetReference.Create(voiceAsset.Id),
            "Runtime project lost character voice sound.");
        Assert(
            builtCharacter.VoiceSounds.Count == 2
                && builtCharacter.VoiceSounds.Contains(
                    AssetReference.Create(voiceAsset.Id))
                && builtCharacter.VoiceSounds.Contains(
                    AssetReference.Create(voiceAltAsset.Id)),
            "Runtime project lost character voice sound list.");
        Assert(
            Math.Abs(builtCharacter.VoicePitch - 1.1) < 0.001,
            "Runtime project changed character voice pitch.");
        Assert(
            builtCharacter.VoiceEveryNthCharacter == 2,
            "Runtime project changed character voice frequency.");
        Assert(
            File.Exists(
                Path.Combine(
                    result.OutputDirectory,
                    builtLibraryCharacter.Sprite.Replace(
                        '/',
                        Path.DirectorySeparatorChar))),
            "Compiled library character sprite was not copied.");
        Assert(
            !Path.IsPathRooted(builtLibraryCharacter.VoiceSound)
                && builtLibraryCharacter.VoiceSound.StartsWith(
                    "files/external/",
                    StringComparison.Ordinal),
            "Compiled library character voice sound did not become a build-relative path.");
        Assert(
            builtLibraryCharacter.GetVoiceSounds().All(voice =>
                !Path.IsPathRooted(voice)
                && voice.StartsWith("files/external/", StringComparison.Ordinal)),
            "Compiled library character voice list did not become build-relative paths.");
        Assert(
            builtLibraryCharacter.GetVoiceSounds().All(voice =>
                File.Exists(
                    Path.Combine(
                        result.OutputDirectory,
                        voice.Replace('/', Path.DirectorySeparatorChar)))),
            "Compiled library character voice was not copied.");
        Assert(
            loaded.Manifest.DebugSymbols,
            "Debug build did not retain debug symbols.");
        Assert(
            !string.IsNullOrWhiteSpace(loaded.Project.SourceCode),
            "Debug build did not keep project source code.");
        Assert(
            loaded.Project.Nodes.Count == project.Nodes.Count,
            "Runtime project changed node count.");
        Assert(
            loaded.Project.MainMenu.Background == AssetReference.Create(asset.Id),
            "Runtime project lost main menu background.");
        var builtMenuElement = loaded.Project.MainMenu.Elements.Single();
        Assert(
            builtMenuElement.Id == "custom-title"
                && builtMenuElement.Text == "Custom title"
                && builtMenuElement.CustomStyleCode == "align left; italic true",
            "Runtime project lost custom main menu elements.");
        Assert(
            !Path.IsPathRooted(builtMenuElement.Image)
                && builtMenuElement.Image.StartsWith(
                    "files/external/",
                    StringComparison.Ordinal),
            "Compiled main menu image did not become a build-relative path.");
        Assert(
            File.Exists(
                Path.Combine(
                    result.OutputDirectory,
                    builtMenuElement.Image.Replace('/', Path.DirectorySeparatorChar))),
            "Compiled main menu image was not copied.");

        var releaseResult = NovelBuildCompiler.Compile(
            project,
            projectPath,
            Path.Combine(directory, "build", "story-release"),
            debugSymbols: false);
        var release = NovelBuildCompiler.LoadBuild(releaseResult.ManifestPath);
        Assert(
            !release.Manifest.DebugSymbols,
            "Release build kept debug symbol metadata enabled.");
        Assert(
            string.IsNullOrEmpty(release.Project.SourceCode),
            "Release build did not strip project source code.");

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

static string FindRepositoryRoot()
{
    var directory = AppContext.BaseDirectory;
    while (!File.Exists(Path.Combine(directory, "NovelEngine.sln")))
    {
        directory = Directory.GetParent(directory)?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    return directory;
}

static string ExtractMethodBody(string source, string methodName)
{
    var methodIndex = source.IndexOf(methodName, StringComparison.Ordinal);
    Assert(methodIndex >= 0, $"Method was not found: {methodName}.");

    var openBrace = source.IndexOf('{', methodIndex);
    Assert(openBrace >= 0, $"Method body was not found: {methodName}.");

    var depth = 0;
    for (var index = openBrace; index < source.Length; index++)
    {
        if (source[index] == '{')
        {
            depth++;
        }
        else if (source[index] == '}')
        {
            depth--;
            if (depth == 0)
            {
                return source[(openBrace + 1)..index];
            }
        }
    }

    throw new InvalidOperationException($"Method body was not closed: {methodName}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}
