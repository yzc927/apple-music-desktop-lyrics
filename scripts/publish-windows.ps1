param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $artifactsRoot "windows-x64"
}
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$artifactBoundary = $artifactsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $outputPath.StartsWith($artifactBoundary, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Output directory must stay under ${artifactsRoot}: $outputPath"
}

$project = Join-Path $repositoryRoot "windows\AppleMusicDesktopLyrics.csproj"
dotnet restore $project -r win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory must be a new path; refusing to overwrite: $outputPath"
}
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputPath | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true --no-restore `
    -p:PublishSingleFile=true -o $outputPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$expectedExecutable = Join-Path $outputPath "AppleMusicDesktopLyrics.exe"
$publishedFiles = @(Get-ChildItem -LiteralPath $outputPath -File -Force)
if ($publishedFiles.Count -ne 1 -or
    -not (Test-Path -LiteralPath $expectedExecutable -PathType Leaf)) {
    throw "Publish directory must contain only AppleMusicDesktopLyrics.exe"
}

$hash = (Get-FileHash -LiteralPath $expectedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $outputPath "AppleMusicDesktopLyrics-win-x64.sha256"
Set-Content -LiteralPath $checksumPath -Encoding ascii `
    -Value "$hash  AppleMusicDesktopLyrics.exe"

$verifiedHash = (Get-FileHash -LiteralPath $expectedExecutable -Algorithm SHA256).Hash.ToLowerInvariant()
if ($verifiedHash -ne $hash) { throw "Published executable changed during checksum creation" }
Write-Host "Checksum file: $checksumPath"

Write-Host "Windows x64 single-file publish completed: $expectedExecutable"
Write-Host "SHA-256: $hash"
