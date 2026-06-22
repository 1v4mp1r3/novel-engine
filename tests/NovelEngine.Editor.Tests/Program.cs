using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Input;
using NovelEngine.Core;
using NovelEngine.Editor;

var tests = new (string Name, Action Run)[]
{
    ("workspace project creates default folders", WorkspaceProjectCreatesDefaultFolders),
    ("workspace project path avoids existing file", WorkspaceProjectPathAvoidsExistingFile),
    ("workspace project can be resolved after creation", WorkspaceProjectCanBeResolvedAfterCreation),
    ("autosave policy skips unchanged saved projects", AutoSavePolicySkipsUnchangedSavedProjects),
    ("manual save policy skips unchanged saved projects", ManualSavePolicySkipsUnchangedSavedProjects),
    ("project resolver opens direct project file", ProjectResolverOpensDirectProjectFile),
    ("project resolver opens the only project in a folder", ProjectResolverOpensOnlyProject),
    ("project resolver prefers single novel project over generic json", ProjectResolverPrefersSingleNovelProjectOverGenericJson),
    ("project resolver opens the only json project in a folder", ProjectResolverOpensOnlyJsonProject),
    ("project resolver treats empty folder as workspace", ProjectResolverTreatsEmptyFolderAsWorkspace),
    ("project resolver prefers folder-named project", ProjectResolverPrefersFolderNamedProject),
    ("project resolver prefers folder-named json project", ProjectResolverPrefersFolderNamedJsonProject),
    ("project resolver prefers novel project over folder json", ProjectResolverPrefersNovelProjectOverFolderJson),
    ("project resolver treats ambiguous folder as workspace", ProjectResolverTreatsAmbiguousFolderAsWorkspace),
    ("project resolver expands quoted environment paths", ProjectResolverExpandsQuotedEnvironmentPaths),
    ("project resolver scans json projects once", ProjectResolverScansJsonProjectsOnce),
    ("autosave snapshot paths avoid same second collisions", AutoSaveSnapshotPathsAvoidSameSecondCollisions),
    ("autosave pruning keeps newest snapshots", AutoSavePruningKeepsNewestSnapshots),
    ("autosave pruning skips locked snapshots", AutoSavePruningSkipsLockedSnapshots),
    ("recent projects deduplicate and order entries", RecentProjectsDeduplicateAndOrderEntries),
    ("recent projects deduplicate normalized cache entries", RecentProjectsDeduplicateNormalizedCacheEntries),
    ("recent projects ignore corrupt cache", RecentProjectsIgnoreCorruptCache),
    ("recent projects ignore blank cache entries", RecentProjectsIgnoreBlankCacheEntries),
    ("recent projects normalize quoted cache paths", RecentProjectsNormalizeQuotedCachePaths),
    ("recent projects remember quoted paths", RecentProjectsRememberQuotedPaths),
    ("recent projects ignore missing paths", RecentProjectsIgnoreMissingPaths),
    ("recent projects keep workspace folders", RecentProjectsKeepWorkspaceFolders),
    ("recent projects keep only newest entries", RecentProjectsKeepOnlyNewestEntries),
    ("asset list filter searches within selected folder", AssetListFilterSearchesWithinSelectedFolder),
    ("asset list filter searches in one pass", AssetListFilterSearchesInOnePass),
    ("asset list stamp tracks visible input state", AssetListStampTracksVisibleInputState),
    ("asset folder tree stamp tracks visible input state", AssetFolderTreeStampTracksVisibleInputState),
    ("asset catalog runtime stamps avoid full scans", AssetCatalogRuntimeStampsAvoidFullScans),
    ("asset preview stamp tracks visible input state", AssetPreviewStampTracksVisibleInputState),
    ("asset preview playback stop skips inactive player", AssetPreviewPlaybackStopSkipsInactivePlayer),
    ("asset selection uses visible items source", AssetSelectionUsesVisibleItemsSource),
    ("dispatcher debounce gate collapses pending requests", DispatcherDebounceGateCollapsesPendingRequests),
    ("files watcher enables after event handlers", FilesWatcherEnablesAfterEventHandlers),
    ("search text changes use debounced refreshes", SearchTextChangesUseDebouncedRefreshes),
    ("asset file caches clear only after disk changes", AssetFileCachesClearOnlyAfterDiskChanges),
    ("asset size cache tracks file stamp", AssetSizeCacheTracksFileStamp),
    ("asset size cache skips repeated file info reads", AssetSizeCacheSkipsRepeatedFileInfoReads),
    ("asset binding refresh avoids duplicate dirty refreshes", AssetBindingRefreshAvoidsDuplicateDirtyRefreshes),
    ("asset bindings skip hidden graph refresh", AssetBindingsSkipHiddenGraphRefresh),
    ("character asset menus reuse filtered asset caches", CharacterAssetMenusReuseFilteredAssetCaches),
    ("selected node actions use graph selected node cache", SelectedNodeActionsUseGraphSelectedNodeCache),
    ("property selection uses row model references", PropertySelectionUsesRowModelReferences),
    ("property row lookup uses visible items source", PropertyRowLookupUsesVisibleItemsSource),
    ("transition edits refresh asset usage", TransitionEditsRefreshAssetUsage),
    ("editor asset mutations skip disk sync refreshes", EditorAssetMutationsSkipDiskSyncRefreshes),
    ("asset import refresh policy skips unchanged imports", AssetImportRefreshPolicySkipsUnchangedImports),
    ("asset import uses batch import", AssetImportUsesBatchImport),
    ("asset transition sound binding policy requires audio output", AssetTransitionSoundBindingPolicyRequiresAudioOutput),
    ("asset transition sound menu lists node outputs", AssetTransitionSoundMenuListsNodeOutputs),
    ("asset usage navigation uses node id directly", AssetUsageNavigationUsesNodeIdDirectly),
    ("preview asset resolution caches references", PreviewAssetResolutionCachesReferences),
    ("editor asset preview windows cache resolved references", EditorAssetPreviewWindowsCacheResolvedReferences),
    ("output editor copy is output neutral", OutputEditorCopyIsOutputNeutral),
    ("output detail editor policy accepts scene next outputs", OutputDetailEditorPolicyAcceptsSceneNextOutputs),
    ("output transition editor policy accepts scene next outputs", OutputTransitionEditorPolicyAcceptsSceneNextOutputs),
    ("output script block shortcut uses ctrl b", OutputScriptBlockShortcutUsesCtrlB),
    ("output transition keyboard shortcuts use ctrl combos", OutputTransitionKeyboardShortcutsUseCtrlCombos),
    ("output list shortcuts require dialogue nodes", OutputListShortcutsRequireDialogueNodes),
    ("output buttons hide choice actions for scene outputs", OutputButtonsHideChoiceActionsForSceneOutputs),
    ("output transition reset appears only for customized transitions", OutputTransitionResetAppearsOnlyForCustomizedTransitions),
    ("output transition change guard skips unchanged edits", OutputTransitionChangeGuardSkipsUnchangedEdits),
    ("graph editing shortcuts require graph focus", GraphEditingShortcutsRequireGraphFocus),
    ("diagnostic panel stamp tracks visible diagnostics", DiagnosticPanelStampTracksVisibleDiagnostics),
    ("app collection styles enable virtualization", AppCollectionStylesEnableVirtualization),
    ("bounded cache evicts least recently used entries", BoundedCacheEvictsLeastRecentlyUsedEntries),
    ("code editor performance policy limits expensive live work", CodeEditorPerformancePolicyLimitsExpensiveLiveWork),
    ("code cursor cache skips oversized line scans", CodeCursorCacheSkipsOversizedLineScans),
    ("code refresh skips unchanged rich text reset", CodeRefreshSkipsUnchangedRichTextReset),
    ("code editor plain text replace skips span work", CodeEditorPlainTextReplaceSkipsSpanWork),
    ("code highlighting skips span overflow repaint", CodeHighlightingSkipsSpanOverflowRepaint),
    ("visual script filter keeps preview cache", VisualScriptFilterKeepsPreviewCache),
    ("visual script filter uses debounced refresh", VisualScriptFilterUsesDebouncedRefresh),
    ("script literal load suppresses change events", ScriptLiteralLoadSuppressesChangeEvents),
    ("script block dialog guard skips unchanged apply", ScriptBlockDialogGuardSkipsUnchangedApply),
    ("node property guard skips unchanged apply", NodePropertyGuardSkipsUnchangedApply),
    ("node rendered property guard skips hidden graph refresh", NodeRenderedPropertyGuardSkipsHiddenGraphRefresh),
    ("node character editing policy respects inheritance", NodeCharacterEditingPolicyRespectsInheritance),
    ("node character actions skip hidden graph refresh", NodeCharacterActionsSkipHiddenGraphRefresh),
    ("node voice binding materializes inherited characters", NodeVoiceBindingMaterializesInheritedCharacters),
    ("node voice binding skips missing inherited character", NodeVoiceBindingSkipsMissingInheritedCharacter),
    ("node voice binding skips duplicate inherited voice", NodeVoiceBindingSkipsDuplicateInheritedVoice),
    ("output editor guard skips unchanged apply", OutputEditorGuardSkipsUnchangedApply),
    ("output rendered property guard skips hidden graph refresh", OutputRenderedPropertyGuardSkipsHiddenGraphRefresh),
    ("project explorer stamp tracks visible node fields", ProjectExplorerStampTracksVisibleNodeFields),
    ("project explorer groups filtered nodes once", ProjectExplorerGroupsFilteredNodesOnce),
    ("project explorer selection uses sync-only path", ProjectExplorerSelectionUsesSyncOnlyPath),
    ("project explorer selection skips unchanged sync", ProjectExplorerSelectionSkipsUnchangedSync),
    ("node property panel stamp tracks visible state", NodePropertyPanelStampTracksVisibleState),
    ("node property panel uses graph selected node cache", NodePropertyPanelUsesGraphSelectedNodeCache),
    ("node property panel uses cached target title lookup", NodePropertyPanelUsesCachedTargetTitleLookup),
    ("node asset picker cache invalidates property panel", NodeAssetPickerCacheInvalidatesPropertyPanel),
    ("main menu drag position clamps and skips micro moves", MainMenuDragPositionClampsAndSkipsMicroMoves),
    ("main menu property guard separates rendered changes", MainMenuPropertyGuardSeparatesRenderedChanges),
    ("main menu text properties use debounced apply", MainMenuTextPropertiesUseDebouncedApply),
    ("main menu property panel stamp tracks fields", MainMenuPropertyPanelStampTracksFields),
    ("main menu element list stamp tracks visible rows", MainMenuElementListStampTracksVisibleRows),
    ("main menu stage stamp tracks rendered state", MainMenuStageStampTracksRenderedState),
    ("scene editor transform clamps and skips micro moves", SceneEditorTransformClampsAndSkipsMicroMoves),
    ("scene editor character list stamp tracks visible rows", SceneEditorCharacterListStampTracksVisibleRows),
    ("modal editors skip hidden graph refresh", ModalEditorsSkipHiddenGraphRefresh),
    ("preview playback avoids transient lists", PreviewPlaybackAvoidsTransientLists),
    ("game process start is registered before launch", GameProcessStartIsRegisteredBeforeLaunch),
    ("silent game process stop disposes immediately", SilentGameProcessStopDisposesImmediately),
    ("main window closing stops editor timers", MainWindowClosingStopsEditorTimers),
    ("graph surface shortcuts use exact modifiers", GraphSurfaceShortcutsUseExactModifiers),
    ("graph drag movement skips micro deltas", GraphDragMovementSkipsMicroDeltas),
    ("graph node drag defers hit cache rebuild", GraphNodeDragDefersHitCacheRebuild),
    ("graph surface uses cached node lookup", GraphSurfaceUsesCachedNodeLookup),
    ("graph centering calculates bounds in one pass", GraphCenteringCalculatesBoundsInOnePass),
    ("graph inheritance menu caches incoming nodes", GraphInheritanceMenuCachesIncomingNodes),
    ("graph inheritance menu skips unchanged apply", GraphInheritanceMenuSkipsUnchangedApply),
    ("graph inheritance render skips selected node", GraphInheritanceRenderSkipsSelectedNode),
    ("dirty change graph refresh policy skips graph-originated changes", DirtyChangeGraphRefreshPolicySkipsGraphOriginatedChanges),
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

static void AutoSavePolicySkipsUnchangedSavedProjects()
{
    Assert(
        !MainWindow.ShouldRunAutoSave(
            hasProjectPath: true,
            hasWorkspaceDirectory: true,
            dirty: false,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: false),
        "Autosave should skip unchanged saved projects.");
    Assert(
        MainWindow.ShouldRunAutoSave(
            hasProjectPath: true,
            hasWorkspaceDirectory: true,
            dirty: true,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: false),
        "Autosave should run for dirty saved projects.");
    Assert(
        MainWindow.ShouldRunAutoSave(
            hasProjectPath: true,
            hasWorkspaceDirectory: true,
            dirty: false,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: true),
        "Autosave should preserve pending code changes.");
    Assert(
        MainWindow.ShouldRunAutoSave(
            hasProjectPath: false,
            hasWorkspaceDirectory: true,
            dirty: false,
            workspaceNeedsProjectFile: true,
            codeHasPendingChanges: false),
        "Autosave should create missing workspace project files.");
    Assert(
        !MainWindow.ShouldRunAutoSave(
            hasProjectPath: false,
            hasWorkspaceDirectory: false,
            dirty: true,
            workspaceNeedsProjectFile: true,
            codeHasPendingChanges: false),
        "Autosave should skip unsaved projects without a workspace directory.");
}

static void ManualSavePolicySkipsUnchangedSavedProjects()
{
    Assert(
        !MainWindow.ShouldRunManualSave(
            hasProjectPath: true,
            dirty: false,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: false),
        "Manual save should skip unchanged saved projects.");
    Assert(
        MainWindow.ShouldRunManualSave(
            hasProjectPath: true,
            dirty: true,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: false),
        "Manual save should run for dirty saved projects.");
    Assert(
        MainWindow.ShouldRunManualSave(
            hasProjectPath: true,
            dirty: false,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: true),
        "Manual save should apply pending code changes.");
    Assert(
        MainWindow.ShouldRunManualSave(
            hasProjectPath: false,
            dirty: false,
            workspaceNeedsProjectFile: false,
            codeHasPendingChanges: false),
        "Manual save should prompt for unsaved project locations.");
    Assert(
        MainWindow.ShouldRunManualSave(
            hasProjectPath: true,
            dirty: false,
            workspaceNeedsProjectFile: true,
            codeHasPendingChanges: false),
        "Manual save should create missing workspace project files.");
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

static void ProjectResolverPrefersSingleNovelProjectOverGenericJson()
{
    var directory = CreateTempDirectory();
    try
    {
        var projectPath = Path.Combine(directory, "custom-name.novel.json");
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(Path.Combine(directory, "tool-config.json"), "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(
            result.ProjectPath == projectPath,
            "Resolver did not prefer the single .novel.json project over generic JSON.");
        Assert(result.WorkspaceDirectory == directory, "Resolver changed mixed-json workspace directory.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a folder with one .novel.json project as empty.");
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

static void ProjectResolverPrefersNovelProjectOverFolderJson()
{
    var directory = CreateTempDirectory();
    try
    {
        var preferredNovelPath = Path.Combine(
            directory,
            $"{Path.GetFileName(directory)}.novel.json");
        var preferredJsonPath = Path.Combine(
            directory,
            $"{Path.GetFileName(directory)}.json");
        File.WriteAllText(preferredJsonPath, "{}");
        File.WriteAllText(preferredNovelPath, "{}");

        var result = ProjectOpenResolver.Resolve(directory);

        Assert(
            result.ProjectPath == preferredNovelPath,
            "Resolver should prefer folder-named .novel.json over folder-named .json.");
        Assert(!result.CreatedEmptyWorkspace, "Resolver marked a preferred novel project folder as empty.");
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

static void ProjectResolverScansJsonProjectsOnce()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "ProjectOpenResolver.cs"));
    var resolveBody = ExtractMethodBody(
        source,
        "private static ProjectOpenResult ResolveProjectFromDirectory");
    var addFilesBody = ExtractMethodBody(
        source,
        "private static string? AddJsonProjectFiles");

    Assert(
        resolveBody.Contains(
            "var preferredProject = AddJsonProjectFiles(",
            StringComparison.Ordinal)
            && !resolveBody.Contains(".Concat(", StringComparison.Ordinal)
            && !resolveBody.Contains(".Distinct(", StringComparison.Ordinal)
            && !resolveBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Project folder resolution should delegate to one explicit JSON file scan.");
    Assert(
        Regex.Matches(addFilesBody, Regex.Escape("Directory.EnumerateFiles(")).Count == 1
            && addFilesBody.Contains("\"*.json\"", StringComparison.Ordinal)
            && addFilesBody.Contains("preferredNovelProject", StringComparison.Ordinal)
            && addFilesBody.Contains("preferredJsonProject", StringComparison.Ordinal)
            && addFilesBody.Contains("IsNovelProjectFile(path)", StringComparison.Ordinal)
            && addFilesBody.Contains("return preferredNovelProject ?? preferredJsonProject;", StringComparison.Ordinal)
            && !addFilesBody.Contains(".Sort(", StringComparison.Ordinal)
            && !addFilesBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !addFilesBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Project resolver should enumerate JSON files once and detect preferred files without sorting.");
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

static void RecentProjectsDeduplicateNormalizedCacheEntries()
{
    var directory = CreateTempDirectory();
    try
    {
        var storePath = Path.Combine(directory, "recent.json");
        var projectPath = Path.Combine(directory, "duplicated.novel.json");
        File.WriteAllText(projectPath, "{}");
        File.WriteAllText(
            storePath,
            $$"""
            [
              {
                "Path": "{{projectPath.Replace("\\", "\\\\")}}",
                "DisplayName": "Old duplicate",
                "LastOpenedUtc": "2026-06-17T10:00:00Z"
              },
              {
                "Path": "  \"{{projectPath.Replace("\\", "\\\\").Replace("\"", "\\\"")}}\"  ",
                "DisplayName": "Newest duplicate",
                "LastOpenedUtc": "2026-06-17T11:00:00Z"
              }
            ]
            """);

        WithRecentProjectsStore(storePath, () =>
        {
            var entries = RecentProjectsStore.Load();

            Assert(entries.Count == 1, "Recent projects kept normalized duplicate cache entries.");
            Assert(entries[0].Path == projectPath, "Recent projects did not normalize the duplicate path.");
            Assert(
                entries[0].DisplayName == "Newest duplicate",
                "Recent projects did not keep the newest duplicate entry.");
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

    var allFoldersResult = AssetListFilter.Apply(assets, null, "voice");
    Assert(allFoldersResult.FolderAssetCount == 3, "Asset search across all folders should count all assets.");
    Assert(allFoldersResult.Assets.Count == 1, "Asset search across all folders returned the wrong count.");
    Assert(allFoldersResult.Assets[0].Id == "hero_voice", "Asset search across all folders returned the wrong asset.");
}

static void AssetListFilterSearchesInOnePass()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "AssetListFilter.cs"));
    var applyBody = ExtractMethodBody(source, "public static AssetListFilterResult Apply");
    var matchesBody = ExtractMethodBody(source, "private static bool MatchesSearch");

    Assert(
        applyBody.Contains("var tokens = SplitSearchQuery(normalizedQuery);", StringComparison.Ordinal),
        "Asset list filtering should tokenize the query once before scanning assets.");
    Assert(
        !matchesBody.Contains(".Split(", StringComparison.Ordinal),
        "Asset search matching should not split the query per asset.");
    Assert(
        matchesBody.Contains("var fileName = Path.GetFileName(asset.Path);", StringComparison.Ordinal)
            && matchesBody.Contains("var kindName = asset.Kind.ToString();", StringComparison.Ordinal)
            && matchesBody.Contains("for (var index = 0; index < tokens.Count; index++)", StringComparison.Ordinal)
            && !matchesBody.Contains(".All(", StringComparison.Ordinal),
        "Asset search matching should check tokens directly without LINQ delegates.");
    Assert(
        !applyBody.Contains("filteredByFolder", StringComparison.Ordinal)
            && !applyBody.Contains(".Where(", StringComparison.Ordinal)
            && !applyBody.Contains(".ToList()", StringComparison.Ordinal),
        "Asset list filtering should avoid separate folder and search passes.");
}

static void AssetListStampTracksVisibleInputState()
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
            Id = "hero_voice",
            Kind = AssetKind.Audio,
            Folder = "voices",
            Path = "files/voices/hero.wav",
        },
    };
    var baseline = MainWindow.CreateAssetListStamp(
        assets,
        " backgrounds ",
        " fuji ");

    Assert(
        baseline == MainWindow.CreateAssetListStamp(assets, "backgrounds", "fuji"),
        "Asset list stamp should normalize folder and search query.");
    Assert(
        baseline != MainWindow.CreateAssetListStamp(assets, "voices", "fuji"),
        "Asset list stamp should track the selected folder.");
    Assert(
        baseline != MainWindow.CreateAssetListStamp(assets, "backgrounds", "voice"),
        "Asset list stamp should track the search query.");

    assets[0].Path = "files/backgrounds/MountFujiLarge.jpg";
    Assert(
        baseline != MainWindow.CreateAssetListStamp(assets, "backgrounds", "fuji"),
        "Asset list stamp should change when asset metadata changes.");
}

static void AssetFolderTreeStampTracksVisibleInputState()
{
    var folders = new[] { "backgrounds", "voices" };
    var assets = new List<NovelAsset>
    {
        new()
        {
            Id = "fuji",
            Kind = AssetKind.Image,
            Folder = "backgrounds",
            Path = "files/backgrounds/fuji.jpg",
        },
        new()
        {
            Id = "voice_a",
            Kind = AssetKind.Audio,
            Folder = "voices",
            Path = "files/voices/voice_a.wav",
        },
    };
    var baseline = MainWindow.CreateAssetFolderTreeStamp(
        folders,
        assets,
        " backgrounds ");

    Assert(
        baseline == MainWindow.CreateAssetFolderTreeStamp(folders, assets, "backgrounds"),
        "Asset folder tree stamp should normalize selected folders.");
    Assert(
        baseline != MainWindow.CreateAssetFolderTreeStamp(folders, assets, "voices"),
        "Asset folder tree stamp should track selected folders.");

    assets[0].Path = "files/backgrounds/renamed.jpg";
    Assert(
        baseline == MainWindow.CreateAssetFolderTreeStamp(folders, assets, "backgrounds"),
        "Asset folder tree stamp should ignore non-visible asset metadata.");

    assets.Add(new NovelAsset
    {
        Id = "lake",
        Kind = AssetKind.Image,
        Folder = "backgrounds",
        Path = "files/backgrounds/lake.jpg",
    });
    Assert(
        baseline != MainWindow.CreateAssetFolderTreeStamp(folders, assets, "backgrounds"),
        "Asset folder tree stamp should track folder counts.");
    Assert(
        baseline != MainWindow.CreateAssetFolderTreeStamp(
            folders.Append("characters"),
            assets,
            "backgrounds"),
        "Asset folder tree stamp should track empty folder rows.");
}

static void AssetCatalogRuntimeStampsAvoidFullScans()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);
    var listBody = ExtractMethodBody(source, "private void RefreshAssetList");
    var folderBody = ExtractMethodBody(source, "private void RefreshAssetFolders");
    var folderStampBody = ExtractMethodBody(
        normalizedSource,
        "internal static AssetFolderTreeStamp CreateAssetFolderTreeStamp(\n        IEnumerable<string> folders");
    var countFoldersBody = ExtractMethodBody(
        source,
        "private static IReadOnlyDictionary<string, int> CountAssetsByFolder");
    var addFolderTreePathBody = ExtractMethodBody(
        source,
        "private static void AddAssetFolderTreePath");
    var clearPickerBody = ExtractMethodBody(source, "private void ClearNodeAssetPickerCaches");
    var listStampIndex = listBody.IndexOf(
        "var stamp = CreateAssetListStamp(",
        StringComparison.Ordinal);
    var listFilterIndex = listBody.IndexOf(
        "AssetListFilter.Apply(",
        StringComparison.Ordinal);
    var folderStampIndex = folderBody.IndexOf(
        "var stamp = CreateAssetFolderTreeStamp(",
        StringComparison.Ordinal);
    var folderCountIndex = folderBody.IndexOf(
        "var folderCounts = CountAssetsByFolder(_project.Assets);",
        StringComparison.Ordinal);

    Assert(
        listStampIndex >= 0 && listFilterIndex > listStampIndex,
        "Asset list should check a cheap stamp before filtering assets.");
    Assert(
        folderStampIndex >= 0 && folderCountIndex > folderStampIndex,
        "Asset folders should check a cheap stamp before counting folders.");
    Assert(
        listBody.Contains("_project.Assets.Count", StringComparison.Ordinal)
            && listBody.Contains("_assetCatalogRevision", StringComparison.Ordinal),
        "Asset list runtime stamp should use catalog counts and revision.");
    Assert(
        listBody.Contains("foreach (var asset in filtered.Assets)", StringComparison.Ordinal)
            && listBody.Contains("var view = new AssetView(", StringComparison.Ordinal)
            && listBody.Contains("selectedView = view;", StringComparison.Ordinal)
            && listBody.Contains("AssetsGrid.SelectedItem = selectedView;", StringComparison.Ordinal)
            && !listBody.Contains(".Select(", StringComparison.Ordinal)
            && !listBody.Contains(".ToList(", StringComparison.Ordinal)
            && !listBody.Contains(".FirstOrDefault(", StringComparison.Ordinal)
            && !listBody.Contains("AssetsGrid.Items\r\n                .OfType<AssetView>()", StringComparison.Ordinal)
            && !listBody.Contains("AssetsGrid.Items\n                .OfType<AssetView>()", StringComparison.Ordinal),
        "Asset list should build view models and restore selection in one direct pass.");
    Assert(
        folderBody.Contains("_project.AssetFolders.Count", StringComparison.Ordinal)
            && folderBody.Contains("_project.Assets.Count", StringComparison.Ordinal)
            && folderBody.Contains("_assetCatalogRevision", StringComparison.Ordinal),
        "Asset folder runtime stamp should use catalog counts and revision.");
    Assert(
        folderBody.Contains("sortedFolders.Sort(StringComparer.CurrentCultureIgnoreCase);", StringComparison.Ordinal)
            && folderBody.Contains("AddAssetFolderTreePath(", StringComparison.Ordinal)
            && !folderBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !folderBody.Contains(".Split(", StringComparison.Ordinal),
        "Asset folder refresh should sort directly and avoid per-folder split arrays.");
    Assert(
        folderStampBody.Contains("foreach (var folder in folders)", StringComparison.Ordinal)
            && folderStampBody.Contains("foreach (var asset in assets)", StringComparison.Ordinal)
            && folderStampBody.Contains("normalizedFolders.Sort(StringComparer.OrdinalIgnoreCase);", StringComparison.Ordinal)
            && !folderStampBody.Contains(".Select(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".Where(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".Distinct(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".GroupBy(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".ToDictionary(", StringComparison.Ordinal)
            && !folderStampBody.Contains(".ToArray(", StringComparison.Ordinal),
        "Asset folder tree stamps should avoid LINQ pipelines while hashing visible folder state.");
    Assert(
        addFolderTreePathBody.Contains("folder.IndexOf('/', segmentStart)", StringComparison.Ordinal)
            && addFolderTreePathBody.Contains("while (segmentStart < folder.Length)", StringComparison.Ordinal)
            && !addFolderTreePathBody.Contains(".Split(", StringComparison.Ordinal),
        "Asset folder tree paths should be walked without allocating split arrays.");
    Assert(
        countFoldersBody.Contains("foreach (var asset in assets)", StringComparison.Ordinal)
            && countFoldersBody.Contains("folderCounts[folder] = folderCounts.GetValueOrDefault(folder) + 1;", StringComparison.Ordinal)
            && !countFoldersBody.Contains(".GroupBy(", StringComparison.Ordinal)
            && !countFoldersBody.Contains(".ToDictionary(", StringComparison.Ordinal),
        "Asset folder counts should be accumulated in one pass without LINQ grouping.");
    Assert(
        clearPickerBody.Contains("_assetCatalogRevision++", StringComparison.Ordinal),
        "Asset catalog invalidation should advance runtime stamp revision.");
}

static void AssetPreviewStampTracksVisibleInputState()
{
    var asset = new NovelAsset
    {
        Id = "fuji",
        Kind = AssetKind.Image,
        Folder = "backgrounds",
        Path = "files/backgrounds/fuji.jpg",
    };
    var baseline = MainWindow.CreateAssetPreviewStamp(asset, usageCount: 2);

    Assert(
        baseline == MainWindow.CreateAssetPreviewStamp(asset, usageCount: 2),
        "Asset preview stamp should remain stable for identical preview state.");

    asset.Folder = "renamed_backgrounds";
    Assert(
        baseline == MainWindow.CreateAssetPreviewStamp(asset, usageCount: 2),
        "Asset preview stamp should ignore fields not shown in the preview panel.");

    asset.Path = "files/backgrounds/fuji-large.jpg";
    Assert(
        baseline != MainWindow.CreateAssetPreviewStamp(asset, usageCount: 2),
        "Asset preview stamp should track preview paths.");
    Assert(
        baseline != MainWindow.CreateAssetPreviewStamp(asset, usageCount: 3),
        "Asset preview stamp should track usage counts.");
    Assert(
        MainWindow.CreateAssetPreviewStamp(null, usageCount: 0)
        == MainWindow.CreateAssetPreviewStamp(null, usageCount: 3),
        "Empty asset preview stamp should ignore usage counts.");
}

static void AssetPreviewPlaybackStopSkipsInactivePlayer()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var stopBody = ExtractMethodBody(source, "private void StopAssetPreviewPlayback");
    var stopIndex = stopBody.IndexOf("_assetPreviewPlayer.Stop();", StringComparison.Ordinal);
    var inactiveGuardIndex = stopBody.IndexOf(
        "if (IsInitialized && !AssetPreviewStopButton.IsEnabled)",
        StringComparison.Ordinal);

    Assert(
        inactiveGuardIndex >= 0 && inactiveGuardIndex < stopIndex,
        "Inactive asset preview audio should skip MediaPlayer.Stop().");
}

static void AssetSelectionUsesVisibleItemsSource()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var helperBody = ExtractMethodBody(source, "private AssetView? FindVisibleAssetView");
    var createVoiceBody = ExtractMethodBody(source, "private void CreateVoiceBlip_Click");
    var renameBody = ExtractMethodBody(source, "private void RenameAsset_Click");
    var diagnosticBody = ExtractMethodBody(source, "private bool TryNavigateToDiagnosticAsset");

    Assert(
        helperBody.Contains("AssetsGrid.ItemsSource is IEnumerable<AssetView>", StringComparison.Ordinal),
        "Visible asset lookup should use the current items source.");
    Assert(
        !source.Contains("AssetsGrid.Items\r\n            .OfType<AssetView>()", StringComparison.Ordinal)
            && !source.Contains("AssetsGrid.Items\n            .OfType<AssetView>()", StringComparison.Ordinal)
            && !source.Contains("AssetsGrid.Items\r\n                .OfType<AssetView>()", StringComparison.Ordinal)
            && !source.Contains("AssetsGrid.Items\n                .OfType<AssetView>()", StringComparison.Ordinal),
        "Asset selection should not walk DataGrid item containers.");
    Assert(
        createVoiceBody.Contains("FindVisibleAssetView(asset.Id)", StringComparison.Ordinal)
            && renameBody.Contains("FindVisibleAssetView(dialog.AssetId)", StringComparison.Ordinal)
            && diagnosticBody.Contains("FindVisibleAssetView(assetId)", StringComparison.Ordinal),
        "Asset commands should reuse visible asset lookup.");
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

static void FilesWatcherEnablesAfterEventHandlers()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private void ConfigureFilesWatcher");
    var createdIndex = body.IndexOf("_filesWatcher.Created +=", StringComparison.Ordinal);
    var renamedIndex = body.IndexOf("_filesWatcher.Renamed +=", StringComparison.Ordinal);
    var changedIndex = body.IndexOf("_filesWatcher.Changed +=", StringComparison.Ordinal);
    var deletedIndex = body.IndexOf("_filesWatcher.Deleted +=", StringComparison.Ordinal);
    var enableIndex = body.IndexOf("_filesWatcher.EnableRaisingEvents = true;", StringComparison.Ordinal);

    Assert(
        createdIndex >= 0
            && renamedIndex > createdIndex
            && changedIndex > renamedIndex
            && deletedIndex > changedIndex
            && enableIndex > deletedIndex,
        "File watcher should subscribe handlers before enabling filesystem events.");
}

static void SearchTextChangesUseDebouncedRefreshes()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var projectTextChanged = ExtractMethodBody(
        source,
        "private void ProjectSearchBox_TextChanged");
    var projectKeyDown = ExtractMethodBody(
        source,
        "private void ProjectSearchBox_KeyDown");
    var assetTextChanged = ExtractMethodBody(
        source,
        "private void AssetSearchBox_TextChanged");
    var assetKeyDown = ExtractMethodBody(
        source,
        "private void AssetSearchBox_KeyDown");

    Assert(
        projectTextChanged.Contains(
            "ScheduleProjectExplorerRefresh();",
            StringComparison.Ordinal),
        "Project search text changes should schedule a debounced refresh.");
    Assert(
        !projectTextChanged.Contains("RefreshExplorer();", StringComparison.Ordinal),
        "Project search text changes should not refresh immediately.");
    Assert(
        assetTextChanged.Contains(
            "ScheduleAssetListRefresh();",
            StringComparison.Ordinal),
        "Asset search text changes should schedule a debounced refresh.");
    Assert(
        !assetTextChanged.Contains("RefreshAssetList();", StringComparison.Ordinal),
        "Asset search text changes should not refresh immediately.");
    Assert(
        projectKeyDown.Contains(
            "_projectExplorerSearchTimer.Stop();",
            StringComparison.Ordinal)
            && projectKeyDown.Contains("RefreshExplorer();", StringComparison.Ordinal),
        "Project search Enter should flush the debounced refresh immediately.");
    Assert(
        assetKeyDown.Contains("_assetSearchTimer.Stop();", StringComparison.Ordinal)
            && assetKeyDown.Contains("RefreshAssetList();", StringComparison.Ordinal),
        "Asset search Escape should flush the debounced refresh immediately.");
}

