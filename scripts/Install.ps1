[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen Demo',
    [string]$ModSource = '',
    [string]$LoaderSource = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$packageId = 'DeepSpaceBestOffer'
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
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
                throw "拒绝通过符号链接或目录联接写入：$probe"
            }
            if ($probe -ne $target -and !$entry.PSIsContainer) { throw "目标路径的父级不是文件夹：$probe" }
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
function Ensure-Directory([string]$Path) { [IO.Directory]::CreateDirectory($Path) | Out-Null }

$gameExe = Get-GamePath 'Probably Stolen.exe'
if (!(Test-Path -LiteralPath $gameExe -PathType Leaf)) { throw "未找到游戏主程序：$gameExe" }
foreach ($process in @(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)) {
    if (!$process.Path -or [IO.Path]::GetFullPath($process.Path).Equals($gameExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先退出游戏，再安装或更新最佳报价 Mod。'
    }
}
if (!$ModSource) {
    $ModSource = Join-Path $projectDir 'dist\BestOffer.dll'
    if (!(Test-Path -LiteralPath $ModSource -PathType Leaf)) {
        $ModSource = Join-Path $projectDir 'src\BestOffer\bin\Release\net6.0\BestOffer.dll'
    }
}
if (!$LoaderSource) { $LoaderSource = Join-Path $projectDir 'tools\melonloader\v0.7.3' }
$ModSource = [IO.Path]::GetFullPath($ModSource)
$LoaderSource = [IO.Path]::GetFullPath($LoaderSource).TrimEnd([IO.Path]::DirectorySeparatorChar)
if (!(Test-Path -LiteralPath $ModSource -PathType Leaf)) { throw '未找到 BestOffer.dll，请先运行 scripts\Build.ps1。' }
if (!(Test-Path -LiteralPath (Join-Path $LoaderSource 'version.dll') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $LoaderSource 'MelonLoader\net6\MelonLoader.dll') -PathType Leaf)) {
    throw "MelonLoader 安装源不完整：$LoaderSource"
}

$manifestPath = Get-GamePath 'UserData\BestOfferInstaller\install-manifest.json'
$previousManifest = $null
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    $previousManifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($previousManifest.PackageId -ne $packageId -or $previousManifest.SchemaVersion -ne 1 -or
        !([string]$previousManifest.GameDirectory).Equals($gameRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw '现有安装清单不是本 Mod 的有效清单，未进行任何覆盖。'
    }
}
$loaderFiles = @(
    Get-Item -LiteralPath (Join-Path $LoaderSource 'version.dll')
    Get-ChildItem -LiteralPath (Join-Path $LoaderSource 'MelonLoader') -Recurse -File -Force
) | Sort-Object FullName
$loaderRecords = @()
foreach ($file in $loaderFiles) {
    $relative = $file.FullName.Substring($LoaderSource.Length + 1)
    $loaderRecords += [pscustomobject]@{ Path = $relative; Sha256 = (Get-Sha $file.FullName) }
}
$destinationLoader = Get-GamePath 'MelonLoader'
$destinationProxy = Get-GamePath 'version.dll'
$reuseLoader = (Test-Path -LiteralPath $destinationLoader) -or (Test-Path -LiteralPath $destinationProxy)
$loaderToCopy = New-Object 'System.Collections.Generic.List[object]'
if ($reuseLoader) {
    foreach ($record in $loaderRecords) {
        $target = Get-GamePath $record.Path
        if (!(Test-Path -LiteralPath $target)) {
            # A prior verified install may have been uninstalled while generated cache
            # folders were preserved. Its manifest also permits repairing missing files.
            $knownMissing = $false
            if ($previousManifest) {
                foreach ($oldRecord in @($previousManifest.LoaderFiles)) {
                    if ($oldRecord.Path -eq $record.Path -and $oldRecord.Sha256 -eq $record.Sha256) {
                        $knownMissing = $true
                        break
                    }
                }
            }
            if ($knownMissing) { $loaderToCopy.Add($record); continue }
        }
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (Get-Sha $target) -ne $record.Sha256) {
            throw "发现未知、不同版本或不完整的加载器，未覆盖：$target。请先由其原安装方式处理。"
        }
    }
} else {
    foreach ($record in $loaderRecords) { $null = Get-GamePath $record.Path; $loaderToCopy.Add($record) }
}
$modRelative = 'Mods\BestOffer.dll'
$activeMod = Get-GamePath $modRelative
$disabledMod = Get-GamePath 'Mods\Disabled\BestOffer.dll'
if ((Test-Path -LiteralPath $activeMod) -and (Test-Path -LiteralPath $disabledMod)) { throw '同时发现启用和禁用的 BestOffer.dll，未覆盖。' }
if (Test-Path -LiteralPath $disabledMod) { $modRelative = 'Mods\Disabled\BestOffer.dll' }
$modPath = Get-GamePath $modRelative
$sourceHash = Get-Sha $ModSource
$existingMod = Test-Path -LiteralPath $modPath -PathType Leaf
if ((Test-Path -LiteralPath $modPath) -and !$existingMod) { throw 'Mods\BestOffer.dll 不是普通文件，未进行安装。' }
if ($existingMod) {
    $existingHash = Get-Sha $modPath
    $recognized = $existingHash -eq $sourceHash
    if ($previousManifest -and $previousManifest.Mod.Path -in @('Mods\BestOffer.dll','Mods\Disabled\BestOffer.dll') -and
        $previousManifest.Mod.Sha256 -eq $existingHash) { $recognized = $true }
    if (!$recognized) { throw 'Mods\BestOffer.dll 已存在且无法确认属于本安装，未覆盖。' }
}
$pdbPath = Get-GamePath 'Mods\BestOffer.pdb'
$pdbSource = [IO.Path]::ChangeExtension($ModSource, '.pdb')
$hasSourcePdb = Test-Path -LiteralPath $pdbSource -PathType Leaf
$existingPdb = Test-Path -LiteralPath $pdbPath -PathType Leaf
if ((Test-Path -LiteralPath $pdbPath) -and !$existingPdb) { throw 'Mods\BestOffer.pdb 不是普通文件，未进行安装。' }
$sourcePdbHash = if ($hasSourcePdb) { Get-Sha $pdbSource } else { $null }
if ($existingPdb) {
    $existingPdbHash = Get-Sha $pdbPath
    $pdbRecognized = $hasSourcePdb -and $existingPdbHash -eq $sourcePdbHash
    if ($previousManifest) {
        $pdbRecord = $previousManifest.PSObject.Properties['Pdb']
        if ($pdbRecord -and $null -ne $pdbRecord.Value) {
            if ($previousManifest.Pdb.Path -eq 'Mods\BestOffer.pdb' -and $previousManifest.Pdb.Sha256 -eq $existingPdbHash) { $pdbRecognized = $true }
        } elseif ($existingMod -and $previousManifest.Mod.Sha256 -eq $existingHash) {
            # Legacy manifests tracked only the DLL. Preserve its companion PDB in
            # the backup before adopting the same named symbols into managed state.
            $pdbRecognized = $true
        }
    }
    if (!$pdbRecognized) { throw 'Mods\BestOffer.pdb 已存在且无法确认属于本安装，未覆盖。' }
}

