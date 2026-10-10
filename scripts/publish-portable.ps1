$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$project = Join-Path $repoRoot "KeySounds.csproj"
$output = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts\publish\win-x64-portable"))

# Confirm the absolute deletion target stays inside this repository.
if (-not $output.StartsWith($repoRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The publish output must be inside the repository."
}

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw "Portable publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $output "KeySounds.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "The published executable is missing." }
$testFiles = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object { $_.Name -match '^(KeySounds\.Tests|xunit|testhost)' }
if ($testFiles) { throw "Test assemblies must not be included in a portable release." }
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $output "LICENSE")
Copy-Item -LiteralPath (Join-Path $repoRoot "docs\privacy.md") -Destination (Join-Path $output "PRIVACY.md")

$bytes = (Get-ChildItem -LiteralPath $output -Recurse -File | Measure-Object -Property Length -Sum).Sum
$sizeMiB = [Math]::Round($bytes / 1MB, 2)

Write-Host "Portable publish complete."
Write-Host "Output: $output"
Write-Host "Executable: $exe"
Write-Host "Size: $sizeMiB MiB ($bytes bytes)"