static void AssetFileCachesClearOnlyAfterDiskChanges()
{
    Assert(
        !MainWindow.ShouldClearAssetFileCaches(
            externalFileEvent: false,
            diskSyncChangedProject: false),
        "Plain asset refresh should keep file caches warm.");
    Assert(
        MainWindow.ShouldClearAssetFileCaches(
            externalFileEvent: true,
            diskSyncChangedProject: false),
        "External file events should clear file caches.");
    Assert(
        MainWindow.ShouldClearAssetFileCaches(
            externalFileEvent: false,
            diskSyncChangedProject: true),
        "Disk sync project changes should clear file caches.");
}

static void AssetSizeCacheTracksFileStamp()
{
    var stamp = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
    var cached = new AssetSizeCacheEntry(1024, stamp, "1 КБ");

    Assert(
        MainWindow.ShouldReuseAssetSizeCache(cached, 1024, stamp),
        "Asset size cache should be reused for the same file stamp.");
    Assert(
        !MainWindow.ShouldReuseAssetSizeCache(cached, 2048, stamp),
        "Asset size cache should be refreshed when the file length changes.");
    Assert(
        !MainWindow.ShouldReuseAssetSizeCache(
            cached,
            1024,
            stamp.AddSeconds(1)),
        "Asset size cache should be refreshed when the file timestamp changes.");
}

static void AssetSizeCacheSkipsRepeatedFileInfoReads()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private string AssetSize");
    var cacheIndex = body.IndexOf(
        "_assetSizeCache.TryGetValue(path, out var cachedSize)",
        StringComparison.Ordinal);
    var fileInfoIndex = body.IndexOf("new FileInfo(path)", StringComparison.Ordinal);

    Assert(cacheIndex >= 0, "Asset size should check the cached label for known paths.");
    Assert(fileInfoIndex >= 0, "Asset size should still read file info for uncached paths.");
    Assert(
        cacheIndex < fileInfoIndex,
        "Asset size should return cached labels before touching the filesystem.");
}

static void AssetBindingRefreshAvoidsDuplicateDirtyRefreshes()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private void RefreshAssetUsageAfterBinding");

    Assert(
        body.Contains("_assetUsageCountCache = null;", StringComparison.Ordinal)
            && body.Contains("RefreshAssetList();", StringComparison.Ordinal),
        "Asset binding should refresh asset usage state through the asset list.");
    Assert(
        !body.Contains("RefreshAssets(", StringComparison.Ordinal),
        "Asset binding should not rebuild folders or sync files when only usage counts changed.");
    Assert(
        !body.Contains("Graph.RefreshGraph(", StringComparison.Ordinal),
        "Asset binding post-refresh should not duplicate MarkDirty graph refresh.");
    Assert(
        !body.Contains("RefreshProperties(", StringComparison.Ordinal),
        "Asset binding post-refresh should not duplicate MarkDirty property refresh.");
    Assert(
        !body.Contains("RequestCodeRefresh(", StringComparison.Ordinal),
        "Asset binding post-refresh should not duplicate MarkDirty code refresh.");
    Assert(
        !body.Contains("RequestDiagnosticsRefresh(", StringComparison.Ordinal),
        "Asset binding post-refresh should not duplicate MarkDirty diagnostics refresh.");
}

static void AssetBindingsSkipHiddenGraphRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var hiddenBindingMethods = new[]
    {
        "private void BindAssetAsNodeBackground",
        "private void BindAssetAsNodeMusic",
        "private void CreateLibraryCharacterFromSprite",
        "private void AddCharacterFromSpriteToSelectedNode",
        "private void BindVoiceAssetToCharacter",
        "private void BindVoiceAssetToLibraryCharacter",
    };

    foreach (var method in hiddenBindingMethods)
    {
        var body = ExtractMethodBody(source, method);
        Assert(
            body.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
            $"{method} should dirty the project without refreshing the graph.");
    }
}

