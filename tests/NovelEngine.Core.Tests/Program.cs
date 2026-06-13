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

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
