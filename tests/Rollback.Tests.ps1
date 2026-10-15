#requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $PSScriptRoot ('rollback-smoke-' + [Guid]::NewGuid().ToString('N').Substring(0, 10))
$logs = Join-Path $fixture 'logs'
[IO.Directory]::CreateDirectory($logs) | Out-Null
$results = New-Object 'System.Collections.Generic.List[object]'
$checks = 0
function Check([bool]$Condition, [string]$Message) {
    $script:checks++
    if (!$Condition) { throw "ASSERTION FAILED: $Message" }
}
function Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Write-Text([string]$Path, [string]$Text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($false)))
}
function Run-Script([string]$Case, [string]$Script, [string[]]$Arguments, [bool]$ExpectedSuccess = $true) {
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = 'powershell.exe'; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.StandardOutputEncoding = New-Object Text.UTF8Encoding($false)
    $start.StandardErrorEncoding = New-Object Text.UTF8Encoding($false)
    foreach ($argument in @('-NoProfile','-NonInteractive','-ExecutionPolicy','Bypass','-File',$Script) + $Arguments) { $start.ArgumentList.Add($argument) }
    $process = New-Object Diagnostics.Process; $process.StartInfo = $start
    $null = $process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    if (!$process.WaitForExit(60000)) { $process.Kill(); throw "Test timeout: $Case" }
    $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
    Write-Text (Join-Path $logs ($Case + '.log')) $output
    Check (($process.ExitCode -eq 0) -eq $ExpectedSuccess) "$Case expected success=$ExpectedSuccess, exit=$($process.ExitCode). $output"
    $results.Add([pscustomobject]@{Case=$Case;ExitCode=$process.ExitCode;ExpectedSuccess=$ExpectedSuccess;Passed=$true})
    Write-Host "PASS: $Case"
}
function Make-CustomZip([string]$Path, [bool]$BadHash, [bool]$UnsafePath) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $items = [ordered]@{'dist/BestOffer.dll'='OLD DLL';'dist/BestOffer.pdb'='OLD PDB'}
        if ($UnsafePath) { $items['../outside.txt'] = 'DO NOT EXTRACT' }
        $lines = New-Object 'System.Collections.Generic.List[string]'
        foreach ($name in $items.Keys) {
            $bytes = [Text.Encoding]::UTF8.GetBytes($items[$name]); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','') } finally { $sha.Dispose() }
            if ($BadHash -and $name -eq 'dist/BestOffer.dll') { $hash = '0' * 64 }
            $lines.Add($hash + '  ' + $name)
            $stream = $zip.CreateEntry($name).Open()
            try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
        }
        $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n") + "`n")
        $stream = $zip.CreateEntry('SHA256SUMS.txt').Open()
        try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
}

$oldZip = Join-Path $project 'release\BestOffer-1.4.0.zip'
if (!(Test-Path -LiteralPath $oldZip)) { $oldZip = Join-Path $project 'rollback\BestOffer-1.4.0.zip' }
$oldZipHash = Hash $oldZip
Check ($oldZipHash -eq '774BD3C870AF4A77B4484BDD416F81A93D30E0435D235C7EA123110D6BDB4ECE') 'Stable 1.4.0 release hash changed'
$oldDllHash = '3225437CB5EC9E29BEFB0A207A365DFA9EF793D9267515BB93F675CE69F380DC'
$oldPdbHash = '1238B91DBDFB7B3019A1CCE1717250FC8A59D057B891315A1F96F4683CE458A3'

$parseTokens = $null; $parseErrors = $null
$packageAst = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'scripts\Package.ps1'), [ref]$parseTokens, [ref]$parseErrors)
Check ($parseErrors.Count -eq 0) 'Package script has syntax errors'
$versionDefault = @($packageAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'Version' })[0].DefaultValue.SafeGetValue()
Check ($versionDefault -eq '1.4.3') 'Package default version is not 1.4.3'
$packageText = [IO.File]::ReadAllText((Join-Path $project 'scripts\Package.ps1'))
Check ($packageText.Contains($oldZipHash) -and $packageText.Contains("Add-Payload 'rollback/BestOffer-1.4.0.zip'")) 'Package does not bundle the pinned 1.4.0 rollback archive'
$install = Join-Path $project 'scripts\Install.ps1'
$uninstall = Join-Path $project 'scripts\Uninstall.ps1'
$rollback = Join-Path $project 'scripts\Rollback.ps1'
$game = Join-Path $fixture 'game'
Write-Text (Join-Path $game 'Probably Stolen.exe') 'fixture only'
Write-Text (Join-Path $game 'save-fixture.es3') 'SAVE STAYS UNCHANGED'
Write-Text (Join-Path $game 'UserData\BestOffer-rules.json') '{"rules":"preserve"}'
Write-Text (Join-Path $game 'UserData\BestOffer-settings.json') '{"settings":"preserve"}'
Write-Text (Join-Path $game 'Mods\OtherMod.dll') 'OTHER MOD STAYS UNCHANGED'
$sourceDll = Join-Path $fixture 'new-source\BestOffer.dll'
$sourcePdb = Join-Path $fixture 'new-source\BestOffer.pdb'
Write-Text $sourceDll '1.4.1 DUMMY DLL - TEST ONLY'
Write-Text $sourcePdb '1.4.1 DUMMY PDB - TEST ONLY'
$newHash = Hash $sourceDll; $newPdbHash = Hash $sourcePdb
$mod = Join-Path $game 'Mods\BestOffer.dll'; $pdb = Join-Path $game 'Mods\BestOffer.pdb'
$manifestPath = Join-Path $game 'UserData\BestOfferInstaller\install-manifest.json'
$loaderSource = Join-Path $project 'tools\melonloader\v0.7.3'
$installArgs = @('-GameDir',$game,'-ModSource',$sourceDll,'-LoaderSource',$loaderSource)
Run-Script '01-install-1.4.1-dummy-with-pdb' $install $installArgs
Check ((Hash $pdb) -eq $newPdbHash) 'Installer did not deploy current PDB'
$loaderHash = Hash (Join-Path $game 'version.dll')
Run-Script '02-default-rollback-to-1.4.0' $rollback @('-GameDir',$game)
Check ((Hash $mod) -eq $oldDllHash) 'Rollback DLL is not stable 1.4.0'
Check ((Hash $pdb) -eq $oldPdbHash) 'Rollback PDB is not stable 1.4.0'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($manifest.Mod.Sha256 -eq $oldDllHash -and $manifest.Pdb.Sha256 -eq (Hash $pdb)) 'Rollback manifest not updated'
Check ($manifest.ModVersion -eq '1.4.0' -and $manifest.RollbackArchiveSha256 -eq $oldZipHash) 'Default rollback manifest version/hash is not stable 1.4.0'
$backup = @(Get-ChildItem -LiteralPath (Join-Path $game 'UserData\BestOfferInstaller\Backups') -Directory | Where-Object Name -like 'rollback-*') | Select-Object -Last 1
Check ((Hash (Join-Path $backup.FullName 'BestOffer.dll')) -eq $newHash) 'Rollback did not preserve prior DLL'
Check ((Hash (Join-Path $backup.FullName 'BestOffer.pdb')) -eq $newPdbHash) 'Rollback did not preserve prior PDB'
Run-Script '03-idempotent-rollback' $rollback @('-GameDir',$game)
Run-Script '04-upgrade-after-rollback' $install $installArgs
Check ((Hash $mod) -eq $newHash -and (Hash $pdb) -eq $newPdbHash) 'Re-upgrade DLL/PDB mismatch'

Write-Text $mod 'TAMPERED DLL'
Run-Script '05-current-dll-hash-protection' $rollback @('-GameDir',$game) $false
Check ([IO.File]::ReadAllText($mod) -eq 'TAMPERED DLL') 'Tampered DLL was overwritten'
Copy-Item -LiteralPath $sourceDll -Destination $mod -Force
Write-Text $pdb 'TAMPERED PDB'
Run-Script '06-current-pdb-hash-protection' $rollback @('-GameDir',$game) $false
Check ([IO.File]::ReadAllText($pdb) -eq 'TAMPERED PDB') 'Tampered PDB was overwritten'
Copy-Item -LiteralPath $sourcePdb -Destination $pdb -Force
$badZip = Join-Path $fixture 'tampered\BestOffer-1.4.0.zip'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($badZip)) | Out-Null
Copy-Item -LiteralPath $oldZip -Destination $badZip
[IO.File]::AppendAllText($badZip,'TAMPERED ZIP')
Run-Script '07-known-zip-hash-protection' $rollback @('-GameDir',$game,'-ReleaseZip',$badZip,'-ExpectedSha256',(Hash $badZip)) $false
Check ((Hash $mod) -eq $newHash) 'Corrupt known ZIP changed DLL'
$internalBad = Join-Path $fixture 'bad-internal.zip'
Make-CustomZip $internalBad $true $false
Run-Script '08-internal-sha-protection' $rollback @('-GameDir',$game,'-ReleaseZip',$internalBad,'-ExpectedSha256',(Hash $internalBad)) $false
$unsafeZip = Join-Path $fixture 'unsafe-path.zip'
Make-CustomZip $unsafeZip $false $true
Run-Script '09-zip-path-protection' $rollback @('-GameDir',$game,'-ReleaseZip',$unsafeZip,'-ExpectedSha256',(Hash $unsafeZip)) $false
Check (!(Test-Path -LiteralPath (Join-Path $fixture 'outside.txt'))) 'Unsafe ZIP path was extracted'
$manifestText = [IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json; $manifest.PackageId = 'AnotherMod'
Write-Text $manifestPath ($manifest | ConvertTo-Json -Depth 8)
Run-Script '10-manifest-owner-protection' $rollback @('-GameDir',$game) $false
Write-Text $manifestPath $manifestText

$manifest = $manifestText | ConvertFrom-Json
$manifest.PSObject.Properties.Remove('Pdb')
Write-Text $manifestPath ($manifest | ConvertTo-Json -Depth 8)
Run-Script '11-legacy-dll-only-manifest' $rollback @('-GameDir',$game)
Check ((Hash $mod) -eq $oldDllHash) 'Legacy manifest rollback failed'
Run-Script '12-upgrade-legacy-rollback' $install $installArgs

$portableRoot = Join-Path $fixture 'portable'
[IO.Directory]::CreateDirectory((Join-Path $portableRoot 'scripts')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $portableRoot 'rollback')) | Out-Null
Copy-Item -LiteralPath $rollback -Destination (Join-Path $portableRoot 'scripts\Rollback.ps1')
Copy-Item -LiteralPath $oldZip -Destination (Join-Path $portableRoot 'rollback\BestOffer-1.4.0.zip')
Run-Script '13-portable-bundled-rollback' (Join-Path $portableRoot 'scripts\Rollback.ps1') @('-GameDir',$game)
Check ((Hash $mod) -eq $oldDllHash) 'Portable rollback missing stable DLL'
Run-Script '14-upgrade-from-portable-rollback' $install $installArgs
$dllOnly = Join-Path $fixture 'dll-only-source\BestOffer.dll'
Write-Text $dllOnly '1.4.1 DLL ONLY - TEST ONLY'
Run-Script '15-dll-only-upgrade-removes-stale-pdb' $install @('-GameDir',$game,'-ModSource',$dllOnly,'-LoaderSource',$loaderSource)
Check (!(Test-Path -LiteralPath $pdb)) 'DLL-only upgrade retained stale PDB'
Run-Script '16-rollback-after-dll-only-upgrade' $rollback @('-GameDir',$game)
$unknownZip = Join-Path $fixture 'explicitly-trusted.zip'
Make-CustomZip $unknownZip $false $false
Run-Script '16b-unknown-zip-requires-trusted-sha' $rollback @('-GameDir',$game,'-ReleaseZip',$unknownZip) $false
Check ((Hash $mod) -eq $oldDllHash) 'Unknown ZIP without a trusted SHA changed DLL'
Run-Script '16c-unknown-zip-wrong-trusted-sha' $rollback @('-GameDir',$game,'-ReleaseZip',$unknownZip,'-ExpectedSha256',('F' * 64)) $false
Check ((Hash $mod) -eq $oldDllHash) 'Unknown ZIP with an incorrect trusted SHA changed DLL'
Run-Script '16d-explicit-trusted-zip' $rollback @('-GameDir',$game,'-ReleaseZip',$unknownZip,'-ExpectedSha256',(Hash $unknownZip))
Check ([IO.File]::ReadAllText($mod) -eq 'OLD DLL' -and [IO.File]::ReadAllText($pdb) -eq 'OLD PDB') 'Explicit trusted ZIP payload was not deployed'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Check ($manifest.ModVersion -eq 'specified-release' -and $manifest.RollbackArchiveSha256 -eq (Hash $unknownZip)) 'Explicit trusted ZIP manifest was not tracked accurately'
Run-Script '16e-return-to-default-1.4.0' $rollback @('-GameDir',$game)
Check ((Hash $mod) -eq $oldDllHash -and (Hash $pdb) -eq $oldPdbHash) 'Default did not return to 1.4.0 after a trusted custom ZIP'
Run-Script '16f-re-upgrade-after-custom-rollback' $install $installArgs
Check ((Hash $mod) -eq $newHash -and (Hash $pdb) -eq $newPdbHash) 'Re-upgrade after a custom rollback failed'
$disabledMod = Join-Path $game 'Mods\Disabled\BestOffer.dll'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($disabledMod)) | Out-Null
Move-Item -LiteralPath $mod -Destination $disabledMod
Run-Script '16g-disabled-mod-stays-disabled' $rollback @('-GameDir',$game)
Check (!(Test-Path -LiteralPath $mod) -and (Hash $disabledMod) -eq $oldDllHash) 'Rollback re-enabled a disabled mod'
Copy-Item -LiteralPath $sourceDll -Destination $mod
Run-Script '16h-duplicate-enabled-disabled-rejected' $rollback @('-GameDir',$game) $false
Check ((Hash $mod) -eq $newHash -and (Hash $disabledMod) -eq $oldDllHash) 'Duplicate enabled/disabled guard modified files'
[IO.File]::Delete($mod)
Move-Item -LiteralPath $disabledMod -Destination $mod
Run-Script '16i-re-enabled-default-already-current' $rollback @('-GameDir',$game)
Run-Script '17-uninstall-after-rollback' $uninstall @('-GameDir',$game)
Check (!(Test-Path -LiteralPath $mod) -and !(Test-Path -LiteralPath $pdb)) 'Uninstall did not remove managed DLL/PDB'
Check ((Hash (Join-Path $game 'version.dll')) -eq $loaderHash) 'Rollback changed loader'
Check ([IO.File]::ReadAllText((Join-Path $game 'save-fixture.es3')) -eq 'SAVE STAYS UNCHANGED') 'Save changed'
Check ([IO.File]::ReadAllText((Join-Path $game 'UserData\BestOffer-rules.json')) -eq '{"rules":"preserve"}') 'Rules changed'
Check ([IO.File]::ReadAllText((Join-Path $game 'UserData\BestOffer-settings.json')) -eq '{"settings":"preserve"}') 'Settings changed'
Check ([IO.File]::ReadAllText((Join-Path $game 'Mods\OtherMod.dll')) -eq 'OTHER MOD STAYS UNCHANGED') 'Other mod changed'
Check ((Hash $oldZip) -eq $oldZipHash) 'Stable release ZIP changed'



# Use a harmless sleeping .NET Framework executable in a separate fixture to
# exercise the exact process-name/path guard without touching the real game.
$runningGame = Join-Path $fixture 'game-running'
[IO.Directory]::CreateDirectory($runningGame) | Out-Null
$runningExe = Join-Path $runningGame 'Probably Stolen.exe'
$compileScript = Join-Path $fixture 'Compile-Sleeper.ps1'
Write-Text $compileScript @'
param([string]$OutputPath)
Add-Type -TypeDefinition 'public static class FixtureSleeper { public static void Main() { System.Threading.Thread.Sleep(60000); } }' -OutputAssembly $OutputPath -OutputType ConsoleApplication
'@
Run-Script '18-compile-harmless-process-fixture' $compileScript @('-OutputPath',$runningExe)
$sleeper = Start-Process -FilePath $runningExe -WindowStyle Hidden -PassThru
try {
    Check (!$sleeper.HasExited) 'Sleeping process fixture failed to start'
    Run-Script '19-running-game-rejected' $rollback @('-GameDir',$runningGame) $false
    Check ([IO.File]::ReadAllText((Join-Path $logs '19-running-game-rejected.log')).Contains('请先退出游戏')) 'Running game was not rejected by process guard'
} finally {
    if (!$sleeper.HasExited) { Stop-Process -Id $sleeper.Id -Force }
    $sleeper.Dispose()
}

$report = [ordered]@{Date=[DateTimeOffset]::Now.ToString('o');RealGameModified=$false;Fixture=$fixture;Assertions=$checks;DefaultTarget='1.4.0';OldZipSha256=$oldZipHash;OldDllSha256=$oldDllHash;OldPdbSha256=$oldPdbHash;Tests=@($results.ToArray())}
Write-Text (Join-Path $PSScriptRoot 'rollback-test-results-1.4.1.json') ($report | ConvertTo-Json -Depth 7)
Write-Host "PASS: $($results.Count) scenarios, $checks assertions; fixture retained at $fixture"