static void CharacterAssetMenusReuseFilteredAssetCaches()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var assetFolderBody = ExtractMethodBody(
        source,
        "private IReadOnlyList<NovelAsset> GetAssetsInFolder");
    var characterAssetMenuBody = ExtractMethodBody(
        source,
        "private MenuItem CreateCharacterAssetVoiceMenu");
    var voiceBindingMenuBody = ExtractMethodBody(
        source,
        "private MenuItem CreateVoiceBindingMenu");
    var libraryVoiceBindingMenuBody = ExtractMethodBody(
        source,
        "private MenuItem CreateLibraryVoiceBindingMenu");
    var effectiveCharactersBody = ExtractMethodBody(
        source,
        "private IReadOnlyList<CharacterPlacement> GetEffectiveCharacters");
    var clearBody = ExtractMethodBody(source, "private void ClearNodeAssetPickerCaches");

    Assert(
        source.Contains("_characterSpriteAssetsCache ??=", StringComparison.Ordinal)
            && source.Contains(
                "GetAssetsInFolder(AssetKind.Image, \"characters\", sortById: false)",
                StringComparison.Ordinal),
        "Character sprite asset list should reuse a cached filtered list.");
    Assert(
        source.Contains("_voiceBlipAssetsCache ??=", StringComparison.Ordinal)
            && source.Contains(
                "GetAssetsInFolder(AssetKind.Audio, \"voices\", sortById: false)",
                StringComparison.Ordinal)
            && source.Contains("_sortedVoiceBlipAssetsCache ??=", StringComparison.Ordinal)
            && source.Contains(
                "GetAssetsInFolder(AssetKind.Audio, \"voices\", sortById: true)",
                StringComparison.Ordinal),
        "Voice asset lists should reuse cached filtered lists.");
    Assert(
        clearBody.Contains("_characterSpriteAssetsCache = null;", StringComparison.Ordinal)
            && clearBody.Contains("_voiceBlipAssetsCache = null;", StringComparison.Ordinal)
            && clearBody.Contains("_sortedVoiceBlipAssetsCache = null;", StringComparison.Ordinal),
        "Asset catalog invalidation should clear character and voice asset caches.");
    Assert(
        assetFolderBody.Contains("foreach (var asset in _project.Assets)", StringComparison.Ordinal)
            && assetFolderBody.Contains("IsInAssetFolder(asset, folder)", StringComparison.Ordinal)
            && assetFolderBody.Contains("assets.Sort(", StringComparison.Ordinal)
            && !assetFolderBody.Contains(".Where(", StringComparison.Ordinal)
            && !assetFolderBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !assetFolderBody.Contains(".ToList(", StringComparison.Ordinal),
        "Character and voice asset caches should be built with a direct filter pass.");
    Assert(
        characterAssetMenuBody.Contains("var voices = GetSortedVoiceBlipAssets();", StringComparison.Ordinal)
            && !characterAssetMenuBody.Contains("_project.Assets", StringComparison.Ordinal)
            && !characterAssetMenuBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !characterAssetMenuBody.Contains(".ToList(", StringComparison.Ordinal),
        "Character sprite context menu should reuse sorted voice asset cache.");
    Assert(
        voiceBindingMenuBody.Contains("var characters = GetEffectiveCharacters(node);", StringComparison.Ordinal)
            && voiceBindingMenuBody.Contains("sortedCharacters.Sort(CompareCharactersByName)", StringComparison.Ordinal)
            && !voiceBindingMenuBody.Contains(".ToList(", StringComparison.Ordinal)
            && !voiceBindingMenuBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Voice binding menu should avoid LINQ allocations for effective characters.");
    Assert(
        libraryVoiceBindingMenuBody.Contains("sortedCharacters.Sort(CompareCharactersByLabel)", StringComparison.Ordinal)
            && !libraryVoiceBindingMenuBody.Contains(".OrderBy(", StringComparison.Ordinal),
        "Library voice binding menu should sort characters without LINQ.");
    Assert(
        effectiveCharactersBody.Contains("foreach (var character in player.State.CurrentCharacters)", StringComparison.Ordinal)
            && !effectiveCharactersBody.Contains(".Select(", StringComparison.Ordinal)
            && !effectiveCharactersBody.Contains(".ToList(", StringComparison.Ordinal),
        "Inherited effective characters should be cloned without LINQ pipelines.");
}

static void SelectedNodeActionsUseGraphSelectedNodeCache()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var selectedNodeMethods = new[]
    {
        "private void EditNodeScriptBlocks_Click",
        "internal bool BindVoiceAssetToCharacterForSmoke",
        "private bool RebuildAssetsContextMenu",
        "private void BindAssetAsNodeBackground",
        "private void BindAssetAsNodeMusic",
        "private void BindAssetAsOutputTransitionSound",
        "private void AddCharacterFromSpriteToSelectedNode",
        "private void BindVoiceAssetToCharacter",
        "private void AddCharacter_Click",
        "private void AddLibraryCharacter_Click",
        "private void SaveCharacterToLibrary_Click",
        "private void EditCharacter_Click",
        "private void DeleteCharacter_Click",
        "private void DuplicateCharacter_Click",
        "private void MoveSelectedCharacter",
        "private void SetSelectedCharacterPosition",
        "private void CharactersGrid_ContextMenuOpening",
        "private void CharactersGrid_PreviewKeyDown",
        "private void AddOutput(string? nodeId = null)",
        "private void DeleteOutput_Click",
        "private void DuplicateOutput_Click",
        "private void MoveSelectedOutput",
        "private void OutputsGrid_ContextMenuOpening",
        "private void OutputsGrid_PreviewKeyDown",
        "private void EditSelectedOutputScriptBlocks()",
        "private void EditSelectedOutputTransition()",
        "private void Inheritance_Changed",
    };

    foreach (var method in selectedNodeMethods)
    {
        var body = ExtractMethodBody(source, method);
        Assert(
            body.Contains("Graph.SelectedNode", StringComparison.Ordinal),
            $"{method} should use GraphSurface selected-node cache.");
        Assert(
            !body.Contains("_project.FindNode(Graph.SelectedNodeId)", StringComparison.Ordinal),
            $"{method} should not linearly search the selected project node.");
    }
}

static void PropertySelectionUsesRowModelReferences()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var selectedCharacterBody = ExtractMethodBody(
        source,
        "private CharacterPlacement? SelectedCharacter");
    var selectedOutputBody = ExtractMethodBody(
        source,
        "private NodeOutput? SelectedOutput");

    Assert(
        selectedCharacterBody.Contains("?.Character", StringComparison.Ordinal),
        "SelectedCharacter should return the row's model reference directly.");
    Assert(
        selectedOutputBody.Contains("?.Output", StringComparison.Ordinal),
        "SelectedOutput should return the row's model reference directly.");
    Assert(
        !selectedCharacterBody.Contains("FirstOrDefault", StringComparison.Ordinal)
            && !selectedOutputBody.Contains("FirstOrDefault", StringComparison.Ordinal),
        "Selected row helpers should not scan node collections by id.");
    Assert(
        !selectedCharacterBody.Contains("Graph.SelectedNode", StringComparison.Ordinal)
            && !selectedOutputBody.Contains("Graph.SelectedNode", StringComparison.Ordinal),
        "Selected row helpers should not resolve the selected node.");
}

static void PropertyRowLookupUsesVisibleItemsSource()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var characterLookupBody = ExtractMethodBody(
        source,
        "private CharacterView? FindVisibleCharacterView");
    var outputLookupBody = ExtractMethodBody(
        source,
        "private OutputView? FindVisibleOutputView");
    var selectUsageBody = ExtractMethodBody(
        source,
        "private void SelectAssetUsageDetail");
    var selectCharacterBody = ExtractMethodBody(
        source,
        "private void SelectCharacterView");
    var selectOutputBody = ExtractMethodBody(
        source,
        "private void SelectOutputView");

    Assert(
        characterLookupBody.Contains(
            "CharactersGrid.ItemsSource is IEnumerable<CharacterView>",
            StringComparison.Ordinal),
        "Character row lookup should use the current items source.");
    Assert(
        outputLookupBody.Contains(
            "OutputsGrid.ItemsSource is IEnumerable<OutputView>",
            StringComparison.Ordinal),
        "Output row lookup should use the current items source.");
    Assert(
        selectUsageBody.Contains("FindVisibleOutputView(outputLabel)", StringComparison.Ordinal)
            && selectUsageBody.Contains("FindVisibleCharacterView(characterName)", StringComparison.Ordinal)
            && selectCharacterBody.Contains("FindVisibleCharacterView(characterId)", StringComparison.Ordinal)
            && selectOutputBody.Contains("FindVisibleOutputView(outputId)", StringComparison.Ordinal),
        "Property navigation should reuse visible row lookup helpers.");
    Assert(
        !source.Contains(".OfType<CharacterView>()", StringComparison.Ordinal)
            && !source.Contains(".OfType<OutputView>()", StringComparison.Ordinal),
        "Property row lookup should not walk DataGrid item containers.");
}

static void TransitionEditsRefreshAssetUsage()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var editBody = ExtractMethodBody(source, "private void EditTransition");
    var resetBody = ExtractMethodBody(source, "private void ResetSelectedOutputTransition");

    Assert(
        editBody.Contains("RefreshAssetUsageAfterBinding();", StringComparison.Ordinal),
        "Editing transition sound should refresh asset usage state.");
    Assert(
        resetBody.Contains("RefreshAssetUsageAfterBinding();", StringComparison.Ordinal),
        "Resetting transition sound should refresh asset usage state.");
}

static void AssetUsageNavigationUsesNodeIdDirectly()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private void NavigateToAssetUsage");

    Assert(
        body.Contains("Graph.SelectNode(usage.NodeId);", StringComparison.Ordinal)
            && body.Contains("Graph.SelectedNodeId == usage.NodeId", StringComparison.Ordinal),
        "Asset usage navigation should select usage node ids through Graph directly.");
    Assert(
        !body.Contains("_project.FindNode(usage.NodeId)", StringComparison.Ordinal),
        "Asset usage navigation should not linearly search project nodes.");
}

static void EditorAssetMutationsSkipDiskSyncRefreshes()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var assetMutationMethods = new[]
    {
        "private void ImportAssets_Click",
        "private void CreateVoiceBlip_Click",
        "private void CreateAssetFolder_Click",
        "private void RenameAssetFolder_Click",
        "private void DeleteAssetFolder_Click",
        "private void MoveAsset_Click",
        "private void RenameAsset_Click",
        "private void DeleteAsset_Click",
        "private string? BrowseAsset",
    };
    var helperBody = ExtractMethodBody(
        source,
        "private void RefreshAssetCatalogAfterEditorChange");

    Assert(
        !Regex.IsMatch(source, @"MarkDirty\(\);\s*\r?\n\s*RefreshAssets\(\);"),
        "Editor-driven asset mutations should not immediately rescan files from disk.");
    Assert(
        helperBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal)
            && helperBody.Contains("RefreshAssets(syncFromDisk: false);", StringComparison.Ordinal),
        "Editor-driven asset mutations should refresh known asset state without disk sync.");

    foreach (var method in assetMutationMethods)
    {
        var body = ExtractMethodBody(source, method);
        Assert(
            !body.Contains("MarkDirty();", StringComparison.Ordinal),
            $"{method} should not refresh the graph for asset manager changes.");
        Assert(
            body.Contains("RefreshAssetCatalogAfterEditorChange();", StringComparison.Ordinal),
            $"{method} should use the catalog-change refresh helper.");
    }
}

static void AssetImportRefreshPolicySkipsUnchangedImports()
{
    Assert(
        !MainWindow.ShouldRefreshAssetsAfterImport(3, 3),
        "Asset import should skip refresh when no assets were added.");
    Assert(
        MainWindow.ShouldRefreshAssetsAfterImport(3, 4),
        "Asset import should refresh after adding a new asset.");
}

static void AssetImportUsesBatchImport()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private void ImportAssets_Click");

    Assert(
        body.Contains("ProjectAssets.ImportMany(_project, _projectPath!, dialog.FileNames)", StringComparison.Ordinal),
        "Bulk asset import should use ProjectAssets.ImportMany.");
    Assert(
        !body.Contains("foreach (var file in dialog.FileNames)", StringComparison.Ordinal)
            && !body.Contains("ImportAssetFile(file)", StringComparison.Ordinal),
        "Bulk asset import should not rebuild import lookup state for each selected file.");
}

static void AssetTransitionSoundBindingPolicyRequiresAudioOutput()
{
    var project = NovelProject.CreateDefault();
    var node = project.FindNode("dialogue-1")
        ?? throw new InvalidOperationException("Default dialogue node was not found.");
    var output = node.Outputs.First();
    var audio = new NovelAsset
    {
        Id = "click",
        Kind = AssetKind.Audio,
        Path = "files/audio_fx/click.wav",
    };
    var image = new NovelAsset
    {
        Id = "bg",
        Kind = AssetKind.Image,
        Path = "files/backgrounds/bg.png",
    };
    var foreignOutput = new NodeOutput { Id = "foreign", Label = "Foreign" };

    Assert(
        MainWindow.CanBindAssetAsOutputTransitionSound(audio, node, output),
        "Audio asset should bind to a selected output of the selected node.");
    Assert(
        !MainWindow.CanBindAssetAsOutputTransitionSound(image, node, output),
        "Image asset should not bind as a transition sound.");
    Assert(
        !MainWindow.CanBindAssetAsOutputTransitionSound(audio, node, null),
        "Transition sound binding should require a selected output.");
    Assert(
        !MainWindow.CanBindAssetAsOutputTransitionSound(audio, node, foreignOutput),
        "Transition sound binding should reject outputs from a different node.");
}

static void AssetTransitionSoundMenuListsNodeOutputs()
{
    var project = NovelProject.CreateDefault();
    var scene = project.FindNode("scene-1")
        ?? throw new InvalidOperationException("Default scene node was not found.");
    var dialogue = project.FindNode("dialogue-1")
        ?? throw new InvalidOperationException("Default dialogue node was not found.");
    var audio = new NovelAsset
    {
        Id = "click",
        Kind = AssetKind.Audio,
        Path = "files/audio_fx/click.wav",
    };
    var image = new NovelAsset
    {
        Id = "bg",
        Kind = AssetKind.Image,
        Path = "files/backgrounds/bg.png",
    };

    Assert(
        MainWindow.GetBindableTransitionSoundOutputs(audio, scene)
            .Select(output => output.Id)
            .SequenceEqual(scene.Outputs.Select(output => output.Id)),
        "Audio transition menu should include scene next outputs.");
    Assert(
        MainWindow.GetBindableTransitionSoundOutputs(audio, dialogue)
            .Select(output => output.Id)
            .SequenceEqual(dialogue.Outputs.Select(output => output.Id)),
        "Audio transition menu should include every dialogue choice output.");
    Assert(
        MainWindow.GetBindableTransitionSoundOutputs(image, dialogue).Count == 0,
        "Non-audio assets should not list transition sound targets.");
    Assert(
        MainWindow.GetBindableTransitionSoundOutputs(audio, null).Count == 0,
        "Transition sound target listing should require a selected node.");
}

static void PreviewAssetResolutionCachesReferences()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "PreviewWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private string ResolveAsset");
    var cacheIndex = body.IndexOf(
        "_resolvedAssetCache.TryGetValue(path, out var cached)",
        StringComparison.Ordinal);
    var resolveIndex = body.IndexOf(
        "_project.ResolveAssetReference(path)",
        StringComparison.Ordinal);

    Assert(
        source.Contains("_resolvedAssetCache", StringComparison.Ordinal),
        "Preview window should keep a resolved asset cache.");
    Assert(
        cacheIndex >= 0 && resolveIndex > cacheIndex,
        "Preview asset resolution should check the cache before scanning project assets.");
    Assert(
        body.Contains("_resolvedAssetCache[path] = resolved;", StringComparison.Ordinal),
        "Preview asset resolution should cache resolved paths.");
}

static void EditorAssetPreviewWindowsCacheResolvedReferences()
{
    AssertResolvedAssetCachePolicy(
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "NovelEngine.Editor",
            "MainMenuEditorWindow.cs"),
        "Main menu editor");
    AssertResolvedAssetCachePolicy(
        Path.Combine(
            FindRepositoryRoot(),
            "src",
            "NovelEngine.Editor",
            "SceneEditorWindow.xaml.cs"),
        "Scene editor");
}

static void AssertResolvedAssetCachePolicy(string path, string owner)
{
    var source = File.ReadAllText(path);
    var body = ExtractMethodBody(source, "private string ResolveAsset");
    var cacheIndex = body.IndexOf(
        "_resolvedAssetCache.TryGetValue(path, out var cached)",
        StringComparison.Ordinal);
    var resolveIndex = body.IndexOf(
        "_project.ResolveAssetReference(path)",
        StringComparison.Ordinal);

    Assert(
        source.Contains(
            "BoundedCache<string, string> _resolvedAssetCache",
            StringComparison.Ordinal),
        $"{owner} should keep a bounded resolved asset cache.");
    Assert(
        cacheIndex >= 0 && resolveIndex > cacheIndex,
        $"{owner} should check the cache before scanning project assets.");
    Assert(
        body.Contains("_resolvedAssetCache.Set(path, resolved);", StringComparison.Ordinal),
        $"{owner} should cache resolved paths.");
}

static void OutputEditorCopyIsOutputNeutral()
{
    Assert(
        OutputEditorWindow.EditorTitle == "Выход",
        "Output editor title should be neutral for scene and dialogue outputs.");
    Assert(
        OutputEditorWindow.LabelCaption == "Текст выхода",
        "Output editor label caption should be output-neutral.");
    Assert(
        OutputEditorWindow.ScriptCaption == "Скрипт выхода",
        "Output editor script caption should be output-neutral.");
    Assert(
        OutputEditorWindow.ScriptBlocksTitle == "Блоки скрипта выхода",
        "Output script block dialog title should be output-neutral.");
    Assert(
        OutputEditorWindow.EmptyLabelMessage == "Текст выхода не может быть пустым.",
        "Output editor validation message should be output-neutral.");
}

static void OutputDetailEditorPolicyAcceptsSceneNextOutputs()
{
    var project = NovelProject.CreateDefault();
    var scene = project.FindNode("scene-1")
        ?? throw new InvalidOperationException("Default scene node was not found.");
    var dialogue = project.FindNode("dialogue-1")
        ?? throw new InvalidOperationException("Default dialogue node was not found.");
    var sceneOutput = scene.Outputs.First();
    var dialogueOutput = dialogue.Outputs.First();
    var foreignOutput = new NodeOutput { Id = "foreign", Label = "Foreign" };

    Assert(
        MainWindow.CanEditOutputDetails(scene, sceneOutput),
        "Scene next outputs should expose output details from the context menu.");
    Assert(
        MainWindow.CanEditOutputDetails(dialogue, dialogueOutput),
        "Dialogue choice outputs should expose output details from the context menu.");
    Assert(
        !MainWindow.CanEditOutputDetails(scene, null),
        "Output detail editing should require a selected output.");
    Assert(
        !MainWindow.CanEditOutputDetails(scene, foreignOutput),
        "Output detail editing should reject outputs from a different node.");
}

