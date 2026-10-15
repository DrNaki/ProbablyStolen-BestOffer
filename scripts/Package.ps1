[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen Demo',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.4.3'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$utf8 = New-Object Text.UTF8Encoding($false)
# .NET Framework's ZIP writer sets the UTF-8 filename flag with Encoding.UTF8;
# a separately constructed UTF8Encoding can leave Chinese entry names unflagged.
$zipNameEncoding = [Text.Encoding]::UTF8
$loaderManifestPath = Join-Path $projectDir 'tools\melonloader\v0.7.3-files.json'
$expectedLoaderManifestHash = '5D0D5CEFAA2F7C5D4F652254B1710427E34007B9171038860E2C278C92C256F5'
$knownRollbackHash = '774BD3C870AF4A77B4484BDD416F81A93D30E0435D235C7EA123110D6BDB4ECE'

function Get-Sha([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}
function Get-SafeSource([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[\\/])\.{1,2}([\\/]|$)') {
        throw "包内路径不合法：$Relative"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $projectDir $Relative))
    if (!$path.StartsWith($projectDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "源文件超出项目目录：$Relative"
    }
    $probe = $path
    while ($probe.Length -gt $projectDir.Length) {
        if (Test-Path -LiteralPath $probe) {
            $entry = Get-Item -LiteralPath $probe -Force
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "打包不读取符号链接或目录联接：$probe"
            }
        }
        $probe = [IO.Path]::GetDirectoryName($probe)
    }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "打包所需文件不存在：$Relative" }
    return $path
}

