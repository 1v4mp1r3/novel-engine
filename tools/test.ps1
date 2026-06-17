[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
Set-Location $repoRoot

dotnet build NovelEngine.sln
dotnet run --no-build --project tests\NovelEngine.Core.Tests\NovelEngine.Core.Tests.csproj
dotnet run --no-build --project tests\NovelEngine.Editor.Tests\NovelEngine.Editor.Tests.csproj