static void OutputTransitionEditorPolicyAcceptsSceneNextOutputs()
{
    var project = NovelProject.CreateDefault();
    var scene = project.FindNode("scene-1")
        ?? throw new InvalidOperationException("Default scene node was not found.");
    var dialogue = project.FindNode("dialogue-1")
        ?? throw new InvalidOperationException("Default dialogue node was not found.");
    var sceneOutput = scene.Outputs.First();
    var dialogueOutput = dialogue.Outputs.First();
    var foreignOutput = new NodeOutput { Id = "foreign", Label = "Foreign" };

    Assert(
        MainWindow.CanEditSelectedOutputTransition(scene, sceneOutput),
        "Scene next outputs should be editable from the outputs table.");
    Assert(
        MainWindow.CanEditSelectedOutputTransition(dialogue, dialogueOutput),
        "Dialogue choice transitions should be editable from the outputs table.");
    Assert(
        !MainWindow.CanEditSelectedOutputTransition(scene, null),
        "Transition editing should require a selected output.");
    Assert(
        !MainWindow.CanEditSelectedOutputTransition(scene, foreignOutput),
        "Transition editing should reject outputs from a different node.");
}

static void OutputScriptBlockShortcutUsesCtrlB()
{
    Assert(
        MainWindow.IsOutputScriptBlocksShortcut(Key.B, ModifierKeys.Control),
        "Ctrl+B should open selected output script blocks.");
    Assert(
        !MainWindow.IsOutputScriptBlocksShortcut(Key.B, ModifierKeys.None),
        "Output script block shortcut should require Ctrl.");
    Assert(
        !MainWindow.IsOutputScriptBlocksShortcut(Key.T, ModifierKeys.Control),
        "Ctrl+T should not open output script blocks.");
}

static void OutputTransitionKeyboardShortcutsUseCtrlCombos()
{
    Assert(
        MainWindow.IsOutputTransitionEditShortcut(Key.T, ModifierKeys.Control),
        "Ctrl+T should open selected output transition settings.");
    Assert(
        !MainWindow.IsOutputTransitionEditShortcut(Key.T, ModifierKeys.None),
        "Transition edit shortcut should require Ctrl.");
    Assert(
        !MainWindow.IsOutputTransitionEditShortcut(Key.R, ModifierKeys.Control),
        "Ctrl+R should not open transition settings.");
    Assert(
        MainWindow.IsOutputTransitionResetShortcut(Key.R, ModifierKeys.Control),
        "Ctrl+R should reset selected output transition settings.");
    Assert(
        !MainWindow.IsOutputTransitionResetShortcut(Key.R, ModifierKeys.None),
        "Transition reset shortcut should require Ctrl.");
    Assert(
        !MainWindow.IsOutputTransitionResetShortcut(Key.T, ModifierKeys.Control),
        "Ctrl+T should not reset transition settings.");
}

static void OutputListShortcutsRequireDialogueNodes()
{
    var scene = new NovelNode { Id = "scene", Kind = NodeKind.Scene };
    var dialogue = new NovelNode { Id = "dialogue", Kind = NodeKind.Dialogue };

    Assert(
        !MainWindow.CanEditOutputList(scene),
        "Scene next output should not expose dialogue choice list editing.");
    Assert(
        MainWindow.CanEditOutputList(dialogue),
        "Dialogue outputs should expose choice list editing.");
    Assert(
        !MainWindow.CanEditOutputList(null),
        "Output list editing should require a selected node.");

    Assert(
        MainWindow.IsOutputListDuplicateShortcut(
            Key.D,
            ModifierKeys.Control,
            canEditOutputList: true),
        "Ctrl+D should duplicate a dialogue choice when choice list editing is available.");
    Assert(
        !MainWindow.IsOutputListDuplicateShortcut(
            Key.D,
            ModifierKeys.Control,
            canEditOutputList: false),
        "Ctrl+D should not be consumed by scene output rows.");
    Assert(
        MainWindow.IsOutputListDeleteShortcut(
            Key.Delete,
            ModifierKeys.None,
            canEditOutputList: true),
        "Delete should remove a dialogue choice when choice list editing is available.");
    Assert(
        !MainWindow.IsOutputListDeleteShortcut(
            Key.Delete,
            ModifierKeys.None,
            canEditOutputList: false),
        "Delete should not be consumed by scene output rows.");
    Assert(
        MainWindow.IsOutputListMoveUpShortcut(
            Key.Up,
            ModifierKeys.Alt,
            canEditOutputList: true),
        "Alt+Up should reorder dialogue choices.");
    Assert(
        !MainWindow.IsOutputListMoveUpShortcut(
            Key.Up,
            ModifierKeys.Alt,
            canEditOutputList: false),
        "Alt+Up should not be consumed by scene output rows.");
    Assert(
        MainWindow.IsOutputListMoveDownShortcut(
            Key.Down,
            ModifierKeys.Alt,
            canEditOutputList: true),
        "Alt+Down should reorder dialogue choices.");
    Assert(
        !MainWindow.IsOutputListMoveDownShortcut(
            Key.Down,
            ModifierKeys.Alt,
            canEditOutputList: false),
        "Alt+Down should not be consumed by scene output rows.");
}

static void OutputButtonsHideChoiceActionsForSceneOutputs()
{
    var sceneState = MainWindow.CreateOutputButtonState(
        canEditOutputList: false,
        selected: true,
        selectedIndex: 0,
        outputCount: 1);

    Assert(
        sceneState.ChoiceActionVisibility == Visibility.Collapsed,
        "Scene outputs should hide dialogue-only choice buttons.");
    Assert(
        !sceneState.CanAddChoice
        && !sceneState.CanDuplicateChoice
        && !sceneState.CanMoveChoiceUp
        && !sceneState.CanMoveChoiceDown
        && !sceneState.CanDeleteChoice,
        "Scene outputs should not enable dialogue choice list actions.");
    Assert(
        sceneState.CanEditOutput
        && sceneState.CanEditTransition
        && sceneState.CanDisconnectOutput,
        "Scene outputs should still allow editing output details and transition.");

    var firstChoiceState = MainWindow.CreateOutputButtonState(
        canEditOutputList: true,
        selected: true,
        selectedIndex: 0,
        outputCount: 2);
    Assert(
        firstChoiceState.ChoiceActionVisibility == Visibility.Visible,
        "Dialogue outputs should show choice list buttons.");
    Assert(firstChoiceState.CanAddChoice, "Dialogue nodes should allow adding choices.");
    Assert(firstChoiceState.CanDuplicateChoice, "Selected choices should be duplicable.");
    Assert(!firstChoiceState.CanMoveChoiceUp, "First choice should not move up.");
    Assert(firstChoiceState.CanMoveChoiceDown, "First choice should move down.");
    Assert(firstChoiceState.CanDeleteChoice, "Selected choices should be deletable.");

    var emptyDialogueState = MainWindow.CreateOutputButtonState(
        canEditOutputList: true,
        selected: false,
        selectedIndex: -1,
        outputCount: 0);
    Assert(
        emptyDialogueState.ChoiceActionVisibility == Visibility.Visible,
        "Empty dialogue output lists should still show the add-choice action.");
    Assert(emptyDialogueState.CanAddChoice, "Empty dialogue output lists should allow adding choices.");
    Assert(!emptyDialogueState.CanEditOutput, "No selected output should disable edit.");
    Assert(!emptyDialogueState.CanDisconnectOutput, "No selected output should disable disconnect.");
}

static void OutputTransitionResetAppearsOnlyForCustomizedTransitions()
{
    Assert(
        !MainWindow.ShouldShowResetOutputTransition(null),
        "Transition reset should require a selected output.");
    Assert(
        !MainWindow.ShouldShowResetOutputTransition(new NodeOutput
        {
            Id = "default",
        }),
        "Default transitions should not show the reset action.");
    Assert(
        MainWindow.ShouldShowResetOutputTransition(new NodeOutput
        {
            Id = "sound",
            TransitionSound = "@click",
        }),
        "Transitions with a sound should show the reset action.");
    Assert(
        MainWindow.ShouldShowResetOutputTransition(new NodeOutput
        {
            Id = "fade",
            FadeDurationMs = 700,
        }),
        "Transitions with a custom fade duration should show the reset action.");
}

static void OutputTransitionChangeGuardSkipsUnchangedEdits()
{
    var output = new NodeOutput
    {
        Id = "next",
        TransitionSound = "@click",
        FadeDurationMs = 700,
    };

    Assert(
        !MainWindow.HasOutputTransitionChanges(output, "@click", 700),
        "Unchanged transition dialog values should not dirty the project.");
    Assert(
        MainWindow.HasOutputTransitionChanges(output, "@other", 700),
        "Changed transition sound should dirty the project.");
    Assert(
        MainWindow.HasOutputTransitionChanges(output, "@click", 350),
        "Changed transition fade duration should dirty the project.");
}

static void GraphEditingShortcutsRequireGraphFocus()
{
    Assert(
        MainWindow.IsGraphDeleteShortcut(
            Key.Delete,
            ModifierKeys.None,
            isTextEditing: false,
            isGraphShortcutContext: true),
        "Delete should edit the graph when the graph has focus.");
    Assert(
        !MainWindow.IsGraphDeleteShortcut(
            Key.Delete,
            ModifierKeys.None,
            isTextEditing: false,
            isGraphShortcutContext: false),
        "Delete should not edit the graph while focus is in tables or panels.");
    Assert(
        !MainWindow.IsGraphDeleteShortcut(
            Key.Delete,
            ModifierKeys.None,
            isTextEditing: true,
            isGraphShortcutContext: true),
        "Delete should not edit the graph while text is being edited.");
    Assert(
        !MainWindow.IsGraphDeleteShortcut(
            Key.Delete,
            ModifierKeys.Shift,
            isTextEditing: false,
            isGraphShortcutContext: true),
        "Modified Delete should be left to the focused control.");
    Assert(
        MainWindow.IsGraphDuplicateShortcut(
            Key.D,
            ModifierKeys.Control,
            isTextEditing: false,
            isGraphShortcutContext: true),
        "Ctrl+D should duplicate a graph node when the graph has focus.");
    Assert(
        !MainWindow.IsGraphDuplicateShortcut(
            Key.D,
            ModifierKeys.Control,
            isTextEditing: false,
            isGraphShortcutContext: false),
        "Ctrl+D should not duplicate a graph node from table focus.");
    Assert(
        MainWindow.IsGraphCenterShortcut(
            Key.Home,
            ModifierKeys.None,
            isTextEditing: false,
            isGraphShortcutContext: true),
        "Home should center the graph when the graph has focus.");
    Assert(
        !MainWindow.IsGraphCenterShortcut(
            Key.Home,
            ModifierKeys.None,
            isTextEditing: false,
            isGraphShortcutContext: false),
        "Home should be left to focused lists and tables outside the graph.");
}

static void DiagnosticPanelStampTracksVisibleDiagnostics()
{
    var diagnostics = new[]
    {
        new ProjectDiagnostic(
            ProjectDiagnosticSeverity.Error,
            "Нода «start»",
            "Нет выхода."),
        new ProjectDiagnostic(
            ProjectDiagnosticSeverity.Warning,
            "Ассет @bg",
            "Ассет пока нигде не используется."),
    };
    var report = new ProjectDiagnosticReport(diagnostics);
    var baseline = MainWindow.CreateDiagnosticPanelStamp(report, showPanel: false);

    Assert(
        baseline == MainWindow.CreateDiagnosticPanelStamp(report, showPanel: false),
        "Diagnostic panel stamp should remain stable for identical diagnostics.");
    Assert(
        baseline != MainWindow.CreateDiagnosticPanelStamp(report, showPanel: true),
        "Diagnostic panel stamp should track explicit panel visibility requests.");
    Assert(
        baseline != MainWindow.CreateDiagnosticPanelStamp(
            new ProjectDiagnosticReport(diagnostics[..1]),
            showPanel: false),
        "Diagnostic panel stamp should track changed diagnostics.");

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(
        source,
        "internal static DiagnosticPanelStamp CreateDiagnosticPanelStamp");
    var refreshBody = ExtractMethodBody(
        source,
        "private ProjectDiagnosticReport RefreshProjectDiagnostics");
    var viewBody = ExtractMethodBody(
        source,
        "private static List<DiagnosticView> CreateDiagnosticViews");
    Assert(
        body.Contains("report.Fingerprint", StringComparison.Ordinal),
        "Diagnostic panel stamp should use the report fingerprint.");
    Assert(
        !body.Contains("foreach", StringComparison.Ordinal),
        "Diagnostic panel stamp should not rescan diagnostics.");
    Assert(
        refreshBody.Contains("var diagnostics = CreateDiagnosticViews(report.Diagnostics);", StringComparison.Ordinal)
            && !refreshBody.Contains(".Select(", StringComparison.Ordinal)
            && !refreshBody.Contains(".ToList(", StringComparison.Ordinal),
        "Diagnostic refresh should build rows without LINQ pipelines.");
    Assert(
        viewBody.Contains("var views = new List<DiagnosticView>(diagnostics.Count);", StringComparison.Ordinal)
            && viewBody.Contains("for (var index = 0; index < diagnostics.Count; index++)", StringComparison.Ordinal)
            && !viewBody.Contains(".Select(", StringComparison.Ordinal)
            && !viewBody.Contains(".ToList(", StringComparison.Ordinal),
        "Diagnostic rows should be built with one direct pass.");
}

static void AppCollectionStylesEnableVirtualization()
{
    var appXaml = XDocument.Load(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "App.xaml"));

    AssertStyleSetters(
        appXaml,
        "DataGrid",
        "EnableRowVirtualization",
        "EnableColumnVirtualization",
        "VirtualizingPanel.IsVirtualizing",
        "VirtualizingPanel.VirtualizationMode",
        "ScrollViewer.CanContentScroll");
    AssertStyleSetters(
        appXaml,
        "TreeView",
        "VirtualizingPanel.IsVirtualizing",
        "VirtualizingPanel.VirtualizationMode",
        "ScrollViewer.CanContentScroll");
    AssertStyleSetters(
        appXaml,
        "ListBox",
        "VirtualizingPanel.IsVirtualizing",
        "VirtualizingPanel.VirtualizationMode",
        "ScrollViewer.CanContentScroll");
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
    var manualCompletionLimit =
        CodeEditorPerformancePolicy.MaxManualCompletionSourceLength;
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
        CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
            automaticCompletionLimit + 1,
            force: true),
        "Manual completions should still work past the automatic completion boundary.");
    Assert(
        CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
            manualCompletionLimit,
            force: true),
        "Manual completions should include the configured boundary length.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
            manualCompletionLimit + 1,
            force: true),
        "Manual completions should not force lookup for oversized documents.");
    Assert(
        !CodeEditorPerformancePolicy.ShouldRunCompletionLookup(
            automaticCompletionLimit + 1,
            force: false),
        "Non-forced completion lookup should use the automatic completion boundary.");
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
        CodeEditorControl.ShouldScheduleAutomaticCompletion(
            automaticCompletionLimit,
            insertedText: true),
        "Inserted text should schedule the automatic completion timer at the configured boundary length.");
    Assert(
        !CodeEditorControl.ShouldScheduleAutomaticCompletion(
            automaticCompletionLimit,
            insertedText: false),
        "Deletion-only edits should not schedule the automatic completion timer.");
    Assert(
        !CodeEditorControl.ShouldScheduleAutomaticCompletion(
            automaticCompletionLimit + 1,
            insertedText: true),
        "Inserted text should still respect the automatic completion boundary.");
    Assert(
        CodeEditorControl.ShouldScheduleHistoryRecord(historyLimit),
        "History timer should run at the configured boundary length.");
    Assert(
        !CodeEditorControl.ShouldScheduleHistoryRecord(historyLimit + 1),
        "History timer should not run for oversized documents.");
    Assert(
        !CodeEditorControl.ShouldResetCompletionPopup(
            isOpen: false,
            hasContext: false,
            hasItemsSource: false),
        "Already closed completion popup should not reset WPF popup state.");
    Assert(
        CodeEditorControl.ShouldResetCompletionPopup(
            isOpen: true,
            hasContext: false,
            hasItemsSource: false),
        "Open completion popup should be reset.");
    Assert(
        CodeEditorControl.ShouldResetCompletionPopup(
            isOpen: false,
            hasContext: true,
            hasItemsSource: false),
        "Completion context should be cleared.");
    Assert(
        CodeEditorControl.ShouldResetCompletionPopup(
            isOpen: false,
            hasContext: false,
            hasItemsSource: true),
        "Completion item source should be cleared.");
    Assert(
        !CodeEditorControl.ShouldMoveCompletionSelection(0)
            && !CodeEditorControl.ShouldMoveCompletionSelection(1)
            && CodeEditorControl.ShouldMoveCompletionSelection(2),
        "Completion selection movement should skip empty and single-item lists.");
    Assert(
        MainWindow.ShouldReadCodeCursorSource(highlightedLimit),
        "Cursor source reads should include the configured boundary length.");
    Assert(
        !MainWindow.ShouldReadCodeCursorSource(highlightedLimit + 1),
        "Cursor source reads should be skipped for oversized documents.");
    Assert(
        MainWindow.ShouldScheduleLiveCodeAnalysis(liveAnalysisLimit),
        "Live code analysis timer should run at the configured boundary length.");
    Assert(
        !MainWindow.ShouldScheduleLiveCodeAnalysis(liveAnalysisLimit + 1),
        "Live code analysis timer should not read oversized documents after typing.");

    var editorSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "CodeEditorControl.cs"));
    var textChangedBody = ExtractMethodBody(
        editorSource,
        "protected override void OnTextChanged");
    Assert(
        textChangedBody.Contains(
            "var insertedText = UpdateEstimatedSourceLength(e);",
            StringComparison.Ordinal)
            && textChangedBody.Contains(
                "ShouldScheduleAutomaticCompletion(_estimatedSourceLength, insertedText)",
                StringComparison.Ordinal),
        "Code editor text changes should skip automatic completion for delete-only edits.");
}

static void CodeCursorCacheSkipsOversizedLineScans()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var body = ExtractMethodBody(source, "private void SetCodeCursorCache");
    var buildBody = ExtractMethodBody(source, "private static int[] BuildLineStarts");
    var guardIndex = body.IndexOf(
        "ShouldReadCodeCursorSource(source.Length)",
        StringComparison.Ordinal);
    var buildIndex = body.IndexOf(
        "BuildLineStarts(source)",
        StringComparison.Ordinal);

    Assert(
        guardIndex >= 0,
        "Code cursor cache should guard oversized sources before scanning lines.");
    Assert(
        buildIndex >= 0,
        "Code cursor cache should still build line starts for normal sources.");
    Assert(
        guardIndex < buildIndex,
        "Code cursor cache should not build line starts before the oversized-source guard.");
    Assert(
        body.Contains("_codeLineStarts = [0];", StringComparison.Ordinal),
        "Oversized code cursor cache should clear stale line starts.");
    Assert(
        body.Contains("_codeCursorCacheDirty = false;", StringComparison.Ordinal),
        "Oversized code cursor cache should avoid repeated scan attempts until the source changes.");
    Assert(
        buildBody.Contains("var lineCount = 1;", StringComparison.Ordinal)
            && buildBody.Contains("var starts = new int[lineCount];", StringComparison.Ordinal)
            && buildBody.Contains("starts[lineIndex++] = index + 1;", StringComparison.Ordinal)
            && !buildBody.Contains("new List<int>", StringComparison.Ordinal)
            && !buildBody.Contains(".ToArray()", StringComparison.Ordinal),
        "Code cursor line starts should allocate the exact array without List/ToArray churn.");
}

