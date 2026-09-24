param([string]$Output = (Join-Path $PSScriptRoot '..\dist3d'))

$root = [System.IO.Path]::GetFullPath($Output)
$runtimeRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'runtimes'))
if (-not $runtimeRoot.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Runtime directory escapes the publish directory.'
}
if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot 'win-x64'))) {
    throw 'win-x64 runtime assets are missing; refusing to prune.'
}

foreach ($directory in Get-ChildItem -LiteralPath $runtimeRoot -Directory) {
    if ($directory.Name -eq 'win-x64') { continue }
    $target = [System.IO.Path]::GetFullPath($directory.FullName)
    if (-not $target.StartsWith($runtimeRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Directory escapes runtime root: $target"
    }
    Remove-Item -LiteralPath $target -Recurse -Force
}

foreach ($symbols in Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.pdb') {
    $target = [System.IO.Path]::GetFullPath($symbols.FullName)
    if (-not $target.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Symbols file escapes publish root: $target"
    }
    Remove-Item -LiteralPath $target -Force
}
