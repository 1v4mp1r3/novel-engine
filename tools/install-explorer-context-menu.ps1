[CmdletBinding()]
param(
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$editorProject = Join-Path $repoRoot 'src\NovelEngine.Editor\NovelEngine.Editor.csproj'
$menuText = -join @(
    [char]0x041E, [char]0x0442, [char]0x043A, [char]0x0440,
    [char]0x044B, [char]0x0442, [char]0x044C, [char]0x0020,
    [char]0x0441, [char]0x0020, [char]0x043F, [char]0x043E,
    [char]0x043C, [char]0x043E, [char]0x0449, [char]0x044C,
    [char]0x044E, [char]0x0020, [char]0x004E, [char]0x006F,
    [char]0x0076, [char]0x0065, [char]0x006C, [char]0x0020,
    [char]0x0045, [char]0x006E, [char]0x0067, [char]0x0069,
    [char]0x006E, [char]0x0065
)

function Quote-Argument {
    param([Parameter(Mandatory = $true)][string]$Value)
    '"' + $Value.Replace('"', '""') + '"'
}

function Resolve-ExecutablePath {
    param([Parameter(Mandatory = $true)][string]$Value)

    if (-not (Test-Path -LiteralPath $Value -PathType Leaf)) {
        throw "ExecutablePath must point to an existing file: $Value"
    }
    (Resolve-Path -LiteralPath $Value).Path
}

if ($ExecutablePath) {
    $resolvedExecutable = Resolve-ExecutablePath $ExecutablePath
    $baseCommand = "$(Quote-Argument $resolvedExecutable) --open-project"
    $iconPath = $resolvedExecutable
} else {
    if (-not (Test-Path $editorProject)) {
        throw "Не найден проект редактора: $editorProject"
    }
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $baseCommand = "$(Quote-Argument $dotnet) run -c Release --project $(Quote-Argument $editorProject) -- --open-project"
    $releaseExe = Join-Path $repoRoot 'src\NovelEngine.Editor\bin\Release\net9.0-windows\NovelEngine.Editor.exe'
    $debugExe = Join-Path $repoRoot 'src\NovelEngine.Editor\bin\Debug\net9.0-windows\NovelEngine.Editor.exe'
    $iconPath = if (Test-Path $releaseExe) {
        $releaseExe
    } elseif (Test-Path $debugExe) {
        $debugExe
    } else {
        $dotnet
    }
}

$items = @(
    @{
        Key = 'HKCU:\Software\Classes\Directory\shell\NovelEngine.Open'
        Argument = '%1'
    },
    @{
        Key = 'HKCU:\Software\Classes\Directory\Background\shell\NovelEngine.Open'
        Argument = '%V'
    }
)

foreach ($item in $items) {
    $key = $item.Key
    $commandKey = Join-Path $key 'command'
    New-Item -Path $key -Force | Out-Null
    New-Item -Path $commandKey -Force | Out-Null
    Set-Item -Path $key -Value $menuText
    New-ItemProperty -Path $key -Name 'MUIVerb' -Value $menuText -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $key -Name 'Icon' -Value $iconPath -PropertyType String -Force | Out-Null
    Set-Item -Path $commandKey -Value ('{0} "{1}"' -f $baseCommand, $item.Argument)
}

Write-Host ('Explorer context menu item "{0}" installed for project folders.' -f $menuText)
