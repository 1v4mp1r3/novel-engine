[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
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

$keys = @(
    'HKCU:\Software\Classes\Directory\shell\NovelEngine.Open',
    'HKCU:\Software\Classes\Directory\Background\shell\NovelEngine.Open',
    'HKCU:\Software\Classes\SystemFileAssociations\.novel.json\shell\NovelEngine.Open'
)

foreach ($key in $keys) {
    Remove-Item -Path $key -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ('Explorer context menu item "{0}" removed.' -f $menuText)