static void CodeRefreshSkipsUnchangedRichTextReset()
{
    var editorSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "CodeEditorControl.cs"));
    var windowSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var refreshBody = ExtractMethodBody(
        windowSource,
        "private void RefreshCodeFromProject");
    var guardIndex = refreshBody.IndexOf(
        "CodeEditor.HasCachedSourceText(code)",
        StringComparison.Ordinal);
    var assignIndex = refreshBody.IndexOf(
        "CodeEditor.SourceText = code;",
        StringComparison.Ordinal);

    Assert(
        editorSource.Contains("internal bool HasCachedSourceText", StringComparison.Ordinal),
        "Code editor should expose a cheap cached-source check.");
    Assert(
        guardIndex >= 0,
        "Code refresh should check whether the RichTextBox already has the requested source.");
    Assert(
        assignIndex >= 0,
        "Code refresh should still update the RichTextBox when the source changed.");
    Assert(
        guardIndex < assignIndex,
        "Code refresh should guard unchanged source before resetting the RichTextBox document.");
}

static void CodeEditorPlainTextReplaceSkipsSpanWork()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "CodeEditorControl.cs"));
    var body = ExtractMethodBody(source, "private void ReplaceDocument");
    var appendBody = ExtractMethodBody(source, "private static void AppendText");
    var plainTextGuardIndex = body.IndexOf(
        "if (spans.Count == 0 && !errorStart.HasValue)",
        StringComparison.Ordinal);
    var defaultForegroundIndex = body.IndexOf(
        "var defaultForeground =",
        StringComparison.Ordinal);
    var boundaryIndex = body.IndexOf(
        "new SortedSet<int>",
        StringComparison.Ordinal);
    var sortIndex = body.IndexOf(
        "Array.Sort(orderedSpans, CompareSyntaxSpans);",
        StringComparison.Ordinal);
    var streamingBoundaryIndex = body.IndexOf(
        "foreach (var position in boundaries)",
        StringComparison.Ordinal);
    var renderedCopyIndex = body.IndexOf(
        "_renderedSpans = CopySyntaxSpans(spans);",
        StringComparison.Ordinal);
    var copyBody = ExtractMethodBody(
        source,
        "private static ProjectLanguageSyntaxSpan[] CopySyntaxSpans");
    var comparerBody = ExtractMethodBody(
        source,
        "private static int CompareSyntaxSpans");

    Assert(
        plainTextGuardIndex >= 0,
        "Plain text document replacement should have a fast path.");
    Assert(
        defaultForegroundIndex >= 0 && defaultForegroundIndex < plainTextGuardIndex,
        "Document replacement should resolve the default text brush once per repaint.");
    Assert(
        boundaryIndex > plainTextGuardIndex && sortIndex > plainTextGuardIndex,
        "Plain text fast path should run before syntax boundary and sorting work.");
    Assert(
        streamingBoundaryIndex > sortIndex
            && renderedCopyIndex > streamingBoundaryIndex
            && !body.Contains("boundaries.ToArray()", StringComparison.Ordinal)
            && !body.Contains(".OrderBy(", StringComparison.Ordinal)
            && !body.Contains(".ToArray()", StringComparison.Ordinal),
        "Highlighted document replacement should stream boundaries and avoid LINQ array copies.");
    Assert(
        copyBody.Contains("var copy = new ProjectLanguageSyntaxSpan[spans.Count];", StringComparison.Ordinal)
            && copyBody.Contains("for (var index = 0; index < spans.Count; index++)", StringComparison.Ordinal)
            && !copyBody.Contains(".ToArray()", StringComparison.Ordinal),
        "Rendered syntax span copies should use a direct indexed copy.");
    Assert(
        comparerBody.Contains("left.Start.CompareTo(right.Start)", StringComparison.Ordinal)
            && comparerBody.Contains("right.Length.CompareTo(left.Length)", StringComparison.Ordinal),
        "Syntax span sorting should preserve start ascending and length descending ordering.");
    Assert(
        body.Contains("AppendText(", StringComparison.Ordinal)
            && body.Contains("source,", StringComparison.Ordinal)
            && body.Contains("isError: false,", StringComparison.Ordinal)
            && body.Contains("defaultForeground);", StringComparison.Ordinal),
        "Plain text fast path should append the source as a single undecorated run.");
    Assert(
        appendBody.Contains(": defaultForeground", StringComparison.Ordinal)
            && !appendBody.Contains("Application.Current.Resources", StringComparison.Ordinal),
        "AppendText should not look up application resources for every rendered run.");
}

static void CodeHighlightingSkipsSpanOverflowRepaint()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var applyBody = ExtractMethodBody(
        source,
        "private void ApplyCodeHighlighting");
    var spansBody = ExtractMethodBody(
        source,
        "private IReadOnlyList<ProjectLanguageSyntaxSpan> GetCodeSyntaxSpans");

    Assert(
        source.Contains("private bool _codeSyntaxSpanLimitExceeded;", StringComparison.Ordinal),
        "Code editor should remember when syntax highlighting exceeds the span limit.");
    Assert(
        spansBody.Contains("ProjectLanguage.TryGetSyntaxSpans(", StringComparison.Ordinal),
        "Code editor should stop syntax span collection at the configured limit.");
    Assert(
        spansBody.Contains("CodeEditorPerformancePolicy.MaxHighlightedSyntaxSpans", StringComparison.Ordinal),
        "Code editor should use the configured syntax span limit.");
    Assert(
        applyBody.Contains("if (error is null && _codeSyntaxSpanLimitExceeded)", StringComparison.Ordinal)
            && applyBody.Contains("return;", StringComparison.Ordinal),
        "Code editor should skip live repaint when syntax spans overflow without an error.");
    Assert(
        applyBody.IndexOf(
            "if (error is null && _codeSyntaxSpanLimitExceeded)",
            StringComparison.Ordinal)
        < applyBody.IndexOf("CodeEditor.ApplySyntax(", StringComparison.Ordinal),
        "Code editor should skip overflow repaint before touching the RichTextBox document.");
}

static void VisualScriptFilterKeepsPreviewCache()
{
    Assert(
        VisualScriptBlocksWindow.ShouldUpdatePreview(
            blocksChanged: true,
            currentPreview: "set flag = true"),
        "Visual script preview should refresh after block changes.");
    Assert(
        VisualScriptBlocksWindow.ShouldUpdatePreview(
            blocksChanged: false,
            currentPreview: string.Empty),
        "Visual script preview should render the first time.");
    Assert(
        !VisualScriptBlocksWindow.ShouldUpdatePreview(
            blocksChanged: false,
            currentPreview: "set flag = true"),
        "Visual script filter changes should keep the compiled preview cache.");
}

static void VisualScriptFilterUsesDebouncedRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "VisualScriptBlocksWindow.cs"));
    var scheduleBody = ExtractMethodBody(
        source,
        "private void ScheduleFilterRefresh");
    var refreshBody = ExtractMethodBody(
        source,
        "private void RefreshList");

    Assert(
        source.Contains(
            "_filterBox.TextChanged += (_, _) => ScheduleFilterRefresh();",
            StringComparison.Ordinal),
        "Visual script filter text changes should schedule a debounced refresh.");
    Assert(
        !source.Contains(
            "_filterBox.TextChanged += (_, _) =>\r\n            RefreshList",
            StringComparison.Ordinal)
            && !source.Contains(
                "_filterBox.TextChanged += (_, _) =>\n            RefreshList",
                StringComparison.Ordinal),
        "Visual script filter text changes should not refresh immediately.");
    Assert(
        scheduleBody.Contains("_filterRefreshTimer.Stop();", StringComparison.Ordinal)
            && scheduleBody.Contains("_filterRefreshTimer.Start();", StringComparison.Ordinal),
        "Visual script filter refresh should restart its debounce timer.");
    Assert(
        source.Contains(
            "RefreshList(GetSelectedBlockIndex(), blocksChanged: false);",
            StringComparison.Ordinal),
        "Visual script filter refresh should keep the compiled preview cache.");
    Assert(
        refreshBody.Contains("var views = new List<BlockView>(_blocks.Count);", StringComparison.Ordinal)
            && refreshBody.Contains("for (var index = 0; index < _blocks.Count; index++)", StringComparison.Ordinal)
            && refreshBody.Contains("visibleIndex = views.Count;", StringComparison.Ordinal)
            && !refreshBody.Contains(".Select(", StringComparison.Ordinal)
            && !refreshBody.Contains(".Where(", StringComparison.Ordinal)
            && !refreshBody.Contains(".ToList(", StringComparison.Ordinal)
            && !refreshBody.Contains(".FindIndex(", StringComparison.Ordinal),
        "Visual script filter refresh should build visible rows and selection in one direct pass.");
}

static void ScriptLiteralLoadSuppressesChangeEvents()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "ScriptLiteralEditorControl.cs"));
    var loadBody = ExtractMethodBody(source, "public void LoadLiteral");
    var raiseBody = ExtractMethodBody(source, "private void RaiseLiteralChanged");

    Assert(
        source.Contains("private bool _suppressLiteralChanged;", StringComparison.Ordinal),
        "Script literal editor should track programmatic literal loading.");
    Assert(
        source.Contains("RaiseLiteralChanged();", StringComparison.Ordinal),
        "Script literal editor event handlers should route through the guarded notifier.");
    Assert(
        loadBody.Contains("_suppressLiteralChanged = true;", StringComparison.Ordinal)
            && loadBody.Contains("finally", StringComparison.Ordinal)
            && loadBody.Contains("_suppressLiteralChanged = false;", StringComparison.Ordinal),
        "Script literal loading should suppress change events until loading finishes.");
    Assert(
        raiseBody.Contains("if (!_suppressLiteralChanged)", StringComparison.Ordinal)
            && raiseBody.Contains("LiteralChanged?.Invoke(this, EventArgs.Empty);", StringComparison.Ordinal),
        "Script literal changes should still notify after interactive edits.");
}

static void ScriptBlockDialogGuardSkipsUnchangedApply()
{
    var block = new VisualScriptBlock
    {
        Id = "block-1",
        Kind = VisualScriptBlockKind.SetVariable,
        VariableName = "score",
        Value = "1",
    };
    var blocks = new[] { block };

    Assert(
        !MainWindow.HasScriptBlockDialogChanges(
            blocks,
            blocks.Select(item => item.Clone()).ToList(),
            clearImportedScript: false,
            textScript: "add score 1"),
        "Unchanged script block dialog values should not dirty the project.");
    Assert(
        MainWindow.HasScriptBlockDialogChanges(
            blocks,
            [
                new VisualScriptBlock
                {
                    Id = "block-1",
                    Kind = VisualScriptBlockKind.SetVariable,
                    VariableName = "score",
                    Value = "2",
                },
            ],
            clearImportedScript: false,
            textScript: "add score 1"),
        "Changed script blocks should dirty the project.");
    Assert(
        MainWindow.HasScriptBlockDialogChanges(
            blocks,
            blocks.Select(item => item.Clone()).ToList(),
            clearImportedScript: true,
            textScript: "add score 1"),
        "Clearing an existing text script should dirty the project.");
    Assert(
        !MainWindow.HasScriptBlockDialogChanges(
            blocks,
            blocks.Select(item => item.Clone()).ToList(),
            clearImportedScript: true,
            textScript: string.Empty),
        "Clearing an already empty text script should not dirty the project.");
}

static void NodePropertyGuardSkipsUnchangedApply()
{
    var node = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        Title = "Scene",
        Speaker = string.Empty,
        Text = "Line",
        InheritBackground = false,
        Background = "@bg",
        InheritMusic = true,
        Music = string.Empty,
        InheritCharacters = false,
        Script = "set flag = true",
    };

    Assert(
        !MainWindow.HasNodePropertyChanges(
            node,
            node.Title,
            node.Speaker,
            node.Text,
            node.InheritBackground,
            node.Background,
            node.InheritMusic,
            node.Music,
            node.InheritCharacters,
            node.Script),
        "Unchanged node properties should not refresh the graph.");
    Assert(
        MainWindow.HasNodePropertyChanges(
            node,
            "Scene 2",
            node.Speaker,
            node.Text,
            node.InheritBackground,
            node.Background,
            node.InheritMusic,
            node.Music,
            node.InheritCharacters,
            node.Script),
        "Changed title should refresh node properties.");
    Assert(
        MainWindow.HasNodePropertyChanges(
            node,
            node.Title,
            node.Speaker,
            node.Text,
            node.InheritBackground,
            node.Background,
            node.InheritMusic,
            node.Music,
            node.InheritCharacters,
            "set flag = false"),
        "Changed script should refresh node properties.");
}

static void NodeRenderedPropertyGuardSkipsHiddenGraphRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var applyBody = ExtractMethodBody(source, "private bool ApplyProperties");
    var scriptBlocksBody = ExtractMethodBody(
        source,
        "private void EditNodeScriptBlocks(NovelNode node)");
    var node = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        Title = "Scene",
        Speaker = "Hero",
        Text = "Line",
        Background = "@old",
        Music = "@theme",
        InheritCharacters = false,
        Script = "set flag = true",
    };

    Assert(
        MainWindow.HasNodePropertyChanges(
            node,
            node.Title,
            node.Speaker,
            node.Text,
            inheritBackground: true,
            "@new",
            inheritMusic: false,
            node.Music,
            inheritCharacters: true,
            "set flag = false"),
        "Hidden node properties should still dirty the project.");
    Assert(
        !MainWindow.HasRenderedNodePropertyChanges(
            node,
            node.Title,
            node.Speaker,
            node.Text),
        "Hidden node properties should not refresh the graph card.");
    Assert(
        MainWindow.HasRenderedNodePropertyChanges(
            node,
            "Scene 2",
            node.Speaker,
            node.Text),
        "Changing the visible title should refresh the graph card.");
    Assert(
        MainWindow.HasRenderedNodePropertyChanges(
            node,
            node.Title,
            "Guide",
            node.Text),
        "Changing the visible speaker should refresh the graph card.");
    Assert(
        MainWindow.HasRenderedNodePropertyChanges(
            node,
            node.Title,
            node.Speaker,
            "Different line"),
        "Changing the visible preview text should refresh the graph card.");
    Assert(
        applyBody.Contains("HasRenderedNodePropertyChanges(", StringComparison.Ordinal)
            && applyBody.Contains("MarkDirty(refreshGraph);", StringComparison.Ordinal),
        "Node property apply should skip graph refresh for hidden-only changes.");
    Assert(
        scriptBlocksBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
        "Node script block edits should skip graph refresh.");
}

static void NodeCharacterEditingPolicyRespectsInheritance()
{
    var start = new NovelNode
    {
        Id = "start",
        Kind = NodeKind.Start,
        InheritCharacters = true,
    };
    var scene = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        InheritCharacters = false,
    };
    var inheritedScene = new NovelNode
    {
        Id = "inherited",
        Kind = NodeKind.Scene,
        InheritCharacters = true,
    };

    Assert(
        MainWindow.CanEditNodeCharacters(start, inheritCharactersChecked: true),
        "Start node characters should remain editable regardless of the inheritance checkbox.");
    Assert(
        MainWindow.CanEditNodeCharacters(scene, inheritCharactersChecked: false),
        "Scene characters should be editable when character inheritance is off.");
    Assert(
        !MainWindow.CanEditNodeCharacters(scene, inheritCharactersChecked: true),
        "Scene characters should not be editable while the panel has inherited characters checked.");
    Assert(
        !MainWindow.CanEditNodeCharacters(inheritedScene, inheritedScene.InheritCharacters),
        "Inherited scene characters should not be edited by direct panel commands.");
    Assert(
        !MainWindow.CanEditNodeCharacters(null, inheritCharactersChecked: false),
        "Character editing should require a selected node.");
}

static void NodeCharacterActionsSkipHiddenGraphRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var hiddenCharacterMethods = new[]
    {
        "private void AddCharacter_Click",
        "private void AddLibraryCharacter_Click",
        "private void SaveCharacterToLibrary_Click",
        "private void EditCharacter_Click",
        "private void DeleteCharacter_Click",
        "private void DuplicateCharacter_Click",
        "private void MoveSelectedCharacter",
        "private void SetSelectedCharacterPosition",
    };

    foreach (var method in hiddenCharacterMethods)
    {
        var body = ExtractMethodBody(source, method);
        Assert(
            body.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
            $"{method} should dirty character data without refreshing the graph.");
    }
}

static void NodeVoiceBindingMaterializesInheritedCharacters()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var bindingBody = ExtractMethodBody(
        source,
        "internal static NodeVoiceBindingResult TryBindVoiceAssetToNodeCharacter");
    var node = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        UsesTypeDefaults = true,
        InheritCharacters = true,
    };
    node.Characters.Add(new CharacterPlacement
    {
        Id = "stale",
        Name = "Stale",
    });
    var inherited = new[]
    {
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
        },
    };
    var asset = new NovelAsset
    {
        Id = "hero_voice",
        Kind = AssetKind.Audio,
        Folder = "voices",
        Path = "files/voices/hero.wav",
    };

    var bound = MainWindow.TryBindVoiceAssetToNodeCharacter(
        node,
        asset,
        "hero",
        inherited);

    Assert(
        bound == NodeVoiceBindingResult.Changed,
        "Voice binding did not accept an inherited character.");
    Assert(!node.InheritCharacters, "Voice binding did not materialize inherited characters.");
    Assert(node.Characters.Count == 1, "Voice binding kept stale local characters.");
    Assert(
        node.Characters[0].Id == "hero",
        "Voice binding did not copy the inherited target character.");
    Assert(
        node.Characters[0].GetVoiceSounds().SequenceEqual([AssetReference.Create(asset.Id)]),
        "Voice binding did not attach the voice reference to the inherited character.");
    Assert(
        node.PropertyOverrides.Contains("inheritCharacters")
            && node.PropertyOverrides.Contains("characters"),
        "Voice binding did not mark type-default character overrides.");
    Assert(
        bindingBody.Contains("foreach (var candidate in effectiveCharacters ?? node.Characters)", StringComparison.Ordinal)
            && !bindingBody.Contains(".Select(", StringComparison.Ordinal)
            && !bindingBody.Contains(".ToList(", StringComparison.Ordinal)
            && !bindingBody.Contains(".FirstOrDefault(", StringComparison.Ordinal),
        "Inherited voice binding should clone and find characters in one direct pass.");
}

static void NodeVoiceBindingSkipsMissingInheritedCharacter()
{
    var node = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        UsesTypeDefaults = true,
        InheritCharacters = true,
    };
    node.Characters.Add(new CharacterPlacement
    {
        Id = "local",
        Name = "Local",
    });
    var inherited = new[]
    {
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
        },
    };
    var asset = new NovelAsset
    {
        Id = "hero_voice",
        Kind = AssetKind.Audio,
        Folder = "voices",
        Path = "files/voices/hero.wav",
    };

    var bound = MainWindow.TryBindVoiceAssetToNodeCharacter(
        node,
        asset,
        "missing",
        inherited);

    Assert(
        bound == NodeVoiceBindingResult.Unavailable,
        "Voice binding accepted a missing inherited character.");
    Assert(node.InheritCharacters, "Failed voice binding changed character inheritance.");
    Assert(node.Characters.Count == 1, "Failed voice binding replaced local characters.");
    Assert(node.Characters[0].Id == "local", "Failed voice binding changed the local character.");
    Assert(node.Characters[0].GetVoiceSounds().Count == 0, "Failed voice binding added a voice reference.");
    Assert(node.PropertyOverrides.Count == 0, "Failed voice binding marked type-default overrides.");
}

