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
