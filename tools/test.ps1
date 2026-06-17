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
