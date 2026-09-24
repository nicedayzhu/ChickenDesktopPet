$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'Pet3D\ChickenDesktopPet3D.csproj'
$output = Join-Path $root 'dist3d'
$package = Join-Path $root 'ChickenDesktopPet3D-win-x64.zip'
$temporaryPackage = Join-Path $root 'ChickenDesktopPet3D-win-x64.zip.tmp'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'

$running = Get-Process ChickenDesktopPet3D -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($output + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw '请先从托盘退出正在运行的 3D 小鸡，再构建发布版。'
}

dotnet restore $project --configfile (Join-Path $root 'Pet3D\NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'NuGet restore failed.' }

dotnet publish $project -c Release --self-contained false --no-restore -o $output
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

& (Join-Path $PSScriptRoot 'prune_dist3d.ps1') -Output $output

Add-Type -AssemblyName System.IO.Compression
if (Test-Path -LiteralPath $temporaryPackage) {
    throw "Temporary package already exists: $temporaryPackage"
}
try {
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $output, $temporaryPackage, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    [System.IO.File]::Move($temporaryPackage, $package, $true)
}
finally {
    if (Test-Path -LiteralPath $temporaryPackage) {
        [System.IO.File]::Delete($temporaryPackage)
    }
}

Write-Host "Published: $output"
Write-Host "Package:   $package"
