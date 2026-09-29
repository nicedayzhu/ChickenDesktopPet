param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'Pet3D\ChickenDesktopPet3D.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'The desktop project must declare a stable MAJOR.MINOR.PATCH release version.'
}
$output = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path $root 'dist3d'
} else {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
$executable = Join-Path $output 'ChickenDesktopPet3D.exe'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
New-Item -ItemType Directory -Force -Path (Join-Path $root '.nuget\feed') | Out-Null

$resourceSource = Join-Path $root 'Pet3D\Assets\Resources'
$manifestPath = Join-Path $resourceSource 'manifest.json'
$bundlePath = Join-Path $resourceSource 'pet_assets.vpk'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $bundlePath -PathType Leaf)) {
    throw 'Missing bundled resources. Restore Pet3D/Assets/Resources or run scripts/update_pet_assets.ps1 first.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.bundleFile -ne 'pet_assets.vpk' -or
    (Get-FileHash -LiteralPath $bundlePath -Algorithm SHA256).Hash -ne $manifest.bundleSha256) {
    throw 'Asset manifest does not match pet_assets.vpk. Regenerate the resource snapshot before publishing.'
}

$running = Get-Process ChickenDesktopPet3D -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($output + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw '请先从托盘退出正在运行的 3D 小鸡，再构建发布版。'
}

dotnet restore $project --configfile (Join-Path $root 'Pet3D\NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

dotnet publish $project -c Release -r win-x64 --self-contained false --no-restore -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Single-file executable is missing: $executable"
}
foreach ($symbols in Get-ChildItem -LiteralPath $output -File -Filter '*.pdb') {
    [System.IO.File]::Delete($symbols.FullName)
}
$unexpected = @(Get-ChildItem -LiteralPath $output -Force |
    Where-Object Name -NotIn @('ChickenDesktopPet3D.exe', 'Resources'))
if ($unexpected.Count -gt 0) {
    throw "Unexpected files in release directory: $($unexpected.Name -join ', ')"
}
$publishedResources = Join-Path $output 'Resources'
foreach ($name in @('pet_assets.vpk', 'manifest.json')) {
    $sourceFile = Join-Path $resourceSource $name
    $publishedFile = Join-Path $publishedResources $name
    if (-not (Test-Path -LiteralPath $publishedFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $sourceFile).Hash -ne (Get-FileHash -LiteralPath $publishedFile).Hash) {
        throw "Published resource is missing or differs from source: $name"
    }
}
$unexpectedResources = @(Get-ChildItem -LiteralPath $publishedResources -Force |
    Where-Object Name -NotIn @('pet_assets.vpk', 'manifest.json'))
if ($unexpectedResources.Count -gt 0) {
    throw "Unexpected resource files: $($unexpectedResources.Name -join ', ')"
}

$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($executable)
if ("$($fileVersion.FileMajorPart).$($fileVersion.FileMinorPart).$($fileVersion.FileBuildPart)" -ne $version) {
    throw "Published EXE version does not match the project version: $version"
}

$releaseDirectory = Join-Path $root "dist\releases\v$version"
New-Item -ItemType Directory -Force -Path $releaseDirectory | Out-Null
$archiveName = "ChickenDesktopPet3D-v$version-win-x64.zip"
$archive = Join-Path $releaseDirectory $archiveName
$temporaryArchive = Join-Path $releaseDirectory "$archiveName.tmp.zip"
try {
    Compress-Archive -LiteralPath $executable, $publishedResources -DestinationPath $temporaryArchive -Force
    Move-Item -LiteralPath $temporaryArchive -Destination $archive -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
}
$checksumFiles = [ordered]@{
    $archiveName = $archive
    'ChickenDesktopPet3D.exe' = $executable
    'Resources/pet_assets.vpk' = (Join-Path $publishedResources 'pet_assets.vpk')
    'Resources/manifest.json' = (Join-Path $publishedResources 'manifest.json')
}
$checksumLines = foreach ($name in $checksumFiles.Keys) {
    $hash = (Get-FileHash -LiteralPath $checksumFiles[$name] -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name"
}
$checksums = Join-Path $releaseDirectory 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksums, [string[]]$checksumLines, [System.Text.UTF8Encoding]::new($false))

Write-Host "Published: $output"
Write-Host "Release v$version (EXE + Resources): $archive"
Write-Host "SHA256 checksums: $checksums"
