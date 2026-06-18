[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
Set-Location $repoRoot

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    Write-Host ""
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

Invoke-Step "Build solution" {
    dotnet build NovelEngine.sln
}
Invoke-Step "Run core tests" {
    dotnet run --no-build --project tests\NovelEngine.Core.Tests\NovelEngine.Core.Tests.csproj
}
Invoke-Step "Run editor tests" {
    dotnet run --no-build --project tests\NovelEngine.Editor.Tests\NovelEngine.Editor.Tests.csproj
}
Invoke-Step "Check Explorer context menu scripts" {
    $install = powershell -NoProfile -ExecutionPolicy Bypass -File tools\install-explorer-context-menu.ps1 -Describe | ConvertFrom-Json
    $expectedMenuText = -join @(
        [char]0x041E, [char]0x0442, [char]0x043A, [char]0x0440,
        [char]0x044B, [char]0x0442, [char]0x044C, [char]0x0020,
        [char]0x0441, [char]0x0020, [char]0x043F, [char]0x043E,
        [char]0x043C, [char]0x043E, [char]0x0449, [char]0x044C,
        [char]0x044E, [char]0x0020, [char]0x004E, [char]0x006F,
        [char]0x0076, [char]0x0065, [char]0x006C, [char]0x0020,
        [char]0x0045, [char]0x006E, [char]0x0067, [char]0x0069,
        [char]0x006E, [char]0x0065
    )
    if ($install.MenuText -ne $expectedMenuText) {
        throw 'Explorer install menu text changed.'
    }
    if ($install.Entries.Count -ne 3) {
        throw "Explorer install expected 3 entries, got $($install.Entries.Count)."
    }
    $expected = @{
        'HKCU:\Software\Classes\Directory\shell\NovelEngine.Open' = '%1'
        'HKCU:\Software\Classes\Directory\Background\shell\NovelEngine.Open' = '%V'
        'HKCU:\Software\Classes\SystemFileAssociations\.novel.json\shell\NovelEngine.Open' = '%1'
    }
    foreach ($entry in $install.Entries) {
        if (-not $expected.ContainsKey($entry.Key)) {
            throw "Unexpected Explorer install key: $($entry.Key)."
        }
        $argument = $expected[$entry.Key]
        if (-not $entry.Command.EndsWith(" `"$argument`"")) {
            throw "Explorer command for $($entry.Key) does not pass $argument."
        }
    }

    $uninstall = powershell -NoProfile -ExecutionPolicy Bypass -File tools\uninstall-explorer-context-menu.ps1 -Describe | ConvertFrom-Json
    foreach ($key in $expected.Keys) {
        if ($uninstall.Keys -notcontains $key) {
            throw "Explorer uninstall does not remove $key."
        }
    }
    if ($uninstall.Keys -notcontains 'HKCU:\Software\Classes\*\shell\NovelEngine.Open') {
        throw 'Explorer uninstall does not remove the legacy wildcard key.'
    }
}
Invoke-Step "Smoke context menus" {
    dotnet run --no-build --project src\NovelEngine.Editor\NovelEngine.Editor.csproj -- --context-menu-smoke
}
Invoke-Step "Smoke condition builder" {
    dotnet run --no-build --project src\NovelEngine.Editor\NovelEngine.Editor.csproj -- --condition-builder-smoke
}
Invoke-Step "Smoke visual script blocks" {
    dotnet run --no-build --project src\NovelEngine.Editor\NovelEngine.Editor.csproj -- --visual-script-blocks-smoke
}
Invoke-Step "Smoke asset manager" {
    dotnet run --no-build --project src\NovelEngine.Editor\NovelEngine.Editor.csproj -- --asset-manager-smoke
}
Invoke-Step "Smoke compiled debug preview" {
    $screenshotPath = Join-Path ([System.IO.Path]::GetTempPath()) 'novel-engine-compiled-preview-smoke.png'
    Remove-Item -LiteralPath $screenshotPath -ErrorAction SilentlyContinue
    dotnet run --no-build --project src\NovelEngine.Editor\NovelEngine.Editor.csproj -- --debug-build-screenshot $screenshotPath
    if ($LASTEXITCODE -ne 0) {
        throw "Compiled preview smoke failed with exit code $LASTEXITCODE."
    }
    if (-not (Test-Path -LiteralPath $screenshotPath -PathType Leaf)) {
        throw 'Compiled preview smoke did not create a screenshot.'
    }
    if ((Get-Item -LiteralPath $screenshotPath).Length -lt 1024) {
        throw 'Compiled preview smoke screenshot is unexpectedly small.'
    }
    Remove-Item -LiteralPath $screenshotPath -ErrorAction SilentlyContinue
}
