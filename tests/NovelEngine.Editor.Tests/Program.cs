using System.Diagnostics;
using System.IO;
using System.Windows;
using NovelEngine.Core;
using NovelEngine.Editor;

var tests = new (string Name, Action Run)[]
{
    ("workspace project creates default folders", WorkspaceProjectCreatesDefaultFolders),
    ("workspace project path avoids existing file", WorkspaceProjectPathAvoidsExistingFile),
    ("workspace project can be resolved after creation", WorkspaceProjectCanBeResolvedAfterCreation),
    ("project resolver opens direct project file", ProjectResolverOpensDirectProjectFile),
    ("project resolver opens the only project in a folder", ProjectResolverOpensOnlyProject),
    ("project resolver opens the only json project in a folder", ProjectResolverOpensOnlyJsonProject),
    ("project resolver treats empty folder as workspace", ProjectResolverTreatsEmptyFolderAsWorkspace),
    ("project resolver prefers folder-named project", ProjectResolverPrefersFolderNamedProject),
    ("project resolver prefers folder-named json project", ProjectResolverPrefersFolderNamedJsonProject),
    ("project resolver treats ambiguous folder as workspace", ProjectResolverTreatsAmbiguousFolderAsWorkspace),
    ("project resolver expands quoted environment paths", ProjectResolverExpandsQuotedEnvironmentPaths),
    ("autosave snapshot paths avoid same second collisions", AutoSaveSnapshotPathsAvoidSameSecondCollisions),
    ("autosave pruning keeps newest snapshots", AutoSavePruningKeepsNewestSnapshots),
    ("autosave pruning skips locked snapshots", AutoSavePruningSkipsLockedSnapshots),
    ("recent projects deduplicate and order entries", RecentProjectsDeduplicateAndOrderEntries),
    ("recent projects ignore corrupt cache", RecentProjectsIgnoreCorruptCache),
    ("recent projects ignore blank cache entries", RecentProjectsIgnoreBlankCacheEntries),
    ("recent projects normalize quoted cache paths", RecentProjectsNormalizeQuotedCachePaths),
    ("recent projects remember quoted paths", RecentProjectsRememberQuotedPaths),
    ("recent projects ignore missing paths", RecentProjectsIgnoreMissingPaths),
    ("recent projects keep workspace folders", RecentProjectsKeepWorkspaceFolders),
    ("recent projects keep only newest entries", RecentProjectsKeepOnlyNewestEntries),
    ("asset list filter searches within selected folder", AssetListFilterSearchesWithinSelectedFolder),
    ("dispatcher debounce gate collapses pending requests", DispatcherDebounceGateCollapsesPendingRequests),
    ("bounded cache evicts least recently used entries", BoundedCacheEvictsLeastRecentlyUsedEntries),
    ("code editor performance policy limits expensive live work", CodeEditorPerformancePolicyLimitsExpensiveLiveWork),
    ("main menu drag position clamps and skips micro moves", MainMenuDragPositionClampsAndSkipsMicroMoves),
    ("scene editor transform clamps and skips micro moves", SceneEditorTransformClampsAndSkipsMicroMoves),
    ("graph connection curve bounds include control points", GraphConnectionCurveBoundsIncludeControlPoints),
    ("graph world hit areas ignore viewport offset", GraphWorldHitAreasIgnoreViewportOffset),
    ("graph hit test cache prefers topmost node", GraphHitTestCachePrefersTopmostNode),
    ("graph hit test cache returns ports", GraphHitTestCacheReturnsPorts),
    ("graph hit test cache crosses spatial cells", GraphHitTestCacheCrossesSpatialCells),
    ("graph hit test cache handles large graphs quickly", GraphHitTestCacheHandlesLargeGraphsQuickly),
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
        var autoSaveStem = Path.GetFileNameWithoutExtension(projectPath);
        var autoSaveFiles = Directory.GetFiles(
            Path.Combine(directory, "autosaves"),
            $"{autoSaveStem}-*.novel.json");
        Assert(autoSaveFiles.Length == 1, "Initial autosave snapshot was not created.");
        var project = ProjectSerializer.Load(projectPath);
        var snapshotProject = ProjectSerializer.Load(autoSaveFiles[0]);
        Assert(
            project.Title == Path.GetFileName(directory),
            "Project title did not use the workspace folder name.");
        Assert(
            snapshotProject.Title == project.Title,
            "Initial autosave snapshot did not contain the created project.");

        foreach (var folder in ProjectAssets.DefaultProjectFolders)
        {
            Assert(
                Directory.Exists(Path.Combine(directory, "files", folder)),
                $"Default asset folder was not created: {folder}");
            Assert(
                project.AssetFolders.Contains(folder, StringComparer.OrdinalIgnoreCase),
                $"Default asset folder was not saved in the project file: {folder}");
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

static void WorkspaceProjectCanBeResolvedAfterCreation()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = ProjectWorkspace.CreateProjectInDirectory(directory);

        var fromFile = ProjectOpenResolver.Resolve(projectPath);
        var fromFolder = ProjectOpenResolver.Resolve(directory);

        Assert(fromFile.ProjectPath == projectPath, "Created project did not resolve from its file path.");
        Assert(fromFile.WorkspaceDirectory == directory, "Created project file did not resolve to its workspace.");
        Assert(!fromFile.CreatedEmptyWorkspace, "Created project file resolved as an empty workspace.");
        Assert(fromFolder.ProjectPath == projectPath, "Created project did not resolve from its workspace folder.");
        Assert(fromFolder.WorkspaceDirectory == directory, "Created project folder changed workspace.");
        Assert(!fromFolder.CreatedEmptyWorkspace, "Created project folder resolved as an empty workspace.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectResolverOpensDirectProjectFile()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = Path.Combine(directory, "direct.novel.json");
        File.WriteAllText(projectPath, "{}");

        var result = ProjectOpenResolver.Resolve(projectPath);

        Assert(result.ProjectPath == projectPath, "Resolver did not open the direct project file.");
        Assert(result.WorkspaceDirectory == directory, "Resolver did not use the project file directory as workspace.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a direct project file as empty workspace.");
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

static void ProjectResolverOpensOnlyJsonProject()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = Path.Combine(directory, "custom-name.json");
        File.WriteAllText(projectPath, "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath == projectPath, "Resolver did not open the only .json project file.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed .json workspace directory.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a folder with one .json project as empty.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void ProjectResolverTreatsEmptyFolderAsWorkspace()
{
    var directory = CreateTempDirectory();
    try
    {
        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath is null, "Resolver picked a project from an empty folder.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed empty workspace directory.");
        Assert(result.CreatedEmptyWorkspace, "Resolver did not mark an empty folder as workspace.");
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

static void ProjectResolverPrefersFolderNamedJsonProject()
{
    var directory = CreateTempDirectory();
    try
    {
        var preferredPath = Path.Combine(
            directory,
            $"{Path.GetFileName(directory)}.json");
        var otherPath = Path.Combine(directory, "other.json");
        File.WriteAllText(preferredPath, "{}");
        File.WriteAllText(otherPath, "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(result.ProjectPath == preferredPath, "Resolver did not prefer the folder-named .json project.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a preferred .json project folder as empty.");
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

static void AutoSaveSnapshotPathsAvoidSameSecondCollisions()
{
    var directory = CreateTempDirectory();
    try
    {
        var timestamp = new DateTime(2026, 6, 17, 12, 34, 56, DateTimeKind.Utc);
        var first = AutoSaveStore.CreateSnapshotPath(directory, "story", timestamp);
        File.WriteAllText(first, "{}");

        var second = AutoSaveStore.CreateSnapshotPath(directory, "story", timestamp);

        Assert(first != second, "Autosave snapshot reused an existing path from the same second.");
        Assert(
            Path.GetFileName(second).StartsWith("story-", StringComparison.Ordinal)
                && Path.GetFileName(second).EndsWith("-2.novel.json", StringComparison.Ordinal),
            "Autosave snapshot did not use a deterministic collision suffix.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AutoSavePruningKeepsNewestSnapshots()
{
    var directory = CreateTempDirectory();
    try
    {
        var oldest = Path.Combine(directory, "story-20260617-100000.novel.json");
        var middle = Path.Combine(directory, "story-20260617-110000.novel.json");
        var newest = Path.Combine(directory, "story-20260617-120000.novel.json");
        var otherProject = Path.Combine(directory, "other-20260617-090000.novel.json");
        File.WriteAllText(oldest, "{}");
        File.WriteAllText(middle, "{}");
        File.WriteAllText(newest, "{}");
        File.WriteAllText(otherProject, "{}");
        File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 6, 17, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(middle, new DateTime(2026, 6, 17, 11, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(otherProject, new DateTime(2026, 6, 17, 9, 0, 0, DateTimeKind.Utc));

        AutoSaveStore.Prune(directory, "story", keepCount: 2);

        Assert(!File.Exists(oldest), "Autosave pruning kept the oldest snapshot.");
        Assert(File.Exists(middle), "Autosave pruning deleted a retained snapshot.");
        Assert(File.Exists(newest), "Autosave pruning deleted the newest snapshot.");
        Assert(File.Exists(otherProject), "Autosave pruning deleted a different project's snapshot.");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void AutoSavePruningSkipsLockedSnapshots()
{
    var directory = CreateTempDirectory();
    try
    {
        var oldest = Path.Combine(directory, "story-20260617-100000.novel.json");
        var locked = Path.Combine(directory, "story-20260617-110000.novel.json");
        var newest = Path.Combine(directory, "story-20260617-120000.novel.json");
        File.WriteAllText(oldest, "{}");
        File.WriteAllText(locked, "{}");
        File.WriteAllText(newest, "{}");
        File.SetLastWriteTimeUtc(oldest, new DateTime(2026, 6, 17, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(locked, new DateTime(2026, 6, 17, 11, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newest, new DateTime(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc));

        AutoSaveStore.Prune(
            directory,
            "story",
            keepCount: 1,
            delete: path =>
            {
                if (path.Equals(locked, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("locked");
                }
                File.Delete(path);
            });

        Assert(!File.Exists(oldest), "Autosave pruning stopped after a locked snapshot.");
        Assert(File.Exists(locked), "Autosave pruning deleted the simulated locked snapshot.");
        Assert(File.Exists(newest), "Autosave pruning deleted the newest snapshot.");
    }
    finally
    {
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

static void RecentProjectsIgnoreCorruptCache()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        File.WriteAllText(storePath, "{ this is not json");

        WithRecentProjectsStore(storePath, () =>
        {
            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 0, "Recent projects did not ignore a corrupt cache.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsIgnoreBlankCacheEntries()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var existingProject = Path.Combine(directory, "existing.novel.json");
        File.WriteAllText(existingProject, "{}");
        File.WriteAllText(
            storePath,
            $$"""
            [
              {
                "Path": "",
                "DisplayName": "Broken",
                "LastOpenedUtc": "2026-06-17T10:00:00Z"
              },
              {
                "Path": "{{existingProject.Replace("\\", "\\\\")}}",
                "DisplayName": "Existing",
                "LastOpenedUtc": "2026-06-17T11:00:00Z"
              }
            ]
            """);

        WithRecentProjectsStore(storePath, () =>
        {
            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects kept a blank cache entry.");
            Assert(entries[0].Path == existingProject, "Recent projects lost the usable cache entry.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsNormalizeQuotedCachePaths()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var existingProject = Path.Combine(directory, "quoted.novel.json");
        File.WriteAllText(existingProject, "{}");
        var quotedProject = $"  \"{existingProject.Replace("\\", "\\\\")}\"  ";
        File.WriteAllText(
            storePath,
            $$"""
            [
              {
                "Path": "{{quotedProject.Replace("\"", "\\\"")}}",
                "DisplayName": "",
                "LastOpenedUtc": "2026-06-17T11:00:00Z"
              }
            ]
            """);

        WithRecentProjectsStore(storePath, () =>
        {
            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects did not keep the quoted cache path.");
            Assert(entries[0].Path == existingProject, "Recent projects did not normalize the quoted cache path.");
            Assert(entries[0].DisplayName == "quoted", "Recent projects did not repair a blank display name.");
        });
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void RecentProjectsRememberQuotedPaths()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var projectPath = Path.Combine(directory, "quoted-input.novel.json");
        File.WriteAllText(projectPath, "{}");

        WithRecentProjectsStore(storePath, () =>
        {
            RecentProjectsStore.Remember($"  \"{projectPath}\"  ");

            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects did not remember the quoted input path.");
            Assert(entries[0].Path == projectPath, "Recent projects did not normalize the remembered path.");
            Assert(entries[0].DisplayName == "quoted-input", "Recent projects used the wrong display name.");
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

static void AssetListFilterSearchesWithinSelectedFolder()
{
    var assets = new[]
    {
        new NovelAsset
        {
            Id = "mount_fuji",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = "files/backgrounds/MountFuji.jpg",
        },
        new NovelAsset
        {
            Id = "forest",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = "files/backgrounds/Forest.png",
        },
        new NovelAsset
        {
            Id = "hero_voice",
            Kind = AssetKind.Audio,
            Folder = "voices",
            Path = "files/voices/hero.wav",
        },
    };

    var result = AssetListFilter.Apply(assets, "backgrounds", "картинка mount");

    Assert(result.Query == "картинка mount", "Asset search query was not normalized.");
    Assert(result.FolderAssetCount == 2, "Asset folder count ignored the selected folder.");
    Assert(result.Assets.Count == 1, "Asset search did not filter the selected folder.");
    Assert(result.Assets[0].Id == "mount_fuji", "Asset search returned the wrong asset.");
}

static void DispatcherDebounceGateCollapsesPendingRequests()
{
    var gate = new DispatcherDebounceGate();

    Assert(gate.TryRequest(), "First dispatcher refresh request should be queued.");
    Assert(gate.IsPending, "Dispatcher refresh gate should report a pending request.");
    Assert(!gate.TryRequest(), "Second pending dispatcher refresh request should be collapsed.");

    gate.Complete();

    Assert(!gate.IsPending, "Dispatcher refresh gate should clear its pending state.");
    Assert(gate.TryRequest(), "Dispatcher refresh gate should allow a request after completion.");
}

static void BoundedCacheEvictsLeastRecentlyUsedEntries()
{
    var cache = new BoundedCache<string, int>(2, StringComparer.OrdinalIgnoreCase);
    cache.Set("first", 1);
    cache.Set("second", 2);

    Assert(cache.TryGetValue("FIRST", out var first) && first == 1, "Cache lookup should refresh recency.");

    cache.Set("third", 3);

    Assert(cache.Count == 2, "Cache exceeded its configured capacity.");
    Assert(cache.TryGetValue("first", out first) && first == 1, "Recently used entry was evicted.");
    Assert(!cache.TryGetValue("second", out _), "Least recently used entry was not evicted.");
    Assert(cache.TryGetValue("third", out var third) && third == 3, "Newest entry was not cached.");

    cache.Clear();
    Assert(cache.Count == 0, "Cache did not clear entries.");
}

static void CodeEditorPerformancePolicyLimitsExpensiveLiveWork()
{
    var highlightedLimit = CodeEditorPerformancePolicy.MaxHighlightedCodeLength;
    var automaticCompletionLimit =
        CodeEditorPerformancePolicy.MaxAutomaticCompletionSourceLength;
    var trackedCaretLimit = CodeEditorPerformancePolicy.MaxTrackedCaretSourceLength;
    var liveAnalysisLimit = CodeEditorPerformancePolicy.MaxLiveCodeAnalysisLength;
    var historyLimit = CodeEditorPerformancePolicy.MaxHistorySnapshotSourceLength;

    Assert(
        CodeEditorPerformancePolicy.ShouldApplyFullSyntaxHighlighting(highlightedLimit),
        "Syntax highlighting should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldApplyFullSyntaxHighlighting(highlightedLimit + 1),
        "Syntax highlighting should stop beyond the boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldApplyLiveErrorHighlighting(highlightedLimit + 1),
        "Live error highlighting should not repaint large code documents.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldTrackCursorPosition(highlightedLimit + 1),
        "Cursor tracking should not scan large code documents.");
    Assert(
        CodeEditorPerformancePolicy.ShouldRunAutomaticCompletions(automaticCompletionLimit),
        "Automatic completions should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldRunAutomaticCompletions(automaticCompletionLimit + 1),
        "Automatic completions should stop before reading oversized documents.");
    Assert(
        CodeEditorPerformancePolicy.ShouldTrackLiveCaret(trackedCaretLimit),
        "Live caret tracking should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldTrackLiveCaret(trackedCaretLimit + 1),
        "Live caret tracking should stop beyond the boundary length.");
    Assert(
        CodeEditorPerformancePolicy.ShouldRunLiveAnalysis(liveAnalysisLimit),
        "Live analysis should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldRunLiveAnalysis(liveAnalysisLimit + 1),
        "Live analysis should stop beyond the boundary length.");
    Assert(
        CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(historyLimit),
        "Undo history should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldRecordHistorySnapshot(historyLimit + 1),
        "Undo history should stop copying oversized documents.");
    Assert(
        CodeEditorControl.ShouldScheduleAutomaticCompletion(automaticCompletionLimit),
        "Automatic completion timer should run at the configured boundary length.");
    Assert(
        !CodeEditorControl.ShouldScheduleAutomaticCompletion(automaticCompletionLimit + 1),
        "Automatic completion timer should not run for oversized documents.");
    Assert(
        CodeEditorControl.ShouldScheduleHistoryRecord(historyLimit),
        "History timer should run at the configured boundary length.");
    Assert(
        !CodeEditorControl.ShouldScheduleHistoryRecord(historyLimit + 1),
        "History timer should not run for oversized documents.");
}

static void MainMenuDragPositionClampsAndSkipsMicroMoves()
{
    var element = new MainMenuElement
    {
        Id = "start-button",
        X = 10,
        Y = 20,
        Width = 80,
        Height = 40,
    };

    var dragged = MainMenuEditorWindow.CalculateDraggedElementPosition(
        element,
        new Point(2, 3),
        new Point(element.X, element.Y),
        new Point(5, 8));

    Assert(Math.Abs(dragged.X - 13) < 0.001, "Main menu drag X was not calculated.");
    Assert(Math.Abs(dragged.Y - 25) < 0.001, "Main menu drag Y was not calculated.");
    Assert(
        MainMenuEditorWindow.HasMeaningfulPositionChange(element, dragged),
        "Visible main menu drag movement was skipped.");

    element.X = dragged.X;
    element.Y = dragged.Y;
    Assert(
        !MainMenuEditorWindow.HasMeaningfulPositionChange(
            element,
            new Point(element.X + 0.1, element.Y + 0.1)),
        "Micro main menu drag movement should not refresh the properties panel.");

    var clamped = MainMenuEditorWindow.CalculateDraggedElementPosition(
        element,
        new Point(0, 0),
        new Point(940, 530),
        new Point(200, 200));

    Assert(Math.Abs(clamped.X - 880) < 0.001, "Main menu drag X was not clamped.");
    Assert(Math.Abs(clamped.Y - 500) < 0.001, "Main menu drag Y was not clamped.");
}

static void SceneEditorTransformClampsAndSkipsMicroMoves()
{
    var moved = SceneEditorWindow.CalculateMovedCharacterPosition(
        10,
        20,
        new Point(2, 3),
        new Point(5, 8));

    Assert(Math.Abs(moved.X - 13) < 0.001, "Scene character drag X was not calculated.");
    Assert(Math.Abs(moved.Y - 25) < 0.001, "Scene character drag Y was not calculated.");

    var clamped = SceneEditorWindow.CalculateMovedCharacterPosition(
        1_910,
        1_070,
        new Point(0, 0),
        new Point(100, 100));

    Assert(
        Math.Abs(clamped.X - CharacterLayout.StageWidth) < 0.001,
        "Scene character drag X was not clamped.");
    Assert(
        Math.Abs(clamped.Y - CharacterLayout.StageHeight) < 0.001,
        "Scene character drag Y was not clamped.");

    var character = new CharacterPlacement
    {
        Id = "hero",
        HasCustomTransform = true,
        X = moved.X,
        Y = moved.Y,
        Scale = 1,
        Rotation = 0,
    };

    Assert(
        !SceneEditorWindow.HasMeaningfulTransformChange(
            character,
            character.X + 0.1,
            character.Y + 0.1,
            character.Scale + 0.0001,
            character.Rotation + 0.05),
        "Micro scene transform movement should not refresh the selected visual.");
    Assert(
        SceneEditorWindow.HasMeaningfulTransformChange(
            character,
            character.X + 1,
            character.Y,
            character.Scale,
            character.Rotation),
        "Visible scene transform movement was skipped.");
    Assert(
        SceneEditorWindow.HasMeaningfulTransformChange(
            character,
            character.X,
            character.Y,
            character.Scale + 0.01,
            character.Rotation),
        "Visible scene transform scale was skipped.");
    Assert(
        SceneEditorWindow.HasMeaningfulTransformChange(
            character,
            character.X,
            character.Y,
            character.Scale,
            character.Rotation + 0.2),
        "Visible scene transform rotation was skipped.");
}

static void GraphConnectionCurveBoundsIncludeControlPoints()
{
    var start = new Point(100, 40);
    var end = new Point(260, 140);
    var minimumBounds = GraphSurface.GetConnectionCurveBounds(start, end);

    AssertRectContains(minimumBounds, start, "Connection bounds did not include the start point.");
    AssertRectContains(minimumBounds, end, "Connection bounds did not include the end point.");
    AssertRectContains(
        minimumBounds,
        new Point(start.X + 80, start.Y),
        "Connection bounds did not include the minimum-distance source control point.");
    AssertRectContains(
        minimumBounds,
        new Point(end.X - 80, end.Y),
        "Connection bounds did not include the minimum-distance target control point.");

    var longEnd = new Point(700, -20);
    var distance = Math.Max(80, Math.Abs(longEnd.X - start.X) * 0.45);
    var longBounds = GraphSurface.GetConnectionCurveBounds(start, longEnd);

    AssertRectContains(
        longBounds,
        new Point(start.X + distance, start.Y),
        "Long connection bounds did not include the source control point.");
    AssertRectContains(
        longBounds,
        new Point(longEnd.X - distance, longEnd.Y),
        "Long connection bounds did not include the target control point.");
}

static void GraphWorldHitAreasIgnoreViewportOffset()
{
    var node = new NovelNode
    {
        Id = "dialogue",
        Kind = NodeKind.Dialogue,
        X = 120,
        Y = 80,
    };
    var output = new NodeOutput { Id = "next" };
    node.Outputs.Add(output);

    var bounds = GraphSurface.GetWorldNodeRectangle(node);
    var viewOffset = new Vector(320, -48);
    var worldPoint = new Point(bounds.Left + 12, bounds.Top + 16);
    var screenPoint = worldPoint + viewOffset;

    Assert(
        GraphSurface.ScreenToWorld(screenPoint, viewOffset) == worldPoint,
        "Graph screen point did not map back to world coordinates.");
    AssertRectContains(
        bounds,
        worldPoint,
        "Graph world node bounds did not include its world point.");

    var input = GraphSurface.GetWorldInputPortHitArea(node);
    var outputHitArea = GraphSurface.GetWorldOutputPortHitArea(node, output, 0);

    Assert(
        Math.Abs(input.Center.X - bounds.Left) < 0.001,
        "Graph input hit area was not anchored in world coordinates.");
    Assert(
        Math.Abs(outputHitArea.Center.X - bounds.Right) < 0.001,
        "Graph output hit area was not anchored in world coordinates.");
    AssertRectContains(input.HitArea, input.Center, "Graph input hit area missed its center.");
    AssertRectContains(
        outputHitArea.HitArea,
        outputHitArea.Center,
        "Graph output hit area missed its center.");
}

static void GraphHitTestCachePrefersTopmostNode()
{
    var bottom = new NovelNode { Id = "bottom", Kind = NodeKind.Scene };
    var top = new NovelNode { Id = "top", Kind = NodeKind.Dialogue };
    var cache = new GraphHitTestCache();
    cache.AddNode(new GraphNodeHitArea(bottom, new Rect(0, 0, 120, 80)));
    cache.AddNode(new GraphNodeHitArea(top, new Rect(20, 20, 120, 80)));

    var hit = cache.HitNode(new Point(30, 30));

    Assert(ReferenceEquals(hit, top), "Hit test cache did not prefer the topmost node.");
}

static void GraphHitTestCacheReturnsPorts()
{
    var inputNode = new NovelNode { Id = "dialogue", Kind = NodeKind.Dialogue };
    var cache = new GraphHitTestCache();
    cache.AddInputPort(
        new GraphInputPortHitArea(
            inputNode,
            new Point(10, 20),
            new Rect(0, 10, 20, 20)));
    cache.AddOutputPort(
        new GraphOutputPortHitArea(
            "scene",
            "next",
            new Point(100, 20),
            new Rect(90, 10, 20, 20)));

    var inputHit = cache.HitInputPort(new Point(8, 18));
    var outputHit = cache.HitOutputPort(new Point(98, 18));

    Assert(ReferenceEquals(inputHit, inputNode), "Hit test cache returned the wrong input node.");
    Assert(outputHit?.NodeId == "scene", "Hit test cache returned the wrong output node.");
    Assert(outputHit?.OutputId == "next", "Hit test cache returned the wrong output id.");
}

static void GraphHitTestCacheCrossesSpatialCells()
{
    var node = new NovelNode { Id = "boundary", Kind = NodeKind.Scene };
    var cache = new GraphHitTestCache();
    cache.AddNode(new GraphNodeHitArea(node, new Rect(250, 250, 120, 80)));
    cache.AddInputPort(
        new GraphInputPortHitArea(
            node,
            new Point(256, 280),
            new Rect(250, 274, 18, 18)));
    cache.AddOutputPort(
        new GraphOutputPortHitArea(
            node.Id,
            "next",
            new Point(370, 280),
            new Rect(364, 274, 18, 18)));

    Assert(ReferenceEquals(cache.HitNode(new Point(320, 290)), node), "Cross-cell node hit failed.");
    Assert(ReferenceEquals(cache.HitInputPort(new Point(260, 280)), node), "Cross-cell input hit failed.");
    Assert(cache.HitOutputPort(new Point(370, 280))?.NodeId == node.Id, "Cross-cell output hit failed.");
}

static void GraphHitTestCacheHandlesLargeGraphsQuickly()
{
    var cache = new GraphHitTestCache();
    const int count = 5_000;

    for (var index = 0; index < count; index++)
    {
        var x = index * 180;
        var y = 0;
        var node = new NovelNode { Id = $"node-{index}", Title = $"Node {index}", Kind = NodeKind.Dialogue };
        cache.AddNode(new GraphNodeHitArea(node, new Rect(x, y, 120, 72)));
        cache.AddInputPort(
            new GraphInputPortHitArea(
                node,
                new Point(x, y + 24),
                new Rect(x - 6, y + 18, 12, 12)));
        cache.AddOutputPort(
            new GraphOutputPortHitArea(
                node.Id,
                $"out-{index}",
                new Point(x + 120, y + 24),
                new Rect(x + 114, y + 18, 12, 12)));
    }

    var lastX = (count - 1) * 180;
    var lastY = 0;
    var nodePoint = new Point(lastX + 40, lastY + 30);
    var inputPoint = new Point(lastX, lastY + 24);
    var outputPoint = new Point(lastX + 120, lastY + 24);

    var stopwatch = Stopwatch.StartNew();
    for (var index = 0; index < 2_000; index++)
    {
        Assert(cache.HitNode(nodePoint)?.Id == "node-4999", "Large graph node hit returned the wrong node.");
        Assert(cache.HitInputPort(inputPoint)?.Id == "node-4999", "Large graph input hit returned the wrong node.");
        var outputHit = cache.HitOutputPort(outputPoint);
        Assert(outputHit?.NodeId == "node-4999", "Large graph output hit returned the wrong node.");
        Assert(outputHit?.OutputId == "out-4999", "Large graph output hit returned the wrong output id.");
    }

    stopwatch.Stop();
    Assert(
        stopwatch.ElapsedMilliseconds < 1_500,
        $"Large graph hit testing is too slow: {stopwatch.ElapsedMilliseconds}ms.");
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

static void AssertRectContains(Rect rect, Point point, string message)
{
    Assert(
        point.X >= rect.Left
            && point.X <= rect.Right
            && point.Y >= rect.Top
            && point.Y <= rect.Bottom,
        message);
}