# Check source allowlists before building. Never scan the workspace recursively.
$null = Get-SafeSource 'README.md'
$null = Get-SafeSource 'THIRD-PARTY-NOTICES.md'
$null = Get-SafeSource 'tools/melonloader/v0.7.3-files.json'
$rollbackSourceRelative = ''
foreach ($candidate in @('rollback/BestOffer-1.4.0.zip', 'release/BestOffer-1.4.0.zip')) {
    if (Test-Path -LiteralPath (Join-Path $projectDir $candidate) -PathType Leaf) {
        $rollbackSourceRelative = $candidate
        break
    }
}
if (!$rollbackSourceRelative -or (Get-Sha (Get-SafeSource $rollbackSourceRelative)) -ne $knownRollbackHash) {
    throw '缺少经过固定 SHA256 验证的 1.4.0 基线发布包，不能生成可回退的新版本。'
}
if ((Get-Sha $loaderManifestPath) -ne $expectedLoaderManifestHash) {
    throw 'MelonLoader 原包文件清单校验失败，拒绝把未核验的加载器文件放入发布包。'
}
$loaderManifest = Get-Content -LiteralPath $loaderManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($loaderManifest.Version -ne '0.7.3' -or $loaderManifest.SourceSha256 -ne
    '5B2B2F3D1CD42B59EC886C5BDC2663EDAE87A0097A4F4A8F58C0965A99DDA416') {
    throw 'MelonLoader 来源清单与指定官方版本不一致。'
}
$docRoot = Join-Path $projectDir 'docs'
$documents = @(Get-ChildItem -LiteralPath $docRoot -Recurse -File -Force)
if ($documents.Count -eq 0) { throw 'docs 尚无最终文档；请完成文档后再打包。' }
$allowedDocExtensions = @('.md', '.txt', '.pdf', '.html', '.css', '.png', '.jpg', '.jpeg', '.svg', '.webp')
foreach ($document in $documents) {
    if ($allowedDocExtensions -notcontains $document.Extension.ToLowerInvariant()) {
        throw "docs 含不在文档白名单内的文件，请检查后再发布：$($document.FullName)"
    }
}
$releaseDir = Join-Path $projectDir 'release'
if (Test-Path -LiteralPath $releaseDir) {
    $releaseEntry = Get-Item -LiteralPath $releaseDir -Force
    if (!$releaseEntry.PSIsContainer -or ($releaseEntry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'release 必须是项目内的普通文件夹。'
    }
}

Write-Host '开始发布构建：运行测试，并使用固定源码映射生成 DLL/PDB。'
& (Join-Path $PSScriptRoot 'Build.ps1') -GameDir $GameDir -Configuration Release -Reproducible

$payload = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
function Add-Payload([string]$Relative, [string]$SourceRelative = '') {
    $name = $Relative.Replace('\', '/')
    if ($name -match '(^|/)(bin|obj|Il2CppAssemblies|DummyDll|backups|analysis)(/|$)' -or
        $name -match '(^|/)(GameAssembly\.dll|Assembly-CSharp[^/]*\.dll|global-metadata\.dat)$' -or
        $name -match '\.es3$') {
        throw "禁止把游戏代码、互操作文件、逆向导出或存档加入发布包：$name"
    }
    if ($payload.ContainsKey($name)) { throw "包内路径重复：$name" }
    if (!$SourceRelative) { $SourceRelative = $Relative }
    $payload.Add($name, (Get-SafeSource $SourceRelative))
}
Add-Payload 'README.md'
Add-Payload 'THIRD-PARTY-NOTICES.md'
Add-Payload 'dist/BestOffer.dll'
Add-Payload 'dist/BestOffer.pdb'
Add-Payload 'tests/OfferSolver.Tests.csproj'
Add-Payload 'tests/Program.cs'
Add-Payload 'tests/Rollback.Tests.ps1'
Add-Payload 'tests/ManagerInstaller.Tests.ps1'
Add-Payload 'tests/KeyChord.Tests/KeyChord.Tests.csproj'
Add-Payload 'tests/KeyChord.Tests/Program.cs'
Add-Payload 'tests/ItemRuleGroups.Tests/ItemRuleGroups.Tests.csproj'
Add-Payload 'tests/ItemRuleGroups.Tests/Program.cs'
Add-Payload 'tests/RiskPolicy.Tests/RiskPolicy.Tests.csproj'
Add-Payload 'tests/RiskPolicy.Tests/Program.cs'
Add-Payload 'tests/InventorySourcePolicy.Tests/InventorySourcePolicy.Tests.csproj'
Add-Payload 'tests/InventorySourcePolicy.Tests/Program.cs'
Add-Payload 'tests/InventorySourcePolicy.Tests/NativeFixtures.cs'
foreach ($name in @('Build', 'Install', 'Uninstall', 'Package', 'Rollback')) { Add-Payload ('scripts/' + $name + '.ps1') }
Add-Payload 'rollback/BestOffer-1.4.0.zip' $rollbackSourceRelative
foreach ($source in @(Get-ChildItem -LiteralPath (Join-Path $projectDir 'src\BestOffer') -File)) {
    if (@('.cs', '.csproj') -contains $source.Extension.ToLowerInvariant()) {
        Add-Payload ('src/BestOffer/' + $source.Name)
    }
}
foreach ($document in $documents) { Add-Payload $document.FullName.Substring($projectDir.Length + 1) }
Add-Payload 'tools/melonloader/v0.7.3-files.json'
foreach ($record in @($loaderManifest.Files)) {
    $relative = 'tools/melonloader/v0.7.3/' + ([string]$record.Path).Replace('\', '/')
    $source = Get-SafeSource $relative
    if ((Get-Sha $source) -ne $record.Sha256 -or (Get-Item -LiteralPath $source).Length -ne $record.Length) {
        throw "MelonLoader 文件与官方原包不一致：$relative"
    }
    Add-Payload $relative
}

[string[]]$names = @($payload.Keys)
[Array]::Sort($names, [StringComparer]::Ordinal)
$checksumLines = New-Object 'System.Collections.Generic.List[string]'
$expectedHashes = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($name in $names) {
    $hash = (Get-Sha $payload[$name]).ToLowerInvariant()
    $expectedHashes.Add($name, $hash)
    $checksumLines.Add($hash + '  ' + $name)
}
$checksumBytes = $utf8.GetBytes(($checksumLines -join "`n") + "`n")

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Directory]::CreateDirectory($releaseDir) | Out-Null
$archiveName = 'BestOffer-' + $Version + '.zip'
$archivePath = Join-Path $releaseDir $archiveName
$temporaryPath = Join-Path $releaseDir ('.BestOffer-' + $Version + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
$fixedTime = New-Object DateTimeOffset(2000, 1, 1, 0, 0, 0, ([TimeSpan]::Zero))
try {
    $archiveStream = [IO.File]::Open($temporaryPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive($archiveStream, [IO.Compression.ZipArchiveMode]::Create, $true, $zipNameEncoding)
        try {
            # Ordinal order, normalized timestamps and no platform attributes make ZIP
            # metadata reproducible. Exact byte identity also needs identical SDK/runtime.
            foreach ($name in $names) {
                $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $entry.ExternalAttributes = 0
                $destination = $entry.Open()
                $source = [IO.File]::OpenRead($payload[$name])
                try { $source.CopyTo($destination) }
                finally { $source.Dispose(); $destination.Dispose() }
            }
            $entry = $archive.CreateEntry('SHA256SUMS.txt', [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixedTime
            $entry.ExternalAttributes = 0
            $destination = $entry.Open()
            try { $destination.Write($checksumBytes, 0, $checksumBytes.Length) }
            finally { $destination.Dispose() }
        } finally { $archive.Dispose() }
    } finally { $archiveStream.Dispose() }

    # Reopen and verify bytes actually written, so an edited input during packaging
    # cannot produce a release whose checksum manifest describes an older file.
    $verificationArchive = [IO.Compression.ZipFile]::OpenRead($temporaryPath)
    try {
        if ($verificationArchive.Entries.Count -ne $payload.Count + 1) { throw 'ZIP 文件数量校验失败。' }
        foreach ($name in $names) {
            $entry = $verificationArchive.GetEntry($name)
            if ($null -eq $entry) { throw "ZIP 缺少文件：$name" }
            $stream = $entry.Open()
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
            finally { $stream.Dispose(); $algorithm.Dispose() }
            if ($actual -ne $expectedHashes[$name]) { throw "文件在打包期间发生变化，未发布，请重试：$name" }
        }
    } finally { $verificationArchive.Dispose() }
    $archiveHash = Get-Sha $temporaryPath
    if (Test-Path -LiteralPath $archivePath) {
        $existing = Get-Item -LiteralPath $archivePath -Force
        if (!$existing.PSIsContainer -and ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and
            (Get-Sha $archivePath) -eq $archiveHash) {
            Write-Host '现有发布包与本次输出完全一致，保留原文件。'
        } else {
            throw "发布目标已存在且内容不同，未覆盖：$archivePath。请先另行保存现有包。"
        }
    } else { [IO.File]::Move($temporaryPath, $archivePath) }

    $sidecar = $archivePath + '.sha256'
    $sidecarText = $archiveHash.ToLowerInvariant() + '  ' + $archiveName + "`n"
    if (Test-Path -LiteralPath $sidecar) {
        $existingSidecar = Get-Item -LiteralPath $sidecar -Force
        if ($existingSidecar.PSIsContainer -or ($existingSidecar.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or
            [IO.File]::ReadAllText($sidecar) -ne $sidecarText) {
            throw "校验文件已存在且内容不同，未覆盖：$sidecar"
        }
    } else { [IO.File]::WriteAllText($sidecar, $sidecarText, $utf8) }
    Write-Host "发布包：$archivePath"
    Write-Host "SHA256：$archiveHash"
    Write-Host "已包含 $($payload.Count) 个文件和 SHA256SUMS.txt；未打包游戏互操作程序集、逆向导出或存档。"
} finally {
    # This exact unique temporary file is created by this invocation. No directory,
    # existing release, unrelated file, or game file is ever deleted here.
    if ([IO.File]::Exists($temporaryPath)) { [IO.File]::Delete($temporaryPath) }
}
