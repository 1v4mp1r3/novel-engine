using System.IO;
using NovelEngine.Core;
using NovelEngine.Editor;

var tests = new (string Name, Action Run)[]
{
    ("workspace project creates default folders", WorkspaceProjectCreatesDefaultFolders),
    ("workspace project path avoids existing file", WorkspaceProjectPathAvoidsExistingFile),
    ("project resolver opens the only project in a folder", ProjectResolverOpensOnlyProject),
    ("project resolver prefers folder-named project", ProjectResolverPrefersFolderNamedProject),
    ("project resolver treats ambiguous folder as workspace", ProjectResolverTreatsAmbiguousFolderAsWorkspace),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception error)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
    }
}

if (failed > 0)
{
    Environment.Exit(1);
}

Console.WriteLine($"{tests.Length}/{tests.Length} tests passed");

static void WorkspaceProjectCreatesDefaultFolders()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = ProjectWorkspace.CreateProjectInDirectory(directory);

        Assert(File.Exists(projectPath), "Project file was not created.");
        Assert(
            Path.GetFileName(projectPath) == $"{Path.GetFileName(directory)}.novel.json",
            "Project file did not use the workspace folder name.");
        Assert(
            Directory.Exists(Path.Combine(directory, "autosaves")),
            "Autosave directory was not created.");

        foreach (var folder in ProjectAssets.DefaultProjectFolders)
        {
            Assert(
                Directory.Exists(Path.Combine(directory, "files", folder)),
                $"Default asset folder was not created: {folder}");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void WorkspaceProjectPathAvoidsExistingFile()
{
    var directory = CreateTempDirectory();
    try
    {
        var firstPath = ProjectWorkspace.CreateProjectInDirectory(directory);
        var secondPath = ProjectWorkspace.GetAvailableProjectPath(directory);

        Assert(firstPath != secondPath, "Available project path reused an existing file.");
        Assert(
            Path.GetFileName(secondPath) == $"{Path.GetFileName(directory)}-2.novel.json",
            "Available project path did not use the expected numeric suffix.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectResolverOpensOnlyProject()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = Path.Combine(directory, "custom-name.novel.json");
        File.WriteAllText(projectPath, "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath == projectPath, "Resolver did not open the only project file.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed workspace directory.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a folder with one project as empty.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectResolverPrefersFolderNamedProject()
{
    var directory = CreateTempDirectory();
    try
    {
        var preferredPath = Path.Combine(
            directory,
            $"{Path.GetFileName(directory)}.novel.json");
        var otherPath = Path.Combine(directory, "other.novel.json");
        File.WriteAllText(preferredPath, "{}");
        File.WriteAllText(otherPath, "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath == preferredPath, "Resolver did not prefer the folder-named project.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a preferred project folder as empty.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectResolverTreatsAmbiguousFolderAsWorkspace()
{
    var directory = CreateTempDirectory();
    try
    {
        File.WriteAllText(Path.Combine(directory, "first.novel.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "second.novel.json"), "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath is null, "Resolver picked a project from an ambiguous folder.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed ambiguous workspace directory.");
        Assert(result.CreatedEmptyWorkspace, "Resolver did not mark an ambiguous folder as workspace.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static string CreateTempDirectory()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "novel-engine-editor-tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
