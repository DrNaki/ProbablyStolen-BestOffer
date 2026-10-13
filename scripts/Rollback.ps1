[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen Demo',
    [string]$ReleaseZip = '',
    [ValidatePattern('^$|^[0-9A-Fa-f]{64}$')][string]$ExpectedSha256 = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$utf8 = New-Object Text.UTF8Encoding($false)
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
$knownReleases = [ordered]@{
    '1.4.0' = '774BD3C870AF4A77B4484BDD416F81A93D30E0435D235C7EA123110D6BDB4ECE'
}

function Get-Sha([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
function Get-GamePath([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative)) { throw "路径必须相对于游戏目录：$Relative" }
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $Relative))
    if (!$target.StartsWith($gameRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "路径超出游戏目录：$Relative" }
    $probe = $target
    while ($probe.Length -gt $gameRoot.Length) {
        if (Test-Path -LiteralPath $probe) {
            $entry = Get-Item -LiteralPath $probe -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "拒绝通过符号链接或目录联接回退：$probe" }
            if ($probe -ne $target -and !$entry.PSIsContainer) { throw "目标路径的父级不是文件夹：$probe" }
        }
        $probe = [IO.Path]::GetDirectoryName($probe)
    }
    return $target
}
function Check-ArchiveName([string]$Name) {
    if (!$Name -or $Name.Contains('\') -or $Name.Contains(':') -or $Name.StartsWith('/') -or
        $Name -match '(^|/)\.{1,2}(/|$)' -or $Name.Contains('//')) { throw "ZIP 包含不合法的路径：$Name" }
}

$gameExe = Get-GamePath 'Probably Stolen.exe'
if (!(Test-Path -LiteralPath $gameExe -PathType Leaf)) { throw "未找到游戏主程序：$gameExe" }
foreach ($process in @(Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue)) {
    if (!$process.Path -or [IO.Path]::GetFullPath($process.Path).Equals($gameExe, [StringComparison]::OrdinalIgnoreCase)) {
        throw '请先退出游戏，再回退最佳报价 Mod。'
    }
}
if (!$ReleaseZip) {
    foreach ($relative in @('rollback\BestOffer-1.4.0.zip', 'release\BestOffer-1.4.0.zip')) {
        $candidate = Join-Path $projectDir $relative
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { $ReleaseZip = $candidate; break }
    }
}
if (!$ReleaseZip -or !(Test-Path -LiteralPath $ReleaseZip -PathType Leaf)) {
    throw '未找到回滚发布包。默认需要 rollback 或 release 文件夹内的 BestOffer-1.4.0.zip；回退其他已备份版本时须同时指定 -ReleaseZip 和可信的 -ExpectedSha256。'
}
$ReleaseZip = [IO.Path]::GetFullPath($ReleaseZip)
$archiveHash = Get-Sha $ReleaseZip
$targetVersion = 'specified-release'
foreach ($knownVersion in $knownReleases.Keys) {
    if ([IO.Path]::GetFileName($ReleaseZip) -eq ('BestOffer-' + $knownVersion + '.zip') -and
        $archiveHash -ne $knownReleases[$knownVersion]) {
        throw "$knownVersion 旧发布包与固定的已验收版本 SHA256 不符，未回退。"
    }
    if ($archiveHash -eq $knownReleases[$knownVersion]) { $targetVersion = $knownVersion }
}
if ($ExpectedSha256) {
    if ($archiveHash -ne $ExpectedSha256) { throw '旧发布包与 -ExpectedSha256 不符，未回退。' }
} elseif ($targetVersion -eq 'specified-release') {
    throw '该 ZIP 不是已验收的 1.4.0 发布包。回退其他已备份版本时必须提供可信的 -ExpectedSha256。'
}
$manifestPath = Get-GamePath 'UserData\BestOfferInstaller\install-manifest.json'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw '找不到本 Mod 的安装清单，无法确认当前 DLL 所有权；未修改文件。'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.PackageId -ne 'DeepSpaceBestOffer' -or $manifest.SchemaVersion -ne 1 -or
    !([string]$manifest.GameDirectory).Equals($gameRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $manifest.Mod.Path -notin @('Mods\BestOffer.dll','Mods\Disabled\BestOffer.dll') -or $manifest.Mod.Sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
    throw '安装清单不属于本 Mod 或内容无效，未回退。'
}
$modPath = Get-GamePath 'Mods\BestOffer.dll'
$disabledMod = Get-GamePath 'Mods\Disabled\BestOffer.dll'
if ((Test-Path -LiteralPath $modPath) -and (Test-Path -LiteralPath $disabledMod)) { throw '同时发现启用和禁用的 BestOffer.dll，未回退。' }
if (Test-Path -LiteralPath $disabledMod) { $modPath = $disabledMod }
$manifest.Mod.Path = $modPath.Substring($gameRoot.Length + 1)
$pdbPath = Get-GamePath 'Mods\BestOffer.pdb'
foreach ($target in @($modPath, $pdbPath)) {
    if ((Test-Path -LiteralPath $target) -and !(Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "回退目标不是普通文件：$target"
    }
}
$modExisted = Test-Path -LiteralPath $modPath -PathType Leaf
$pdbExisted = Test-Path -LiteralPath $pdbPath -PathType Leaf
if ($modExisted -and (Get-Sha $modPath) -ne $manifest.Mod.Sha256) {
    throw '当前 BestOffer.dll 与安装清单 SHA256 不符，未覆盖。请先确认该文件的来源。'
}
$hasPdbRecord = $null -ne $manifest.PSObject.Properties['Pdb'] -and $null -ne $manifest.Pdb
if ($hasPdbRecord) {
    if ($manifest.Pdb.Path -ne 'Mods\BestOffer.pdb' -or $manifest.Pdb.Sha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        ($pdbExisted -and (Get-Sha $pdbPath) -ne $manifest.Pdb.Sha256)) { throw '当前 PDB 与安装清单不符，未覆盖。' }
} elseif ($pdbExisted -and !$modExisted) {
    throw '现有 PDB 没有匹配的已确认 DLL 或清单记录，未覆盖。'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($ReleaseZip)
try {
    if ((Get-Sha $ReleaseZip) -ne $archiveHash) { throw '旧 ZIP 在校验期间发生变化，未回退。' }
    $entries = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $caseNames = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    [long]$totalLength = 0
    if ($archive.Entries.Count -gt 4096) { throw 'ZIP 文件数量超出回退检查上限。' }
    foreach ($entry in $archive.Entries) {
        Check-ArchiveName $entry.FullName
        if (!$caseNames.Add($entry.FullName)) { throw "ZIP 包含重复路径：$($entry.FullName)" }
        if ($entry.FullName.EndsWith('/')) { continue }
        $totalLength += $entry.Length
        if ($entry.Length -gt 64MB -or $totalLength -gt 512MB) { throw 'ZIP 解压大小超出回退检查上限。' }
        $entries.Add($entry.FullName, $entry)
    }
    if (!$entries.ContainsKey('SHA256SUMS.txt') -or $entries['SHA256SUMS.txt'].Length -gt 1MB) {
        throw '旧 ZIP 缺少有效 SHA256SUMS.txt。'
    }
    $reader = New-Object IO.StreamReader($entries['SHA256SUMS.txt'].Open(), (New-Object Text.UTF8Encoding($false, $true)))
    try { $checksumText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $checksums = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($line in ($checksumText -split "`r?`n")) {
        if (!$line) { continue }
        if ($line -notmatch '^([0-9A-Fa-f]{64})  (.+)$') { throw 'SHA256SUMS.txt 含无效行。' }
        $hash = $Matches[1]; $name = $Matches[2]
        Check-ArchiveName $name
        if ($name -eq 'SHA256SUMS.txt' -or $checksums.ContainsKey($name)) { throw "校验清单路径重复或无效：$name" }
        $checksums.Add($name, $hash)
    }
    if ($checksums.Count -ne $entries.Count - 1) { throw 'ZIP 文件数量与 SHA256SUMS.txt 不一致。' }
    foreach ($name in $checksums.Keys) {
        if (!$entries.ContainsKey($name)) { throw "校验清单指向不存在的文件：$name" }
        $stream = $entries[$name].Open()
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $algorithm.Dispose() }
        if ($actual -ne $checksums[$name]) { throw "ZIP 内部 SHA256 校验失败：$name" }
    }
    foreach ($name in @('dist/BestOffer.dll', 'dist/BestOffer.pdb')) {
        if (!$entries.ContainsKey($name)) { throw "旧发布包缺少 $name，未回退。" }
    }
    $payload = @{}
    foreach ($name in @('dist/BestOffer.dll', 'dist/BestOffer.pdb')) {
        $stream = $entries[$name].Open(); $memory = New-Object IO.MemoryStream
        try { $stream.CopyTo($memory); $payload[$name] = $memory.ToArray() }
        finally { $stream.Dispose(); $memory.Dispose() }
    }
} finally { $archive.Dispose() }

if ($modExisted -and $pdbExisted -and $hasPdbRecord -and
    (Get-Sha $modPath) -eq $checksums['dist/BestOffer.dll'] -and (Get-Sha $pdbPath) -eq $checksums['dist/BestOffer.pdb']) {
    Write-Host "当前 DLL/PDB 已是目标版本 $targetVersion，无需再次回退。"
    return
}
$backupRelative = 'UserData\BestOfferInstaller\Backups\rollback-' + $targetVersion + '-' +
    (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$backupDir = Get-GamePath $backupRelative
[IO.Directory]::CreateDirectory($backupDir) | Out-Null
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $backupDir 'install-manifest.json')
if ($modExisted) { Copy-Item -LiteralPath $modPath -Destination (Join-Path $backupDir 'BestOffer.dll') }
if ($pdbExisted) { Copy-Item -LiteralPath $pdbPath -Destination (Join-Path $backupDir 'BestOffer.pdb') }
$manifest.Mod.Sha256 = $checksums['dist/BestOffer.dll'].ToUpperInvariant()
$manifest.ModInstalled = $true
$manifest.InstalledAt = [DateTimeOffset]::Now.ToString('o')
$manifest | Add-Member -NotePropertyName Pdb -NotePropertyValue ([pscustomobject]@{
    Path = 'Mods\BestOffer.pdb'; Sha256 = $checksums['dist/BestOffer.pdb'].ToUpperInvariant()
}) -Force
$manifest | Add-Member -NotePropertyName ModVersion -NotePropertyValue $targetVersion -Force
$manifest | Add-Member -NotePropertyName RollbackArchiveSha256 -NotePropertyValue $archiveHash -Force
$stagedDll = Join-Path $backupDir 'target-BestOffer.dll'
$stagedPdb = Join-Path $backupDir 'target-BestOffer.pdb'
$stagedManifest = Join-Path $backupDir 'target-install-manifest.json'
[IO.File]::WriteAllBytes($stagedDll, $payload['dist/BestOffer.dll'])
[IO.File]::WriteAllBytes($stagedPdb, $payload['dist/BestOffer.pdb'])
[IO.File]::WriteAllText($stagedManifest, ($manifest | ConvertTo-Json -Depth 8), $utf8)
try {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($modPath)) | Out-Null
    Copy-Item -LiteralPath $stagedDll -Destination $modPath -Force
    Copy-Item -LiteralPath $stagedPdb -Destination $pdbPath -Force
    if ((Get-Sha $modPath) -ne $checksums['dist/BestOffer.dll'] -or (Get-Sha $pdbPath) -ne $checksums['dist/BestOffer.pdb']) {
        throw '回退后的 DLL/PDB 校验失败。'
    }
    Copy-Item -LiteralPath $stagedManifest -Destination $manifestPath -Force
} catch {
    $originalError = $_
    try {
        if ($modExisted) { Copy-Item -LiteralPath (Join-Path $backupDir 'BestOffer.dll') -Destination $modPath -Force }
        elseif ((Test-Path -LiteralPath $modPath -PathType Leaf) -and (Get-Sha $modPath) -eq $checksums['dist/BestOffer.dll']) { [IO.File]::Delete($modPath) }
        if ($pdbExisted) { Copy-Item -LiteralPath (Join-Path $backupDir 'BestOffer.pdb') -Destination $pdbPath -Force }
        elseif ((Test-Path -LiteralPath $pdbPath -PathType Leaf) -and (Get-Sha $pdbPath) -eq $checksums['dist/BestOffer.pdb']) { [IO.File]::Delete($pdbPath) }
        Copy-Item -LiteralPath (Join-Path $backupDir 'install-manifest.json') -Destination $manifestPath -Force
    } catch { throw "回退失败且自动恢复未完成。原文件保存在 $backupDir；错误：$($_.Exception.Message)" }
    throw $originalError
}
Write-Host "已回退到 $targetVersion；DLL SHA256：$($manifest.Mod.Sha256)"
Write-Host "回退前的 DLL/PDB 和安装清单已保存在：$backupDir"
Write-Host 'rules、settings、其他 Mod、加载器和游戏存档保持原样。以后可再次运行 Install.ps1 升级。'
