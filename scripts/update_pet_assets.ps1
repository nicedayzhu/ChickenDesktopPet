param(
    [string]$Cs2Vpk = $env:CHICK_CS2_VPK,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Cs2Vpk)) {
    throw '请通过 -Cs2Vpk 指定开发机 CS2 的 pak01_dir.vpk，或设置 CHICK_CS2_VPK。仅资源更新需要游戏安装。'
}
if (-not (Test-Path -LiteralPath $Cs2Vpk -PathType Leaf)) { throw "VPK does not exist: $Cs2Vpk" }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $root 'Pet3D\Assets\Resources'
}
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
New-Item -ItemType Directory -Force -Path (Join-Path $root '.nuget\feed') | Out-Null
$project = Join-Path $PSScriptRoot 'AssetBundle\AssetBundle.csproj'
dotnet restore $project --configfile (Join-Path $root 'Pet3D\NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Asset tool restore failed.' }
dotnet run --project $project -c Release --no-restore -- $Cs2Vpk $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Asset bundle update failed; review missing dependencies before releasing.' }
Write-Host "Updated resource snapshot: $OutputDirectory"
