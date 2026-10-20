[CmdletBinding()]
param(
    [string]$GameDir = 'E:\Steam\steamapps\common\Probably Stolen Demo',
    [ValidateSet('Release', 'Debug')][string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Reproducible
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $OutputEncoding
$projectDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$gameRoot = [IO.Path]::GetFullPath($GameDir)
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '未找到 dotnet，请先安装 .NET 8 SDK。' }
if (!(Test-Path -LiteralPath (Join-Path $gameRoot 'MelonLoader\Il2CppAssemblies\Assembly-CSharp.dll') -PathType Leaf)) {
    throw '缺少游戏互操作程序集：请先安装 MelonLoader 并启动游戏一次，等待生成完成后再构建。'
}
if (!$SkipTests) {
    Write-Host '正在验证报价求解器……'
    & dotnet run --project (Join-Path $projectDir 'tests\OfferSolver.Tests.csproj') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "求解器测试失败，退出码 $LASTEXITCODE。" }
    Write-Host '正在验证同名商品分组与数量规则……'
    & dotnet run --project (Join-Path $projectDir 'tests\ItemRuleGroups.Tests\ItemRuleGroups.Tests.csproj') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "同名商品规则测试失败，退出码 $LASTEXITCODE。" }
    Write-Host '正在验证自动禁卖保护与买家明确需求……'
    & dotnet run --project (Join-Path $projectDir 'tests\RiskPolicy.Tests\RiskPolicy.Tests.csproj') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "自动禁卖保护测试失败，退出码 $LASTEXITCODE。" }
    Write-Host '正在验证机器货源、产物槽与整件包装保护……'
    & dotnet run --project (Join-Path $projectDir 'tests\InventorySourcePolicy.Tests\InventorySourcePolicy.Tests.csproj') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "货源策略测试失败，退出码 $LASTEXITCODE。" }
    Write-Host '正在验证分类规则优先级、有效期与跨存档隔离……'
    & dotnet run --project (Join-Path $projectDir 'tests\CategoryRules.Tests\CategoryRules.Tests.csproj') --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "分类规则测试失败，退出码 $LASTEXITCODE。" }
}
Write-Host '正在构建最佳报价 Mod……'
$buildArguments = @('build', (Join-Path $projectDir 'src\BestOffer\BestOffer.csproj'), '--configuration', $Configuration, '--nologo', "-p:GameDir=$gameRoot")
if ($Reproducible) {
    $buildArguments += '-p:ContinuousIntegrationBuild=true'
    $buildArguments += "-p:PathMap=$projectDir=/_/BestOffer"
}
& dotnet @buildArguments
if ($LASTEXITCODE -ne 0) { throw "Mod 构建失败，退出码 $LASTEXITCODE。" }
$outputDir = Join-Path $projectDir ("src\BestOffer\bin\" + $Configuration + '\net6.0')
$distDir = Join-Path $projectDir 'dist'
[IO.Directory]::CreateDirectory($distDir) | Out-Null
Copy-Item -LiteralPath (Join-Path $outputDir 'BestOffer.dll') -Destination (Join-Path $distDir 'BestOffer.dll') -Force
if (Test-Path -LiteralPath (Join-Path $outputDir 'BestOffer.pdb') -PathType Leaf) {
    Copy-Item -LiteralPath (Join-Path $outputDir 'BestOffer.pdb') -Destination (Join-Path $distDir 'BestOffer.pdb') -Force
}
$algorithm = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead((Join-Path $distDir 'BestOffer.dll'))
try { $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '') }
finally { $stream.Dispose(); $algorithm.Dispose() }
Write-Host "构建完成：$(Join-Path $distDir 'BestOffer.dll')"
Write-Host "SHA256：$hash"
Write-Host '构建不会部署到游戏。请关闭游戏后运行 scripts\Install.ps1。'
