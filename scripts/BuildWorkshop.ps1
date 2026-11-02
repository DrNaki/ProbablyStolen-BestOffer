[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen',
    [switch]$SkipTests
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& python (Join-Path $PSScriptRoot 'PrepareWorkshop.py')
if ($LASTEXITCODE -ne 0) { throw 'Workshop source preparation failed.' }
if (!$SkipTests) {
    foreach ($testProject in @('NativeLocalization.Tests', 'TradeHotkeys.Tests', 'WorkshopSolver.Tests', 'WorkshopCategories.Tests', 'WorkshopHotkeys.Tests', 'WorkshopInventorySources.Tests')) {
        & dotnet run --project (Join-Path $projectRoot "tests\$testProject\$testProject.csproj") -c Release "-p:GameDir=$GameDir"
        if ($LASTEXITCODE -ne 0) { throw "$testProject failed." }
    }
}
& dotnet build (Join-Path $projectRoot 'src\BestOffer.Workshop\BestOffer.Workshop.csproj') -c Release --nologo "-p:GameDir=$GameDir" '-p:ContinuousIntegrationBuild=true' "-p:PathMap=$projectRoot=/_/BestOffer"
if ($LASTEXITCODE -ne 0) { throw 'Workshop build failed.' }
$contentRoot = Join-Path $projectRoot 'workshop-release\1.7.3\content'
New-Item -ItemType Directory -Path $contentRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\BestOffer.Workshop\bin\Release\netstandard2.1\BestOffer.dll') -Destination (Join-Path $contentRoot 'BestOffer.dll') -Force
Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\BestOffer.Workshop\package') -File | Copy-Item -Destination $contentRoot -Force
$files = @(Get-ChildItem -LiteralPath $contentRoot -File | ForEach-Object {
    [pscustomobject]@{ File = $_.Name; Bytes = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
})
[pscustomobject]@{ Version = '1.7.3'; ContentRoot = $contentRoot; Files = $files } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot 'workshop-release\1.7.3\manifest.json') -Encoding utf8
Write-Host "Workshop package prepared: $contentRoot"
Write-Host 'The build does not install or publish the mod. Nexus 1.6.1 files are retained.'
