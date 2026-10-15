[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path $PSScriptRoot -Parent
[xml]$buildProps = Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$buildProps.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Directory.Build.props must contain a stable three-part Version.'
}

$packageName = "Egorik-$version-win-x64"
$releaseRoot = Join-Path $repoRoot 'artifacts/releases'
# A fresh staging directory prevents stale files from entering a new release.
$stagingRoot = Join-Path $repoRoot ("artifacts/staging/" + [Guid]::NewGuid().ToString('N'))
$publishPath = Join-Path $stagingRoot $packageName
New-Item -ItemType Directory -Path $publishPath -Force | Out-Null
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null

& dotnet publish (Join-Path $repoRoot 'src/Egorik.App/Egorik.App.csproj') `
    -p:PublishProfile=WindowsPortable -c Release -o $publishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }

$requiredFiles = @(
    'Egorik.exe', 'Egorik.dll', 'Egorik.runtimeconfig.json', 'coreclr.dll',
    'hostfxr.dll', 'libvosk.dll', 'sherpa-onnx-c-api.dll', 'onnxruntime.dll',
    'plugins/egorik.dota2.hero-picker/plugin.json',
    'plugins/egorik.dota2.hero-picker/plugin.js',
    'plugins/egorik.dota2.hero-picker/heroes.json'
)
foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishPath $relativePath) -PathType Leaf)) {
        throw "Missing release dependency: $relativePath"
    }
}

Copy-Item -LiteralPath (Join-Path $repoRoot 'docs/QUICKSTART.txt') -Destination $publishPath
Copy-Item -LiteralPath (Join-Path $repoRoot 'CHANGELOG.md') -Destination $publishPath
$archivePath = Join-Path $releaseRoot "$packageName.zip"
Compress-Archive -LiteralPath $publishPath -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $packageName.zip" | Set-Content -LiteralPath "$archivePath.sha256" -Encoding ascii
Write-Host "Release: $archivePath"
Write-Host "SHA256:  $hash"
Write-Host "Executable: $(Join-Path $publishPath 'Egorik.exe')"