static void NodeVoiceBindingSkipsDuplicateInheritedVoice()
{
    var voiceReference = AssetReference.Create("hero_voice");
    var node = new NovelNode
    {
        Id = "scene",
        Kind = NodeKind.Scene,
        UsesTypeDefaults = true,
        InheritCharacters = true,
    };
    node.Characters.Add(new CharacterPlacement
    {
        Id = "local",
        Name = "Local",
    });
    var inherited = new[]
    {
        new CharacterPlacement
        {
            Id = "hero",
            Name = "Hero",
            VoiceSounds = { voiceReference },
        },
    };
    var asset = new NovelAsset
    {
        Id = "hero_voice",
        Kind = AssetKind.Audio,
        Folder = "voices",
        Path = "files/voices/hero.wav",
    };

    var bound = MainWindow.TryBindVoiceAssetToNodeCharacter(
        node,
        asset,
        "hero",
        inherited);

    Assert(
        bound == NodeVoiceBindingResult.Unchanged,
        "Voice binding changed a character that already had the voice.");
    Assert(node.InheritCharacters, "Duplicate voice binding changed character inheritance.");
    Assert(node.Characters.Count == 1, "Duplicate voice binding replaced local characters.");
    Assert(node.Characters[0].Id == "local", "Duplicate voice binding changed the local character.");
    Assert(node.PropertyOverrides.Count == 0, "Duplicate voice binding marked type-default overrides.");
}

static void OutputEditorGuardSkipsUnchangedApply()
{
    var block = new VisualScriptBlock
    {
        Id = "block-1",
        Kind = VisualScriptBlockKind.SetVariable,
        VariableName = "score",
        Value = "1",
    };
    var output = new NodeOutput
    {
        Id = "next",
        Label = "Дальше",
        Condition = "score >= 1",
        Script = "add score 1",
        ScriptBlocks = { block },
    };

    Assert(
        !MainWindow.HasOutputEditorChanges(
            output,
            output.Label,
            output.Condition,
            output.ConditionExpression,
            output.Script,
            output.ScriptBlocks.Select(item => item.Clone()).ToList()),
        "Unchanged output editor values should not dirty the project.");
    Assert(
        MainWindow.HasOutputEditorChanges(
            output,
            "Продолжить",
            output.Condition,
            output.ConditionExpression,
            output.Script,
            output.ScriptBlocks),
        "Changed output label should dirty the project.");
    Assert(
        MainWindow.HasOutputEditorChanges(
            output,
            output.Label,
            output.Condition,
            output.ConditionExpression,
            "set visited = true",
            output.ScriptBlocks),
        "Changed output script should dirty the project.");
    Assert(
        MainWindow.HasOutputEditorChanges(
            output,
            output.Label,
            output.Condition,
            output.ConditionExpression,
            output.Script,
            [
                new VisualScriptBlock
                {
                    Id = "block-1",
                    Kind = VisualScriptBlockKind.SetVariable,
                    VariableName = "score",
                    Value = "2",
                },
            ]),
        "Changed output script blocks should dirty the project.");
}

static void OutputRenderedPropertyGuardSkipsHiddenGraphRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var editBody = ExtractMethodBody(source, "private void EditOutput_Click");
    var scriptBlocksBody = ExtractMethodBody(
        source,
        "private void EditOutputScriptBlocks");
    var resetTransitionBody = ExtractMethodBody(
        source,
        "private void ResetSelectedOutputTransition");
    var editTransitionBody = ExtractMethodBody(source, "private void EditTransition");
    var bindTransitionBody = ExtractMethodBody(
        source,
        "private void BindAssetAsOutputTransitionSound");
    var output = new NodeOutput
    {
        Id = "next",
        Label = "Дальше",
        Condition = "score >= 1",
        Script = "add score 1",
    };

    Assert(
        MainWindow.HasOutputEditorChanges(
            output,
            output.Label,
            "score >= 2",
            output.ConditionExpression,
            "set visited = true",
            output.ScriptBlocks),
        "Hidden output fields should still dirty the project.");
    Assert(
        !MainWindow.HasRenderedOutputPropertyChanges(output, output.Label),
        "Hidden output fields should not refresh the graph port label.");
    Assert(
        MainWindow.HasRenderedOutputPropertyChanges(output, "Продолжить"),
        "Changing the visible output label should refresh the graph port label.");
    Assert(
        editBody.Contains("HasRenderedOutputPropertyChanges(", StringComparison.Ordinal)
            && editBody.Contains("MarkDirty(refreshGraph);", StringComparison.Ordinal),
        "Output editor apply should refresh the graph only for visible output changes.");
    Assert(
        scriptBlocksBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
        "Output script block edits should skip graph refresh.");
    Assert(
        resetTransitionBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
        "Resetting output transition should skip graph refresh.");
    Assert(
        editTransitionBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
        "Editing output transition should skip graph refresh.");
    Assert(
        bindTransitionBody.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
        "Binding an output transition sound should skip graph refresh.");
}

static void ProjectExplorerStampTracksVisibleNodeFields()
{
    var project = NovelProject.CreateDefault();
    var baseline = MainWindow.CreateProjectExplorerStamp(project, " scene ");
    var emptySearchBaseline = MainWindow.CreateProjectExplorerStamp(project, string.Empty);

    Assert(
        baseline == MainWindow.CreateProjectExplorerStamp(project, "scene"),
        "Project explorer stamp should normalize the search query.");

    var changedHiddenFieldsProject = NovelProject.CreateDefault();
    changedHiddenFieldsProject.Nodes[0].Speaker += " updated";
    changedHiddenFieldsProject.Nodes[0].Text += " updated";
    Assert(
        emptySearchBaseline
            == MainWindow.CreateProjectExplorerStamp(changedHiddenFieldsProject, string.Empty),
        "Project explorer stamp should ignore searchable-only fields when search is empty.");

    var changedTitleProject = NovelProject.CreateDefault();
    changedTitleProject.Nodes[0].Title += " updated";
    Assert(
        baseline != MainWindow.CreateProjectExplorerStamp(changedTitleProject, "scene"),
        "Project explorer stamp should change when a node title changes.");

    var changedSearchProject = NovelProject.CreateDefault();
    changedSearchProject.Nodes[0].Text += " extra";
    Assert(
        baseline != MainWindow.CreateProjectExplorerStamp(changedSearchProject, "scene"),
        "Project explorer stamp should change when searchable node text changes.");
    Assert(
        emptySearchBaseline
            == MainWindow.CreateProjectExplorerStamp(changedSearchProject, string.Empty),
        "Project explorer stamp should not refresh hidden searchable text without a search query.");

    var changedPositionProject = NovelProject.CreateDefault();
    changedPositionProject.Nodes[0].X += 10;
    Assert(
        baseline == MainWindow.CreateProjectExplorerStamp(changedPositionProject, "scene"),
        "Project explorer stamp should ignore node position-only changes.");
}

static void ProjectExplorerGroupsFilteredNodesOnce()
{
    var project = NovelProject.CreateDefault();
    project.Nodes.Add(new NovelNode
    {
        Id = "scene-extra",
        Kind = NodeKind.Scene,
        Title = "Hidden room",
        Text = "quiet branch",
    });
    project.Nodes.Add(new NovelNode
    {
        Id = "dialogue-extra",
        Kind = NodeKind.Dialogue,
        Title = "Hero choice",
        Text = "branch line",
    });

    var groups = MainWindow.BuildProjectExplorerGroups(project.Nodes, "branch");
    Assert(groups.StartNodes.Count == 0, "Explorer groups should filter unmatched start nodes.");
    Assert(groups.SceneNodes.Count == 1, "Explorer groups should include matching scene nodes.");
    Assert(groups.DialogueNodes.Count == 1, "Explorer groups should include matching dialogue nodes.");
    Assert(groups.TotalCount == 2, "Explorer group total should count all matching nodes.");

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var refreshBody = ExtractMethodBody(source, "private void RefreshExplorer()");
    var addGroupBody = ExtractMethodBody(source, "private void AddNodeGroup");
    var keyDownBody = ExtractMethodBody(source, "private void ProjectSearchBox_KeyDown");
    var firstFilteredBody = ExtractMethodBody(
        source,
        "private NovelNode? FindFirstFilteredNode");

    Assert(
        refreshBody.Contains("BuildProjectExplorerGroups(_project.Nodes, query)", StringComparison.Ordinal),
        "Project explorer refresh should group filtered nodes in one pass.");
    Assert(
        !refreshBody.Contains("FilteredNodes(query).ToList()", StringComparison.Ordinal),
        "Project explorer refresh should not allocate an intermediate matched node list.");
    Assert(
        !addGroupBody.Contains(".Where(", StringComparison.Ordinal)
            && !addGroupBody.Contains(".ToList()", StringComparison.Ordinal),
        "Project explorer node groups should not re-filter each section.");
    Assert(
        keyDownBody.Contains("var node = FindFirstFilteredNode(query);", StringComparison.Ordinal)
            && !keyDownBody.Contains("FilteredNodes(query).FirstOrDefault()", StringComparison.Ordinal),
        "Project explorer Enter search should use a direct first-match lookup.");
    Assert(
        firstFilteredBody.Contains("foreach (var node in _project.Nodes)", StringComparison.Ordinal)
            && firstFilteredBody.Contains("NodeMatchesSearch(node, query)", StringComparison.Ordinal)
            && !firstFilteredBody.Contains(".Where(", StringComparison.Ordinal)
            && !firstFilteredBody.Contains(".FirstOrDefault(", StringComparison.Ordinal),
        "Project explorer first-match lookup should avoid LINQ iterators.");
    Assert(
        !source.Contains("FilteredNodes(string query)", StringComparison.Ordinal),
        "Project explorer should not keep the old filtered-node iterator helper.");
}

static void ProjectExplorerSelectionUsesSyncOnlyPath()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var selectionBody = ExtractMethodBody(source, "private void HandleGraphSelection");
    var syncOnlyBody = ExtractMethodBody(source, "private void RefreshExplorerSelection");

    Assert(
        selectionBody.Contains("RefreshExplorerSelection();", StringComparison.Ordinal),
        "Graph selection should use the lightweight project explorer selection path.");
    Assert(
        !selectionBody.Contains("RefreshExplorer();", StringComparison.Ordinal),
        "Graph selection should not recompute the project explorer stamp.");
    Assert(
        syncOnlyBody.Contains("SyncProjectTreeSelection(Graph.SelectedNodeId);", StringComparison.Ordinal),
        "Selection-only explorer refresh should reuse the existing node item lookup.");
    Assert(
        syncOnlyBody.Contains("if (ProjectTree.Items.Count == 0)", StringComparison.Ordinal)
            && syncOnlyBody.Contains("RefreshExplorer();", StringComparison.Ordinal),
        "Selection-only explorer refresh should fall back to a full build when the tree is empty.");
}

static void ProjectExplorerSelectionSkipsUnchangedSync()
{
    Assert(
        !MainWindow.ShouldSyncProjectTreeSelection("scene", "scene", selectedItemIsSelected: true),
        "Project explorer selection sync should skip an already selected node.");
    Assert(
        MainWindow.ShouldSyncProjectTreeSelection("scene", "scene", selectedItemIsSelected: false),
        "Project explorer selection sync should repair stale item selection state.");
    Assert(
        MainWindow.ShouldSyncProjectTreeSelection("scene", "dialogue", selectedItemIsSelected: true),
        "Project explorer selection sync should switch to a different requested node.");
    Assert(
        MainWindow.ShouldSyncProjectTreeSelection(null, "scene", selectedItemIsSelected: true),
        "Project explorer selection sync should clear an existing selection.");
    Assert(
        !MainWindow.ShouldSyncProjectTreeSelection(null, null, selectedItemIsSelected: false),
        "Project explorer selection sync should skip when no node and no item are selected.");
}

static void NodePropertyPanelStampTracksVisibleState()
{
    var baselineProject = NovelProject.CreateDefault();
    var baselineNode = baselineProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Default scene node was not found.");
    var baseline = MainWindow.CreateNodePropertyPanelStamp(
        baselineProject,
        baselineNode);

    var movedProject = ProjectSerializer.FromJson(ProjectSerializer.ToJson(baselineProject));
    var movedNode = movedProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Moved scene node was not found.");
    movedNode.X += 120;
    movedNode.Y += 80;
    Assert(
        baseline == MainWindow.CreateNodePropertyPanelStamp(movedProject, movedNode),
        "Node property panel stamp should ignore position-only changes.");

    var changedNodeProject = ProjectSerializer.FromJson(ProjectSerializer.ToJson(baselineProject));
    var changedNode = changedNodeProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Changed scene node was not found.");
    changedNode.Title += " updated";
    Assert(
        baseline != MainWindow.CreateNodePropertyPanelStamp(
            changedNodeProject,
            changedNode),
        "Node property panel stamp should track selected node text fields.");

    var changedTargetProject = ProjectSerializer.FromJson(ProjectSerializer.ToJson(baselineProject));
    var changedTargetScene = changedTargetProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Scene target source was not found.");
    var changedTarget = changedTargetProject.FindNode("dialogue-1")
        ?? throw new InvalidOperationException("Dialogue target was not found.");
    changedTarget.Title += " updated";
    Assert(
        baseline != MainWindow.CreateNodePropertyPanelStamp(
            changedTargetProject,
            changedTargetScene),
        "Node property panel stamp should track output target titles.");

    var changedTransitionProject = ProjectSerializer.FromJson(ProjectSerializer.ToJson(baselineProject));
    var changedTransitionNode = changedTransitionProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Transition source node was not found.");
    changedTransitionNode.Outputs[0].TransitionSound = "@click";
    changedTransitionNode.Outputs[0].FadeDurationMs = 700;
    Assert(
        baseline != MainWindow.CreateNodePropertyPanelStamp(
            changedTransitionProject,
            changedTransitionNode),
        "Node property panel stamp should track visible transition settings.");

    var changedAssetsProject = ProjectSerializer.FromJson(ProjectSerializer.ToJson(baselineProject));
    var changedAssetsNode = changedAssetsProject.FindNode("scene-1")
        ?? throw new InvalidOperationException("Asset scene node was not found.");
    changedAssetsProject.Assets.Add(new NovelAsset
    {
        Id = "mount_fuji",
        Kind = AssetKind.Image,
        Folder = "backgrounds",
        Path = "files/backgrounds/MountFuji.jpg",
    });
    Assert(
        baseline == MainWindow.CreateNodePropertyPanelStamp(
            changedAssetsProject,
            changedAssetsNode),
        "Node property panel stamp should leave asset picker inputs to explicit cache invalidation.");
}

static void NodePropertyPanelUsesGraphSelectedNodeCache()
{
    var windowSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var graphSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var refreshBody = ExtractMethodBody(windowSource, "private void RefreshProperties");
    var applyBody = ExtractMethodBody(windowSource, "private bool ApplyProperties");
    var characterSelectionBody = ExtractMethodBody(
        windowSource,
        "private void CharactersGrid_SelectionChanged");
    var outputSelectionBody = ExtractMethodBody(
        windowSource,
        "private void OutputsGrid_SelectionChanged");

    Assert(
        graphSource.Contains("public NovelNode? SelectedNode => FindNode(SelectedNodeId);", StringComparison.Ordinal),
        "GraphSurface should expose selected nodes through its cached node lookup.");
    Assert(
        refreshBody.Contains("var node = Graph.SelectedNode;", StringComparison.Ordinal),
        "Node property refresh should use GraphSurface selected-node cache.");
    Assert(
        applyBody.Contains("var node = Graph.SelectedNode;", StringComparison.Ordinal),
        "Node property apply should use GraphSurface selected-node cache.");
    Assert(
        !refreshBody.Contains("_project.FindNode(Graph.SelectedNodeId)", StringComparison.Ordinal)
            && !applyBody.Contains("_project.FindNode(Graph.SelectedNodeId)", StringComparison.Ordinal),
        "Hot node property paths should not linearly search the selected project node.");
    Assert(
        characterSelectionBody.Contains("var node = Graph.SelectedNode;", StringComparison.Ordinal)
            && outputSelectionBody.Contains("var node = Graph.SelectedNode;", StringComparison.Ordinal),
        "Node property selection handlers should use GraphSurface selected-node cache.");
    Assert(
        !characterSelectionBody.Contains("_project.FindNode(Graph.SelectedNodeId)", StringComparison.Ordinal)
            && !outputSelectionBody.Contains("_project.FindNode(Graph.SelectedNodeId)", StringComparison.Ordinal),
        "Node property selection handlers should not linearly search the selected project node.");
}

static void NodePropertyPanelUsesCachedTargetTitleLookup()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var refreshBody = ExtractMethodBody(source, "private void RefreshProperties");
    var stampBody = ExtractMethodBody(
        source,
        "internal static NodePropertyPanelStamp CreateNodePropertyPanelStamp");
    var outputLookupBody = ExtractMethodBody(
        source,
        "private static IReadOnlyDictionary<string, string> BuildOutputTargetTitleLookup");
    var lookupBody = ExtractMethodBody(
        source,
        "private static IReadOnlyDictionary<string, string> BuildNodeTitleLookup");
    var resolveBody = ExtractMethodBody(
        source,
        "private static string ResolveNodeTitle");
    var characterViewsBody = ExtractMethodBody(
        source,
        "private static List<CharacterView> CreateCharacterViews");
    var outputViewsBody = ExtractMethodBody(
        source,
        "private static List<OutputView> CreateOutputViews");

    Assert(
        refreshBody.Contains(
            "var nodeTitlesById = BuildOutputTargetTitleLookup(_project, node);",
            StringComparison.Ordinal),
        "Node property refresh should build the output target title lookup before stamping.");
    Assert(
        refreshBody.Contains(
            "CreateNodePropertyPanelStamp(_project, node, nodeTitlesById)",
            StringComparison.Ordinal),
        "Node property refresh should pass the target title lookup into the stamp.");
    Assert(
        !refreshBody.Contains("BuildNodeTitleLookup(_project)", StringComparison.Ordinal),
        "Node property refresh should not build the target title lookup twice.");
    Assert(
        stampBody.Contains(
            "nodeTitlesById ??= BuildOutputTargetTitleLookup(project, node);",
            StringComparison.Ordinal),
        "Node property stamp should reuse a provided target title lookup.");
    Assert(
        outputLookupBody.Contains("TargetNodeId is not null", StringComparison.Ordinal)
            && outputLookupBody.Contains("EmptyNodeTitleLookup", StringComparison.Ordinal),
        "Output target title lookup should skip full graph indexing when no outputs are connected.");
    Assert(
        !refreshBody.Contains("FindNode(output.TargetNodeId)", StringComparison.Ordinal)
            && !stampBody.Contains("FindNode(output.TargetNodeId)", StringComparison.Ordinal),
        "Node property output rows should not linearly find target nodes per output.");
    Assert(
        refreshBody.Contains("CharactersGrid.ItemsSource = CreateCharacterViews(node.Characters);", StringComparison.Ordinal)
            && refreshBody.Contains("OutputsGrid.ItemsSource = CreateOutputViews(", StringComparison.Ordinal)
            && !refreshBody.Contains(".Select(", StringComparison.Ordinal)
            && !refreshBody.Contains(".ToList(", StringComparison.Ordinal),
        "Node property refresh should delegate row creation without LINQ pipelines.");
    Assert(
        characterViewsBody.Contains("var views = new List<CharacterView>(characters.Count);", StringComparison.Ordinal)
            && characterViewsBody.Contains("for (var index = 0; index < characters.Count; index++)", StringComparison.Ordinal)
            && !characterViewsBody.Contains(".Select(", StringComparison.Ordinal)
            && !characterViewsBody.Contains(".ToList(", StringComparison.Ordinal),
        "Node character rows should be built in one direct pass.");
    Assert(
        outputViewsBody.Contains("var views = new List<OutputView>(outputs.Count);", StringComparison.Ordinal)
            && outputViewsBody.Contains("for (var index = 0; index < outputs.Count; index++)", StringComparison.Ordinal)
            && outputViewsBody.Contains("ResolveNodeTitle(", StringComparison.Ordinal)
            && !outputViewsBody.Contains(".Select(", StringComparison.Ordinal)
            && !outputViewsBody.Contains(".ToList(", StringComparison.Ordinal),
        "Node output rows should be built in one direct pass with cached title lookup.");
    Assert(
        lookupBody.Contains("lookup[node.Id] = node.Title;", StringComparison.Ordinal),
        "Node target title lookup should index titles by node id.");
    Assert(
        resolveBody.Contains("nodeTitlesById.TryGetValue", StringComparison.Ordinal),
        "Node target title resolution should use the lookup dictionary.");
}

