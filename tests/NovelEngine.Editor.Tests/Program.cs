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
    ("project resolver expands quoted environment paths", ProjectResolverExpandsQuotedEnvironmentPaths),
    ("recent projects deduplicate and order entries", RecentProjectsDeduplicateAndOrderEntries),
    ("recent projects ignore missing paths", RecentProjectsIgnoreMissingPaths),
    ("recent projects keep workspace folders", RecentProjectsKeepWorkspaceFolders),
    ("recent projects keep only newest entries", RecentProjectsKeepOnlyNewestEntries),
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

static void ProjectResolverExpandsQuotedEnvironmentPaths()
{
    var directory = CreateTempDirectory();
    const string variableName = "NOVEL_ENGINE_TEST_PROJECT";
    var previous = Environment.GetEnvironmentVariable(variableName);
    try
    {
        var projectPath = Path.Combine(directory, "story.novel.json");
        File.WriteAllText(projectPath, "{}");
        Environment.SetEnvironmentVariable(variableName, directory);

        var result = ProjectOpenResolver.Resolve($"\"%{variableName}%\"");

        Assert(result.ProjectPath == projectPath, "Resolver did not expand the quoted environment path.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed the expanded workspace directory.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked an expanded project folder as empty.");
    }
    finally
    {
        Environment.SetEnvironmentVariable(variableName, previous);
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsDeduplicateAndOrderEntries()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var firstProject = Path.Combine(directory, "first.novel.json");
        var secondProject = Path.Combine(directory, "second.novel.json");
        File.WriteAllText(firstProject, "{}");
        File.WriteAllText(secondProject, "{}");

        WithRecentProjectsStore(storePath, () =>
        {
            RecentProjectsStore.Remember(firstProject);
            RecentProjectsStore.Remember(secondProject);
            RecentProjectsStore.Remember(firstProject.ToUpperInvariant());

            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 2, "Recent projects did not deduplicate paths.");
            Assert(
                entries[0].Path.Equals(firstProject, StringComparison.OrdinalIgnoreCase),
                "Recent projects did not move the reopened project to the top.");
            Assert(
                entries[0].DisplayName.Equals(
                    "first",
                    StringComparison.OrdinalIgnoreCase),
                "Recent project display name was not derived from the project file.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsIgnoreMissingPaths()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var existingProject = Path.Combine(directory, "existing.novel.json");
        var missingProject = Path.Combine(directory, "missing.novel.json");
        File.WriteAllText(existingProject, "{}");

        WithRecentProjectsStore(storePath, () =>
        {
            RecentProjectsStore.Remember(existingProject);
            RecentProjectsStore.Remember(missingProject);

            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects kept a missing path.");
            Assert(
                entries[0].Path == existingProject,
                "Recent projects lost the existing path while pruning missing entries.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsKeepWorkspaceFolders()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var workspace = Path.Combine(directory, "workspace");
        Directory.CreateDirectory(workspace);

        WithRecentProjectsStore(storePath, () =>
        {
            RecentProjectsStore.Remember(workspace);

            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects did not keep the workspace folder.");
            Assert(entries[0].Path == workspace, "Recent workspace path changed.");
            Assert(
                entries[0].DisplayName == "workspace",
                "Recent workspace display name was not derived from the folder.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsKeepOnlyNewestEntries()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var projectPaths = Enumerable.Range(0, 10)
            .Select(index => Path.Combine(directory, $"project-{index}.novel.json"))
            .ToList();
        foreach (var projectPath in projectPaths)
        {
            File.WriteAllText(projectPath, "{}");
        }

        WithRecentProjectsStore(storePath, () =>
        {
            foreach (var projectPath in projectPaths)
            {
                RecentProjectsStore.Remember(projectPath);
            }

            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 8, "Recent projects did not enforce the maximum entry count.");
            Assert(
                entries[0].Path == projectPaths[^1],
                "Recent projects did not keep the newest project first.");
            Assert(
                !entries.Any(entry => entry.Path == projectPaths[0]),
                "Recent projects kept the oldest overflow entry.");
            Assert(
                !entries.Any(entry => entry.Path == projectPaths[1]),
                "Recent projects kept the second oldest overflow entry.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void WithRecentProjectsStore(string storePath, Action action)
{
    const string variable = "NOVEL_ENGINE_RECENT_PROJECTS_PATH";
    var previous = Environment.GetEnvironmentVariable(variable);
    Environment.SetEnvironmentVariable(variable, storePath);
    try
    {
        action();
    }
    finally
    {
        Environment.SetEnvironmentVariable(variable, previous);
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
