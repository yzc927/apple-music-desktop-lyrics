param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
$resolvedExecutable = [System.IO.Path]::GetFullPath($ExecutablePath)
if (-not (Test-Path -LiteralPath $resolvedExecutable -PathType Leaf)) {
    throw "Release executable not found: $resolvedExecutable"
}

$process = Start-Process -FilePath $resolvedExecutable -ArgumentList "--self-test" `
    -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -ne 0) {
    throw "Release self-test failed with exit code $($process.ExitCode)"
}
Write-Host "Release self-test passed: $resolvedExecutable"
