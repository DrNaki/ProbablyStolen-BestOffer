[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen Demo',
    [switch]$RemoveLoader
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$gameRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd([IO.Path]::DirectorySeparatorChar)

function Get-GamePath([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw "安装清单包含绝对路径：$Relative" }
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $Relative))
    if (!$target.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "路径超出游戏目录：$Relative"
    }
    $probe = $target
    while ($probe.Length -gt $gameRoot.Length) {
        if (Test-Path -LiteralPath $probe) {
            $entry = Get-Item -LiteralPath $probe -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "拒绝通过符号链接或目录联接删除：$probe"
            }
        }
        $probe = [IO.Path]::GetDirectoryName($probe)
    }
    return $target
}
function Get-Sha([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

$gameExe = Get-GamePath 'Probably Stolen.exe'
if (!(Test-Path -LiteralPath $gameExe -PathType Leaf)) { throw "未找到游戏主程序：$gameExe" }
foreach ($process in @(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)) {
    if (!$process.Path -or [IO.Path]::GetFullPath($process.Path).Equals($gameExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先退出游戏，再卸载最佳报价 Mod。'
    }
}
$manifestPath = Get-GamePath 'UserData\BestOfferInstaller\install-manifest.json'
$modPath = Get-GamePath 'Mods\BestOffer.dll'
$disabledMod = Get-GamePath 'Mods\Disabled\BestOffer.dll'
if ((Test-Path -LiteralPath $modPath) -and (Test-Path -LiteralPath $disabledMod)) { throw '同时发现启用和禁用的 BestOffer.dll，未删除。' }
if (Test-Path -LiteralPath $disabledMod) { $modPath = $disabledMod }
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    if (Test-Path -LiteralPath $modPath) { throw '找不到安装清单，无法确认 BestOffer.dll 的归属；未删除任何文件。' }
    if ($RemoveLoader) { throw '找不到安装清单，无法验证加载器；未删除任何文件。' }
    Write-Host '未发现本安装器安装的最佳报价 Mod。'
    return
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.PackageId -ne 'DeepSpaceBestOffer' -or $manifest.SchemaVersion -ne 1 -or
    !([string]$manifest.GameDirectory).Equals($gameRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $manifest.Mod.Path -notin @('Mods\BestOffer.dll','Mods\Disabled\BestOffer.dll') -or $manifest.Mod.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw '安装清单无效；未删除任何文件。'
}
$deletePaths = New-Object 'System.Collections.Generic.List[string]'
if (Test-Path -LiteralPath $modPath -PathType Leaf) {
    if ((Get-Sha $modPath) -ne $manifest.Mod.Sha256) { throw 'BestOffer.dll 已被修改，SHA256 与清单不符；未删除任何文件。' }
    $deletePaths.Add($modPath)
}
$pdbPath = Get-GamePath 'Mods\BestOffer.pdb'
$hasPdbRecord = $null -ne $manifest.PSObject.Properties['Pdb'] -and $null -ne $manifest.Pdb
if ($hasPdbRecord) {
    if ($manifest.Pdb.Path -ne 'Mods\BestOffer.pdb' -or $manifest.Pdb.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
        throw '安装清单 PDB 记录无效；未删除任何文件。'
    }
    if (Test-Path -LiteralPath $pdbPath) {
        if (!(Test-Path -LiteralPath $pdbPath -PathType Leaf) -or (Get-Sha $pdbPath) -ne $manifest.Pdb.Sha256) {
            throw 'BestOffer.pdb 已被修改，SHA256 与清单不符；未删除任何文件。'
        }
        $deletePaths.Add($pdbPath)
    }
}
if ($RemoveLoader) {
    if (@($manifest.LoaderFiles).Count -eq 0) { throw '清单没有加载器文件记录；未删除任何文件。' }
    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($record in @($manifest.LoaderFiles)) {
        $relative = ([string]$record.Path).Replace('/', '\')
        if (($relative -ne 'version.dll' -and !$relative.StartsWith('MelonLoader\', [StringComparison]::OrdinalIgnoreCase)) -or
            $relative -match '(^|\\)\.{1,2}(\\|$)' -or
            $record.Sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or !$seen.Add($relative)) {
            throw "清单包含无效或重复的加载器记录：$relative"
        }
        $target = Get-GamePath $relative
        # Canonical path must still be a loader file, even if the relative input had '..'.
        $loaderPrefix = (Get-GamePath 'MelonLoader') + '\'
        if (!$target.Equals((Get-GamePath 'version.dll'), [StringComparison]::OrdinalIgnoreCase) -and
            !$target.StartsWith($loaderPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "加载器记录越界：$relative"
        }
        if (Test-Path -LiteralPath $target) {
            if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Sha $target) -ne $record.Sha256) {
                throw "加载器文件已变化，SHA256 校验失败；未删除任何文件：$target"
            }
            $deletePaths.Add($target)
        }
    }
}
# Verify the complete removal list before deleting any file. No recursive deletion is used.
if ($deletePaths.Count -gt 0) {
    $backupDir = Get-GamePath ('UserData\BestOfferInstaller\Backups\uninstall-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
    [IO.Directory]::CreateDirectory($backupDir) | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupDir 'install-manifest.json')
    if (Test-Path -LiteralPath $modPath -PathType Leaf) { Copy-Item -LiteralPath $modPath -Destination (Join-Path $backupDir 'BestOffer.dll') }
    if ($hasPdbRecord -and (Test-Path -LiteralPath $pdbPath -PathType Leaf)) { Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $backupDir 'BestOffer.pdb') }
    foreach ($target in $deletePaths) { Remove-Item -LiteralPath $target -Force }
}
$manifest.ModInstalled = $false
if ($RemoveLoader) { $manifest.LoaderInstalled = $false }
[IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Write-Host '最佳报价 Mod 已卸载。'
if ($RemoveLoader) {
    Write-Host '已移除清单内且 SHA256 校验一致的加载器文件。生成缓存、日志、额外插件和配置仍保留。'
} else { Write-Host 'MelonLoader、其他 Mod 和配置已保留。' }
Write-Host '存档未作任何修改。'