static void NodeAssetPickerCacheInvalidatesPropertyPanel()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var stampBody = ExtractMethodBody(
        source,
        "internal static NodePropertyPanelStamp CreateNodePropertyPanelStamp");
    var folderOptionsBody = ExtractMethodBody(
        source,
        "private List<NodeAssetFolderOption> GetCachedFolderOptions");
    var assetChoicesBody = ExtractMethodBody(
        source,
        "private List<NodeAssetChoice> GetCachedAssetChoices");
    var ensureBody = ExtractMethodBody(source, "private void EnsureAssetPickerCachesCurrent");
    var clearBody = ExtractMethodBody(source, "private void ClearNodeAssetPickerCaches");
    var resolveBody = ExtractMethodBody(source, "private NovelAsset? ResolveAssetChoice");
    var refreshAssetsBody = ExtractMethodBody(source, "private void RefreshAssets");
    var catalogChangeBody = ExtractMethodBody(
        source,
        "private void RefreshAssetCatalogAfterEditorChange");
    var clearIndex = catalogChangeBody.IndexOf(
        "ClearNodeAssetPickerCaches();",
        StringComparison.Ordinal);
    var markIndex = catalogChangeBody.IndexOf(
        "MarkDirty(refreshGraph: false);",
        StringComparison.Ordinal);
    var refreshIndex = catalogChangeBody.IndexOf(
        "RefreshAssets(syncFromDisk: false);",
        StringComparison.Ordinal);

    Assert(
        !stampBody.Contains("project.Assets", StringComparison.Ordinal)
            && !stampBody.Contains("project.AssetFolders", StringComparison.Ordinal),
        "Node property panel stamp should not scan the full asset catalog.");
    Assert(
        !ensureBody.Contains("CreateAssetPickerCacheStamp", StringComparison.Ordinal),
        "Node asset picker cache validation should not rescan assets during property refresh.");
    Assert(
        folderOptionsBody.Contains("foreach (var folder in _project.AssetFolders)", StringComparison.Ordinal)
            && folderOptionsBody.Contains("foreach (var asset in _project.Assets)", StringComparison.Ordinal)
            && folderOptionsBody.Contains("sortedFolders.Sort(StringComparer.CurrentCultureIgnoreCase)", StringComparison.Ordinal)
            && !folderOptionsBody.Contains(".GroupBy(", StringComparison.Ordinal)
            && !folderOptionsBody.Contains(".Concat(", StringComparison.Ordinal)
            && !folderOptionsBody.Contains(".Distinct(", StringComparison.Ordinal)
            && !folderOptionsBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !folderOptionsBody.Contains(".Select(", StringComparison.Ordinal),
        "Node asset folder options should be cached through direct passes over folders and assets.");
    Assert(
        assetChoicesBody.Contains("foreach (var asset in _project.Assets)", StringComparison.Ordinal)
            && assetChoicesBody.Contains("matchingAssets.Sort(", StringComparison.Ordinal)
            && assetChoicesBody.Contains("new(null, \"Не выбрано\")", StringComparison.Ordinal)
            && !assetChoicesBody.Contains(".Where(", StringComparison.Ordinal)
            && !assetChoicesBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !assetChoicesBody.Contains(".Select(", StringComparison.Ordinal)
            && !assetChoicesBody.Contains(".Prepend(", StringComparison.Ordinal),
        "Node asset choices should be cached through a direct filter and sort pass.");
    Assert(
        ensureBody.Contains("_nodeAssetByIdCache.Clear();", StringComparison.Ordinal)
            && ensureBody.Contains("_nodeAssetByIdCache[asset.Id] = asset;", StringComparison.Ordinal),
        "Node asset picker cache should build an id lookup with the other picker caches.");
    Assert(
        resolveBody.Contains("_nodeAssetByIdCache.TryGetValue", StringComparison.Ordinal)
            && !resolveBody.Contains("_project.FindAsset", StringComparison.Ordinal),
        "Node asset picker reference resolution should use the cached id lookup.");
    Assert(
        clearBody.Contains("_nodePropertyPanelStamp = null;", StringComparison.Ordinal),
        "Asset picker cache invalidation should force the property panel to rebuild.");
    Assert(
        clearBody.Contains("_nodeAssetByIdCache.Clear();", StringComparison.Ordinal),
        "Asset picker cache invalidation should clear the id lookup.");
    Assert(
        clearIndex >= 0 && markIndex > clearIndex && refreshIndex > markIndex,
        "Editor asset catalog changes should invalidate picker caches before dirty refreshes.");
    Assert(
        !refreshAssetsBody.Contains("ClearNodeAssetPickerCaches();", StringComparison.Ordinal)
            && refreshAssetsBody.Contains("if (externalFileEvent || filesChanged)", StringComparison.Ordinal),
        "Plain asset refresh should keep picker caches warm and only refresh properties for disk changes.");
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

static void MainMenuPropertyGuardSeparatesRenderedChanges()
{
    var element = new MainMenuElement
    {
        Id = "start",
        Action = MainMenuAction.NewGame,
        Text = "Start",
        Image = "start.png",
        X = 20,
        Y = 30,
        Width = 200,
        Height = 50,
        FontSize = 24,
        Foreground = "#FFFFFF",
        Background = "#222222",
        Border = "#333333",
        CustomStyleCode = "font-weight: bold",
    };

    Assert(
        !MainMenuEditorWindow.HasElementPropertyChanges(
            element,
            element.Text,
            element.Image,
            element.Action,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.FontSize,
            element.Foreground,
            element.Background,
            element.Border,
            element.CustomStyleCode),
        "Unchanged main menu element properties should be skipped.");
    Assert(
        MainMenuEditorWindow.HasElementPropertyChanges(
            element,
            element.Text,
            element.Image,
            MainMenuAction.LoadGame,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.FontSize,
            element.Foreground,
            element.Background,
            element.Border,
            element.CustomStyleCode),
        "Action-only main menu changes should still update the model.");
    Assert(
        !MainMenuEditorWindow.HasRenderedElementPropertyChanges(
            element,
            element.Text,
            element.Image,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.FontSize,
            element.Foreground,
            element.Background,
            element.Border,
            element.CustomStyleCode),
        "Unchanged rendered main menu properties should not refresh the stage.");
    Assert(
        MainMenuEditorWindow.HasRenderedElementPropertyChanges(
            element,
            "Continue",
            element.Image,
            element.X,
            element.Y,
            element.Width,
            element.Height,
            element.FontSize,
            element.Foreground,
            element.Background,
            element.Border,
            element.CustomStyleCode),
        "Rendered text changes should refresh the main menu stage.");
}

static void MainMenuTextPropertiesUseDebouncedApply()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainMenuEditorWindow.cs"));
    var buildProperties = ExtractMethodBody(source, "private UIElement BuildProperties");
    var scheduleBody = ExtractMethodBody(source, "private void ScheduleApplyProperties");
    var buildFooter = ExtractMethodBody(source, "private UIElement BuildFooter");
    var selectElement = ExtractMethodBody(source, "private void SelectElement");
    var deleteSelected = ExtractMethodBody(source, "private void DeleteSelected");

    Assert(
        buildProperties.Contains(
            "textBox.TextChanged += (_, _) => ScheduleApplyProperties();",
            StringComparison.Ordinal),
        "Main menu text fields should schedule debounced property application.");
    Assert(
        !buildProperties.Contains(
            "textBox.TextChanged += (_, _) => ApplyProperties();",
            StringComparison.Ordinal),
        "Main menu text fields should not apply properties on every keystroke.");
    Assert(
        buildProperties.Contains(
            "_actionBox.SelectionChanged += (_, _) => FlushPendingPropertyChanges();",
            StringComparison.Ordinal),
        "Main menu action changes should flush pending text edits immediately.");
    Assert(
        scheduleBody.Contains("_propertyApplyTimer.Stop();", StringComparison.Ordinal)
            && scheduleBody.Contains("_propertyApplyTimer.Start();", StringComparison.Ordinal),
        "Main menu debounced property application should restart its timer.");
    Assert(
        buildFooter.Contains("FlushPendingPropertyChanges();", StringComparison.Ordinal),
        "Saving the main menu should flush pending property edits.");
    Assert(
        selectElement.Contains("FlushPendingPropertyChanges();", StringComparison.Ordinal),
        "Changing selected main menu element should flush pending property edits.");
    Assert(
        deleteSelected.Contains("_propertyApplyTimer.Stop();", StringComparison.Ordinal),
        "Deleting a main menu element should cancel pending property edits.");
}

static void MainMenuPropertyPanelStampTracksFields()
{
    var element = new MainMenuElement
    {
        Id = "start",
        Action = MainMenuAction.NewGame,
        Text = "Start",
        Image = "start.png",
        X = 20,
        Y = 30,
        Width = 200,
        Height = 50,
        FontSize = 24,
        Foreground = "#FFFFFF",
        Background = "#222222",
        Border = "#333333",
        CustomStyleCode = "font-weight: bold",
    };
    var baseline = MainMenuEditorWindow.CreatePropertyPanelStamp(element);

    Assert(
        baseline == MainMenuEditorWindow.CreatePropertyPanelStamp(element.Clone()),
        "Main menu property panel stamp should be stable for identical fields.");

    var moved = element.Clone();
    moved.X += 10;
    Assert(
        baseline != MainMenuEditorWindow.CreatePropertyPanelStamp(moved),
        "Main menu property panel stamp should track coordinate fields.");

    var actionChanged = element.Clone();
    actionChanged.Action = MainMenuAction.LoadGame;
    Assert(
        baseline != MainMenuEditorWindow.CreatePropertyPanelStamp(actionChanged),
        "Main menu property panel stamp should track action fields.");

    Assert(
        MainMenuEditorWindow.CreatePropertyPanelStamp(null)
            == MainMenuEditorWindow.CreatePropertyPanelStamp(null),
        "Empty main menu property panel stamp should be stable.");
}

static void MainMenuElementListStampTracksVisibleRows()
{
    var elements = new List<MainMenuElement>
    {
        new()
        {
            Id = "start",
            Text = "Start",
            X = 20,
            Y = 30,
            Width = 200,
            Height = 50,
        },
    };
    var baseline = MainMenuEditorWindow.CreateElementListStamp(elements);

    elements[0].X += 40;
    elements[0].Y += 20;
    elements[0].Width += 10;
    elements[0].Height += 5;
    Assert(
        baseline == MainMenuEditorWindow.CreateElementListStamp(elements),
        "Main menu element list stamp should ignore layout-only changes.");

    elements[0].Text = "Continue";
    Assert(
        baseline != MainMenuEditorWindow.CreateElementListStamp(elements),
        "Main menu element list stamp should track visible text.");

    elements.Add(new MainMenuElement { Id = "load", Text = "Load" });
    Assert(
        baseline != MainMenuEditorWindow.CreateElementListStamp(elements),
        "Main menu element list stamp should track added rows.");
}

static void MainMenuStageStampTracksRenderedState()
{
    var design = new MainMenuDesign();
    design.Elements.Clear();
    var element = new MainMenuElement
    {
        Id = "start",
        Kind = MainMenuElementKind.Button,
        Action = MainMenuAction.NewGame,
        Text = "Start",
        X = 20,
        Y = 30,
        Width = 200,
        Height = 50,
    };
    design.Elements.Add(element);
    var baseline = MainMenuEditorWindow.CreateStageStamp(design, "start");

    element.Action = MainMenuAction.LoadGame;
    Assert(
        baseline == MainMenuEditorWindow.CreateStageStamp(design, "start"),
        "Main menu stage stamp should ignore non-rendered action changes.");

    Assert(
        baseline != MainMenuEditorWindow.CreateStageStamp(design, null),
        "Main menu stage stamp should track selected element highlight.");

    element.X += 12;
    Assert(
        baseline != MainMenuEditorWindow.CreateStageStamp(design, "start"),
        "Main menu stage stamp should track rendered layout changes.");

    element.X -= 12;
    element.Text = "Continue";
    Assert(
        baseline != MainMenuEditorWindow.CreateStageStamp(design, "start"),
        "Main menu stage stamp should track rendered text changes.");

    element.Text = "Start";
    design.Background = "@main_menu_bg";
    Assert(
        baseline != MainMenuEditorWindow.CreateStageStamp(design, "start"),
        "Main menu stage stamp should track background changes.");
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

static void SceneEditorCharacterListStampTracksVisibleRows()
{
    var characters = new List<CharacterPlacement>
    {
        new()
        {
            Id = "hero",
            Name = "Hero",
            HasCustomTransform = true,
            X = 100,
            Y = 200,
            Scale = 1,
            Rotation = 0,
        },
    };
    var baseline = SceneEditorWindow.CreateCharacterListStamp(characters);

    characters[0].X += 40;
    characters[0].Y += 20;
    characters[0].Scale = 1.2;
    characters[0].Rotation = 15;
    Assert(
        baseline == SceneEditorWindow.CreateCharacterListStamp(characters),
        "Scene character list stamp should ignore transform-only changes.");

    characters[0].Name = "Hero Updated";
    Assert(
        baseline != SceneEditorWindow.CreateCharacterListStamp(characters),
        "Scene character list stamp should track visible names.");

    characters.Add(new CharacterPlacement { Id = "sidekick", Name = "Sidekick" });
    Assert(
        baseline != SceneEditorWindow.CreateCharacterListStamp(characters),
        "Scene character list stamp should track added rows.");
}

static void PreviewPlaybackAvoidsTransientLists()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "PreviewWindow.xaml.cs"));
    var constructorBody = ExtractMethodBody(source, "public PreviewWindow(");
    var closedBody = ExtractMethodBody(source, "private void PreviewWindow_Closed");
    var renderBody = ExtractMethodBody(source, "private void RenderNode");
    var debugBody = ExtractMethodBody(source, "private void RefreshDebugState");
    var debugCharactersBody = ExtractMethodBody(source, "private string FormatDebugCharacters");
    var debugVariablesBody = ExtractMethodBody(source, "private string FormatDebugVariables");
    var trimBody = ExtractMethodBody(source, "private void TrimVoicePools");
    var hasActiveBody = ExtractMethodBody(
        source,
        "private bool HasActiveVoicePlayer");
    var choicesBody = ExtractMethodBody(source, "private void SetChoicesEnabled");
    var resolveVoiceBody = ExtractMethodBody(source, "private CharacterVoice? ResolveVoice");
    var resolveVoicePathsBody = ExtractMethodBody(
        source,
        "private List<string> ResolveVoiceSoundPaths");
    var typeDialogueBody = ExtractMethodBody(
        source,
        "private async Task TypeDialogueAsync");
    var stopBody = ExtractMethodBody(source, "private void StopAllVoicePlayers");
    var pauseBody = ExtractMethodBody(source, "private void SetPaused");
    var settingsBody = ExtractMethodBody(source, "private void ApplyRuntimeSettings");
    var keyBody = ExtractMethodBody(source, "private void Window_KeyDown");

    Assert(
        trimBody.Contains("HasActiveVoicePlayer(pool)", StringComparison.Ordinal)
            && !trimBody.Contains(".Any(", StringComparison.Ordinal),
        "Voice pool trimming should avoid LINQ delegates on active player checks.");
    Assert(
        hasActiveBody.Contains("foreach (var player in pool)", StringComparison.Ordinal)
            && hasActiveBody.Contains("_activeVoicePlayers.Contains(player)", StringComparison.Ordinal),
        "Active voice player checks should use a direct loop.");
    Assert(
        choicesBody.Contains("foreach (UIElement child in ChoicesPanel.Children)", StringComparison.Ordinal)
            && !choicesBody.Contains("OfType<Button>", StringComparison.Ordinal),
        "Choice availability toggles should avoid LINQ iterators.");
    Assert(
        resolveVoiceBody.Contains("ResolveVoiceSoundPaths(character)", StringComparison.Ordinal)
            && resolveVoiceBody.Contains("foreach (var candidate in _player.State.CurrentCharacters)", StringComparison.Ordinal)
            && !resolveVoiceBody.Contains(".FirstOrDefault(", StringComparison.Ordinal)
            && !resolveVoiceBody.Contains(".Select(ResolveAsset)", StringComparison.Ordinal)
            && !resolveVoiceBody.Contains(".Distinct(", StringComparison.Ordinal),
        "Voice resolution should delegate runtime path collection without LINQ chains.");
    Assert(
        debugBody.Contains("FormatDebugCharacters()", StringComparison.Ordinal)
            && debugBody.Contains("FormatDebugVariables()", StringComparison.Ordinal)
            && !debugBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !debugBody.Contains(".Select(", StringComparison.Ordinal),
        "Preview debug refresh should delegate state formatting without LINQ chains.");
    Assert(
        debugCharactersBody.Contains("foreach (var character in _player.State.CurrentCharacters)", StringComparison.Ordinal)
            && debugVariablesBody.Contains("variables.Sort(", StringComparison.Ordinal)
            && debugVariablesBody.Contains("foreach (var pair in variables)", StringComparison.Ordinal)
            && !debugVariablesBody.Contains(".OrderBy(", StringComparison.Ordinal)
            && !debugVariablesBody.Contains(".Select(", StringComparison.Ordinal),
        "Preview debug state formatting should use direct loops.");
    Assert(
        resolveVoicePathsBody.Contains("foreach (var reference in character.GetVoiceSounds())", StringComparison.Ordinal)
            && resolveVoicePathsBody.Contains("sounds.Contains(sound, StringComparer.OrdinalIgnoreCase)", StringComparison.Ordinal),
        "Voice sound path collection should preserve first-seen unique paths in a direct loop.");
    Assert(
        stopBody.Contains("foreach (var pool in _voicePlayerPools.Values)", StringComparison.Ordinal)
            && stopBody.Contains("foreach (var player in pool)", StringComparison.Ordinal)
            && !stopBody.Contains("SelectMany", StringComparison.Ordinal),
        "Stopping voice players should avoid SelectMany iterator chains.");
    Assert(
        settingsBody.Contains("foreach (var pool in _voicePlayerPools.Values)", StringComparison.Ordinal)
            && settingsBody.Contains("foreach (var player in pool)", StringComparison.Ordinal)
            && !settingsBody.Contains("SelectMany", StringComparison.Ordinal),
        "Applying runtime settings should avoid SelectMany iterator chains.");
    Assert(
        !pauseBody.Contains("_activeVoicePlayers.ToList()", StringComparison.Ordinal),
        "Pause toggles should not allocate a temporary active player list.");
    Assert(
        keyBody.Contains("FindChoiceButton(index, out var buttonCount)", StringComparison.Ordinal)
            && !keyBody.Contains("ChoicesPanel.Children.OfType<Button>().ToList()", StringComparison.Ordinal),
        "Choice shortcuts should find buttons without allocating a temporary list.");
    Assert(
        typeDialogueBody.Contains("catch (OperationCanceledException)", StringComparison.Ordinal)
            && typeDialogueBody.Contains("catch (Exception error)", StringComparison.Ordinal)
            && typeDialogueBody.Contains("if (version == _typingVersion)", StringComparison.Ordinal)
            && typeDialogueBody.Contains("DialogueText.Text = $\"Ошибка показа реплики: {error.Message}\";", StringComparison.Ordinal)
            && typeDialogueBody.Contains("ShowChoices();", StringComparison.Ordinal),
        "Preview dialogue typing should surface current-node errors instead of leaving fire-and-forget task faults unhandled.");
    Assert(
        constructorBody.Contains("Closed += PreviewWindow_Closed;", StringComparison.Ordinal)
            && closedBody.Contains("dialogueCts?.Cancel();", StringComparison.Ordinal)
            && closedBody.Contains("_musicPlayer.Close();", StringComparison.Ordinal)
            && closedBody.Contains("_transitionPlayer.Close();", StringComparison.Ordinal)
            && closedBody.Contains("StopAllVoicePlayers(close: true);", StringComparison.Ordinal),
        "Preview windows should close media resources and cancel active typing when the window closes.");
    Assert(
        renderBody.Contains("var previousDialogueCts = _dialogueCts;", StringComparison.Ordinal)
            && renderBody.Contains("previousDialogueCts?.Cancel();", StringComparison.Ordinal)
            && renderBody.Contains("_dialogueCts = new CancellationTokenSource();", StringComparison.Ordinal)
            && renderBody.Contains("TypeDialogueAsync(node, _dialogueCts, ++_typingVersion)", StringComparison.Ordinal)
            && !renderBody.Contains("_dialogueCts?.Cancel();", StringComparison.Ordinal),
        "Rendering a new preview node should replace the typing CTS through one explicit cancellation path.");
    Assert(
        typeDialogueBody.Contains("var cancellationToken = cancellationTokenSource.Token;", StringComparison.Ordinal)
            && typeDialogueBody.Contains("ReferenceEquals(_dialogueCts, cancellationTokenSource)", StringComparison.Ordinal)
            && typeDialogueBody.Contains("cancellationTokenSource.Dispose();", StringComparison.Ordinal),
        "Preview dialogue typing should own and dispose its CTS after the async loop exits.");
}

