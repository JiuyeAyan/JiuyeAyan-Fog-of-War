param(
  [Parameter(Mandatory = $true)][string]$GameDir,
  [Parameter(Mandatory = $true)][string]$BepInExCoreDir
)

$ErrorActionPreference = 'Stop'
$managedRoot = Join-Path $GameDir 'Stronghold Crusader Definitive Edition_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @(
  (Join-Path $BepInExCoreDir 'BepInEx.dll'),
  (Join-Path $BepInExCoreDir '0Harmony.dll'),
  (Join-Path $managedRoot 'Assembly-CSharp.dll'),
  (Join-Path $managedRoot 'UnityEngine.dll'),
  (Join-Path $managedRoot 'UnityEngine.CoreModule.dll'),
  (Join-Path $managedRoot 'UnityEngine.GridModule.dll'),
  (Join-Path $managedRoot 'UnityEngine.TilemapModule.dll')
)
foreach ($required in (@($compiler) + $references)) {
  if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
    throw "Missing build dependency: $required"
  }
}

$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$buildRoot = Join-Path $PSScriptRoot ('build\' + [Guid]::NewGuid().ToString('N'))
$pluginRoot = Join-Path $buildRoot 'payload\BepInEx\plugins\SC2FogOfWar'
$releaseRoot = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Path $pluginRoot, $releaseRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Destination $buildRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD_PARTY_NOTICES.txt') -Destination $pluginRoot
$packedConfig = Join-Path $PSScriptRoot 'config\SC2FogOfWar.toml'
$compilerArgs = @(
  '/nologo', '/target:library', '/optimize+',
  "/resource:$packedConfig,SCDEFogOfWar.PackedSettings.toml",
  "/out:$pluginRoot\SCDEFogOfWar.dll"
)
$compilerArgs += $references | ForEach-Object { "/reference:$_" }
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler $compilerArgs
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packagePath = Join-Path $releaseRoot ($manifest.name + '-' + $manifest.version + '.scdemod')
if (Test-Path -LiteralPath $packagePath) { throw "Package already exists: $packagePath" }
[System.IO.Compression.ZipFile]::CreateFromDirectory($buildRoot, $packagePath)
Write-Output "Package: $packagePath"
