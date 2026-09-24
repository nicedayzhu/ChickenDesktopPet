param(
    [Parameter(Mandatory=$true)][string]$VrfCli,
    [Parameter(Mandatory=$true)][string]$Cs2Vpk
)

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'assets\chick_selected.glb'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
$animations = 'chick_idle01,chick_idle02,chick_idle03,chick_walk,chick_run,chick_react01,chick_react02,chick_trick01,chick_trick02,chick_sleep_loop01,chick_feed01,chickbaby_feed02'
& $VrfCli -i $Cs2Vpk -f 'models/chicken/chick.vmdl_c' -o $output -d --gltf_export_format glb --gltf_export_materials --gltf_export_animations --gltf_animation_list $animations
if ($LASTEXITCODE -ne 0) { throw "ValveResourceFormat export failed: $LASTEXITCODE" }
Write-Output "Exported $output"