static void GameProcessStartIsRegisteredBeforeLaunch()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var startBody = ExtractMethodBody(source, "private void StartGameProcess");
    var exitedBody = ExtractMethodBody(source, "private void GameProcess_Exited");
    var assignIndex = startBody.IndexOf("_gameProcess = process;", StringComparison.Ordinal);
    var startIndex = startBody.IndexOf("process.Start()", StringComparison.Ordinal);
    var clearIndex = startBody.IndexOf("_gameProcess = null;", startIndex, StringComparison.Ordinal);
    var disposeIndex = startBody.IndexOf("process.Dispose();", startIndex, StringComparison.Ordinal);

    Assert(
        assignIndex >= 0 && startIndex > assignIndex,
        "Game process should be stored before Start so fast Exited events can match the active process.");
    Assert(
        clearIndex > startIndex && clearIndex < disposeIndex,
        "Failed game starts should clear the stored process before disposal.");
    Assert(
        exitedBody.Contains("ReferenceEquals(process, _gameProcess)", StringComparison.Ordinal)
            && exitedBody.Contains("_gameProcess = null;", StringComparison.Ordinal)
            && exitedBody.Contains("process.Dispose();", StringComparison.Ordinal),
        "Game process exit handling should only clear and dispose the active process.");
}

static void SilentGameProcessStopDisposesImmediately()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var stopBody = ExtractMethodBody(source, "private void StopGameProcess");
    var silentIndex = stopBody.IndexOf("if (silent)", StringComparison.Ordinal);
    var clearIndex = stopBody.IndexOf("_gameProcess = null;", silentIndex, StringComparison.Ordinal);
    var killIndex = stopBody.IndexOf("process.Kill(entireProcessTree: true);", silentIndex, StringComparison.Ordinal);
    var disposeIndex = stopBody.IndexOf("process.Dispose();", silentIndex, StringComparison.Ordinal);
    var returnIndex = stopBody.IndexOf("return;", silentIndex, StringComparison.Ordinal);
    var normalStopIndex = stopBody.LastIndexOf(
        "StatusText.Text = \"Остановка игры...\";",
        StringComparison.Ordinal);

    Assert(
        stopBody.Contains("var process = _gameProcess;", StringComparison.Ordinal)
            && stopBody.Contains("if (process is not { HasExited: false })", StringComparison.Ordinal),
        "Game process stop should capture the process once before checking running state.");
    Assert(
        silentIndex >= 0
            && clearIndex > silentIndex
            && killIndex > clearIndex
            && disposeIndex > killIndex
            && returnIndex > disposeIndex,
        "Silent game process stop should clear, kill, dispose, and return without waiting for Exited.");
    Assert(
        normalStopIndex > returnIndex,
        "Normal game process stop should keep the visible stopping status outside the silent cleanup branch.");
}

static void MainWindowClosingStopsEditorTimers()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var closingBody = ExtractMethodBody(source, "private void MainWindow_Closing");
    var stopTimersBody = ExtractMethodBody(source, "private void StopEditorTimers");

    Assert(
        closingBody.Contains("StopEditorTimers();", StringComparison.Ordinal)
            && closingBody.IndexOf("StopEditorTimers();", StringComparison.Ordinal)
                < closingBody.IndexOf("_filesWatcher?.Dispose();", StringComparison.Ordinal),
        "Main window closing should stop timers before disposing editor resources.");

    var expectedStops = new[]
    {
        "_codeAnalysisTimer.Stop();",
        "_autoSaveTimer.Stop();",
        "_filesRefreshTimer.Stop();",
        "_diagnosticsTimer.Stop();",
        "_projectExplorerSearchTimer.Stop();",
        "_assetSearchTimer.Stop();",
    };
    foreach (var expected in expectedStops)
    {
        Assert(
            stopTimersBody.Contains(expected, StringComparison.Ordinal),
            $"Main window timer cleanup is missing {expected}");
    }
}

static void GraphSurfaceShortcutsUseExactModifiers()
{
    Assert(
        GraphSurface.IsDeleteShortcut(Key.Delete, ModifierKeys.None),
        "Plain Delete should delete the selected graph node.");
    Assert(
        !GraphSurface.IsDeleteShortcut(Key.Delete, ModifierKeys.Shift),
        "Modified Delete should be left to the focused control.");
    Assert(
        GraphSurface.IsDuplicateShortcut(Key.D, ModifierKeys.Control),
        "Ctrl+D should duplicate the selected graph node.");
    Assert(
        !GraphSurface.IsDuplicateShortcut(Key.D, ModifierKeys.None),
        "Plain D should not duplicate graph nodes.");
    Assert(
        !GraphSurface.IsDuplicateShortcut(
            Key.D,
            ModifierKeys.Control | ModifierKeys.Shift),
        "Extra modifiers should not duplicate graph nodes.");
    Assert(
        GraphSurface.IsCenterShortcut(Key.Home, ModifierKeys.None),
        "Plain Home should center the graph.");
    Assert(
        !GraphSurface.IsCenterShortcut(Key.Home, ModifierKeys.Control),
        "Modified Home should be left to the focused control.");
}

static void GraphDragMovementSkipsMicroDeltas()
{
    Assert(
        !GraphSurface.HasMeaningfulDragPositionChange(10, 20, 10.1, 20.1),
        "Graph node micro drag should not refresh rendering or mark the project dirty.");
    Assert(
        GraphSurface.HasMeaningfulDragPositionChange(10, 20, 10.5, 20),
        "Graph node drag at the render threshold should update X.");
    Assert(
        GraphSurface.HasMeaningfulDragPositionChange(10, 20, 10, 20.5),
        "Graph node drag at the render threshold should update Y.");
}

static void GraphNodeDragDefersHitCacheRebuild()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var dragBody = ExtractMethodBody(source, "protected override void OnMouseMove");
    var mouseUpBody = ExtractMethodBody(source, "protected override void OnMouseUp");
    var nodeDragIndex = dragBody.IndexOf("node.X = nextX;", StringComparison.Ordinal);
    var nodeDragRenderIndex = dragBody.IndexOf("RequestRender();", nodeDragIndex);
    var movedIndex = mouseUpBody.IndexOf("if (_dragMoved)", StringComparison.Ordinal);
    var invalidateIndex = mouseUpBody.IndexOf("InvalidateHitTestCache();", movedIndex);
    var projectChangedIndex = mouseUpBody.IndexOf("ProjectChanged?.Invoke", movedIndex);

    Assert(nodeDragIndex >= 0, "Graph node drag branch should update node coordinates.");
    Assert(
        nodeDragRenderIndex > nodeDragIndex,
        "Graph node drag should still queue a render after movement.");
    Assert(
        !dragBody.Contains("RequestRender(invalidateHitTests: true)", StringComparison.Ordinal),
        "Graph node drag should not rebuild hit-test state on every mouse move.");
    Assert(
        invalidateIndex > movedIndex && invalidateIndex < projectChangedIndex,
        "Graph node drag should invalidate hit-test state once when the drag ends.");
}

static void GraphSurfaceUsesCachedNodeLookup()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var helperBody = ExtractMethodBody(source, "private NovelNode? FindNode");
    var selectBody = ExtractMethodBody(source, "public void SelectNode");
    var dragBody = ExtractMethodBody(source, "protected override void OnMouseMove");
    var outputPortBody = ExtractMethodBody(
        source,
        "private GraphOutputPortHitArea? GetOutputPort");

    Assert(
        !source.Contains("Project.FindNode(", StringComparison.Ordinal),
        "GraphSurface should use its node lookup cache instead of linear project lookup.");
    Assert(
        helperBody.Contains("EnsureNodeLookup();", StringComparison.Ordinal)
            && helperBody.Contains("_nodesById.TryGetValue", StringComparison.Ordinal),
        "GraphSurface cached node helper should use the node dictionary.");
    Assert(
        selectBody.Contains("FindNode(nodeId)", StringComparison.Ordinal),
        "Graph selection should use the cached node helper.");
    Assert(
        dragBody.Contains("FindNode(_dragNodeId)", StringComparison.Ordinal),
        "Graph drag should use the cached node helper.");
    Assert(
        outputPortBody.Contains("FindNode(nodeId)", StringComparison.Ordinal),
        "Graph output-port rendering should use the cached node helper.");
}

static void GraphCenteringCalculatesBoundsInOnePass()
{
    var first = new NovelNode
    {
        Id = "first",
        Kind = NodeKind.Scene,
        X = -30,
        Y = 80,
    };
    var second = new NovelNode
    {
        Id = "second",
        Kind = NodeKind.Dialogue,
        X = 10,
        Y = -20,
    };
    second.Outputs.Add(new NodeOutput { Id = "left" });
    second.Outputs.Add(new NodeOutput { Id = "middle" });
    second.Outputs.Add(new NodeOutput { Id = "right" });

    Assert(
        GraphSurface.TryCalculateGraphBounds([first, second], out var bounds),
        "Graph bounds should be calculated for non-empty node lists.");
    Assert(Math.Abs(bounds.Left - -30) < 0.001, "Graph bounds left edge is wrong.");
    Assert(Math.Abs(bounds.Top - -20) < 0.001, "Graph bounds top edge is wrong.");
    Assert(Math.Abs(bounds.Right - 230) < 0.001, "Graph bounds right edge is wrong.");
    Assert(Math.Abs(bounds.Bottom - 235) < 0.001, "Graph bounds bottom edge is wrong.");
    Assert(
        !GraphSurface.TryCalculateGraphBounds([], out _),
        "Graph bounds should report empty input without work.");

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var centerBody = ExtractMethodBody(source, "public void CenterGraph");
    var boundsBody = ExtractMethodBody(
        source,
        "internal static bool TryCalculateGraphBounds");
    Assert(
        centerBody.Contains("TryCalculateGraphBounds(Project.Nodes", StringComparison.Ordinal),
        "CenterGraph should delegate graph bounds to the one-pass helper.");
    Assert(
        !centerBody.Contains(".Min(", StringComparison.Ordinal)
            && !centerBody.Contains(".Max(", StringComparison.Ordinal),
        "CenterGraph should not enumerate graph nodes through multiple Min/Max passes.");
    Assert(
        boundsBody.Contains("for (var index = 1; index < nodes.Count; index++)", StringComparison.Ordinal)
            && !boundsBody.Contains("nodes.Min(", StringComparison.Ordinal)
            && !boundsBody.Contains("nodes.Max(", StringComparison.Ordinal),
        "Graph bounds helper should use one indexed pass over nodes.");
}

static void ModalEditorsSkipHiddenGraphRefresh()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "MainWindow.xaml.cs"));
    var hiddenModalEditorMethods = new[]
    {
        "private void EditNodeScene",
        "private void EditMainMenu",
    };

    foreach (var method in hiddenModalEditorMethods)
    {
        var body = ExtractMethodBody(source, method);
        Assert(
            body.Contains("MarkDirty(refreshGraph: false);", StringComparison.Ordinal),
            $"{method} should dirty modal editor changes without refreshing the graph.");
    }
}

static void GraphInheritanceMenuCachesIncomingNodes()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var menuBody = ExtractMethodBody(source, "private MenuItem CreateInheritanceMenu");
    var resourceBody = ExtractMethodBody(
        source,
        "private MenuItem CreateInheritanceResourceMenu");
    var buildBody = ExtractMethodBody(
        source,
        "private Dictionary<string, List<NovelNode>> BuildIncomingNodesByTargetId");
    var incomingBody = ExtractMethodBody(
        source,
        "private static IReadOnlyList<NovelNode> GetIncomingNodes");
    var resolveBody = ExtractMethodBody(
        source,
        "private InheritanceSourceDescription? ResolveInheritanceSource");

    Assert(
        menuBody.Contains("BuildIncomingNodesByTargetId();", StringComparison.Ordinal),
        "Inheritance menu should build incoming node lookup once.");
    Assert(
        resourceBody.Contains("GetIncomingNodes(node, incomingNodesByTargetId)", StringComparison.Ordinal),
        "Inheritance resource menu should use cached incoming lookup.");
    Assert(
        buildBody.Contains("foreach (var node in Project.Nodes)", StringComparison.Ordinal)
            && buildBody.Contains("sources.Sort(", StringComparison.Ordinal),
        "Incoming lookup should scan the graph once and keep source ordering stable.");
    Assert(
        incomingBody.Contains("incomingNodesByTargetId.TryGetValue", StringComparison.Ordinal)
            && !incomingBody.Contains("Project.Nodes", StringComparison.Ordinal),
        "Incoming node reads should not rescan the graph.");
    Assert(
        resolveBody.Contains("GetIncomingNodes(node, incomingNodesByTargetId)", StringComparison.Ordinal),
        "Recursive inheritance resolution should reuse the cached incoming lookup.");
}

static void GraphInheritanceMenuSkipsUnchangedApply()
{
    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var body = ExtractMethodBody(source, "private void ApplyInheritance");
    var noChangeIndex = body.IndexOf(
        "if (NodeInheritsResource(node, resource))",
        StringComparison.Ordinal);
    var projectChangedIndex = body.IndexOf(
        "ProjectChanged?.Invoke(this, EventArgs.Empty);",
        StringComparison.Ordinal);

    Assert(
        noChangeIndex >= 0,
        "Inheritance menu should guard already inherited resources.");
    Assert(
        body.Contains("SelectNode(node.Id);", StringComparison.Ordinal),
        "Unchanged inheritance should still keep the context node selected.");
    Assert(
        projectChangedIndex >= 0 && noChangeIndex < projectChangedIndex,
        "Unchanged inheritance should return before dirtying the project.");
}

static void GraphInheritanceRenderSkipsSelectedNode()
{
    Assert(
        !GraphSurface.ShouldRenderAfterHiddenInheritanceApply("scene", "scene"),
        "Applying hidden inheritance to the selected node should not repaint the graph.");
    Assert(
        GraphSurface.ShouldRenderAfterHiddenInheritanceApply(null, "scene"),
        "Applying hidden inheritance should render when it selects a node.");
    Assert(
        GraphSurface.ShouldRenderAfterHiddenInheritanceApply("other", "scene"),
        "Applying hidden inheritance should render when selection changes.");

    var source = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "src",
        "NovelEngine.Editor",
        "GraphSurface.cs"));
    var body = ExtractMethodBody(source, "private void ApplyInheritance");
    Assert(
        body.Contains(
            "ShouldRenderAfterHiddenInheritanceApply(SelectedNodeId, node.Id)",
            StringComparison.Ordinal),
        "ApplyInheritance should avoid repainting already selected hidden changes.");
}

static void DirtyChangeGraphRefreshPolicySkipsGraphOriginatedChanges()
{
    Assert(
        !MainWindow.ShouldRefreshGraphForDirtyChange(originatedFromGraphSurface: true),
        "Graph-originated project changes already refreshed the graph surface.");
    Assert(
        MainWindow.ShouldRefreshGraphForDirtyChange(originatedFromGraphSurface: false),
        "External project changes should refresh the graph surface.");
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

static string FindRepositoryRoot()
{
    var directory = Directory.GetCurrentDirectory();
    while (!File.Exists(Path.Combine(directory, "NovelEngine.sln")))
    {
        directory = Directory.GetParent(directory)?.FullName
            ?? throw new DirectoryNotFoundException("NovelEngine.sln was not found.");
    }
    return directory;
}

static void AssertStyleSetters(
    XDocument document,
    string targetType,
    params string[] expectedProperties)
{
    var presentation = (XNamespace)"http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    var style = document
        .Descendants(presentation + "Style")
        .FirstOrDefault(candidate =>
            candidate.Attribute("TargetType")?.Value == targetType);

    Assert(style is not null, $"App style was not found: {targetType}.");

    var properties = style!
        .Elements(presentation + "Setter")
        .Select(setter => setter.Attribute("Property")?.Value)
        .Where(property => property is not null)
        .ToHashSet(StringComparer.Ordinal);
    foreach (var expected in expectedProperties)
    {
        Assert(
            properties.Contains(expected),
            $"App style {targetType} is missing setter {expected}.");
    }
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

static void AssertRectContains(Rect rect, Point point, string message)
{
    Assert(
        point.X >= rect.Left
            && point.X <= rect.Right
            && point.Y >= rect.Top
            && point.Y <= rect.Bottom,
        message);
}
