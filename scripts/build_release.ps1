$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'Pet3D\ChickenDesktopPet3D.csproj'
$output = Join-Path $root 'dist3d'
$executable = Join-Path $output 'ChickenDesktopPet3D.exe'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'

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
$unexpected = @(Get-ChildItem -LiteralPath $output -Force | Where-Object Name -ne 'ChickenDesktopPet3D.exe')
if ($unexpected.Count -gt 0) {
    throw "Unexpected files in release directory: $($unexpected.Name -join ', ')"
}

Write-Host "Published: $output"