# All destinations and existing files have been verified before any mutation.
$backupRelative = 'UserData\BestOfferInstaller\Backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$backupDir = Get-GamePath $backupRelative
if ($existingMod -or $existingPdb -or $previousManifest) {
    Ensure-Directory $backupDir
    if ($existingMod) { Copy-Item -LiteralPath $modPath -Destination (Join-Path $backupDir 'BestOffer.dll') }
    if ($existingPdb) { Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $backupDir 'BestOffer.pdb') }
    if ($previousManifest) { Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupDir 'install-manifest.json') }
    Write-Host "已备份原 Mod 和安装清单：$backupDir"
}
if ($loaderToCopy.Count -gt 0) {
    foreach ($record in $loaderToCopy) {
        $target = Get-GamePath $record.Path
        Ensure-Directory ([IO.Path]::GetDirectoryName($target))
        Copy-Item -LiteralPath (Join-Path $LoaderSource $record.Path) -Destination $target
        if ((Get-Sha $target) -ne $record.Sha256) { throw "复制后校验失败：$target" }
    }
    Write-Host '已安装或补全 MelonLoader 0.7.3。'
} else { Write-Host '现有 MelonLoader 与安装包逐文件校验一致，直接复用。' }
Ensure-Directory ([IO.Path]::GetDirectoryName($modPath))
if (!$existingMod -or (Get-Sha $modPath) -ne $sourceHash) {
    Copy-Item -LiteralPath $ModSource -Destination $modPath -Force
}
if ((Get-Sha $modPath) -ne $sourceHash) { throw 'Mod 复制后 SHA256 校验失败。' }
if ($hasSourcePdb) {
    Copy-Item -LiteralPath $pdbSource -Destination $pdbPath -Force
    if ((Get-Sha $pdbPath) -ne $sourcePdbHash) { throw 'PDB 复制后 SHA256 校验失败。' }
} elseif ($existingPdb) {
    # A DLL-only deployment must not leave mismatched old symbols beside it.
    [IO.File]::Delete($pdbPath)
}
$manifest = [ordered]@{
    SchemaVersion = 1; PackageId = $packageId; GameDirectory = $gameRoot
    InstalledAt = [DateTimeOffset]::Now.ToString('o'); LoaderVersion = '0.7.3'
    LoaderInstalled = $true; ModInstalled = $true
    Mod = [ordered]@{ Path = $modRelative; Sha256 = $sourceHash }
    Pdb = $(if ($hasSourcePdb) { [ordered]@{ Path = 'Mods\BestOffer.pdb'; Sha256 = $sourcePdbHash } } else { $null })
    LoaderFiles = @($loaderRecords)
}
Ensure-Directory ([IO.Path]::GetDirectoryName($manifestPath))
$manifestTemp = Get-GamePath 'UserData\BestOfferInstaller\install-manifest.json.tmp'
[IO.File]::WriteAllText($manifestTemp, ($manifest | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
Move-Item -LiteralPath $manifestTemp -Destination $manifestPath -Force
Write-Host "最佳报价已安装：$modPath"
Write-Host '启动游戏后，在店内点击「最佳报价」或按 F8。首次启动会生成互操作程序集，耗时可能较长。'
Write-Host '安装不修改存档。'
