[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$root = Join-Path $PSScriptRoot ('manager-installer-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $root 'game'
$loader = Join-Path $root 'loader'
function Put($Path, $Value) { [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null; [IO.File]::WriteAllText($Path, $Value) }
function Assert($Value, $Message) { if (!$Value) { throw $Message } }
function Reject([scriptblock]$Action) { $rejected=$false; try { & $Action | Out-Null } catch { $rejected=$true }; Assert $rejected 'Expected rejection' }
Put (Join-Path $game 'Probably Stolen.exe') 'fixture'
Put (Join-Path $game 'Mods\WarehouseSort.dll') 'preserve-other-mod'
Put (Join-Path $game 'UserData\BestOffer-rules.json') '{"protected":2}'
Put (Join-Path $loader 'version.dll') 'loader-proxy'
Put (Join-Path $loader 'MelonLoader\net6\MelonLoader.dll') 'loader-runtime'
$v1=Join-Path $root 'v1\BestOffer.dll'; Put $v1 'v1'; Put ([IO.Path]::ChangeExtension($v1,'.pdb')) 'pdb1'
$v2=Join-Path $root 'v2\BestOffer.dll'; Put $v2 'v2'; Put ([IO.Path]::ChangeExtension($v2,'.pdb')) 'pdb2'
$install=Join-Path $repo 'scripts\Install.ps1'; $uninstall=Join-Path $repo 'scripts\Uninstall.ps1'
& $install -GameDir $game -ModSource $v1 -LoaderSource $loader
$active=Join-Path $game 'Mods\BestOffer.dll'; $disabled=Join-Path $game 'Mods\Disabled\BestOffer.dll'
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($disabled)) | Out-Null
Move-Item -LiteralPath $active -Destination $disabled
& $install -GameDir $game -ModSource $v2 -LoaderSource $loader
Assert (!(Test-Path -LiteralPath $active) -and [IO.File]::ReadAllText($disabled) -eq 'v2') 'Disabled update reenabled or failed'
Put $active 'duplicate'
Reject { & $install -GameDir $game -ModSource $v1 -LoaderSource $loader }
Reject { & $uninstall -GameDir $game }
Remove-Item -LiteralPath $active
& $uninstall -GameDir $game
Assert (!(Test-Path -LiteralPath $disabled)) 'Disabled DLL survived uninstall'
Assert ([IO.File]::ReadAllText((Join-Path $game 'Mods\WarehouseSort.dll')) -eq 'preserve-other-mod') 'Other mod changed'
Assert ([IO.File]::ReadAllText((Join-Path $game 'UserData\BestOffer-rules.json')) -eq '{"protected":2}') 'Rules changed'
Write-Host 'PASS: disabled update/uninstall, duplicate protection, unrelated Mod and rules preservation.'
