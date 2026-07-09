$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "KeySounds.csproj"
$output = Join-Path $repoRoot "artifacts\publish\win-x64-portable"

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained true -o $output

$bytes = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMiB = [Math]::Round($bytes / 1MB, 2)
$exe = Join-Path $output "KeySounds.exe"

Write-Host "Portable publish complete."
Write-Host "Output: $output"
Write-Host "Executable: $exe"
Write-Host "Size: $sizeMiB MiB ($bytes bytes)"
