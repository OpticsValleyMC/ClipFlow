param(
    [string]$BuildOutputDirectory,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
if (!$BuildOutputDirectory) {
    $BuildOutputDirectory = Join-Path $repoPath 'artifacts/tray-debug'
}
$buildPath = [IO.Path]::GetFullPath($BuildOutputDirectory)
if (!$SkipBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $buildPath
}

$testPath = Join-Path $repoPath ('artifacts/tray-' + [Guid]::NewGuid().ToString('N'))
$oldData = $env:CLIPFLOW_TEST_DATA_ROOT
$oldTest = $env:CLIPFLOW_SELF_TEST
try {
    $env:CLIPFLOW_TEST_DATA_ROOT = $testPath
    $env:CLIPFLOW_SELF_TEST = 'tray-check'
    $fixture = Start-Process -FilePath (Join-Path $buildPath 'ClipFlow.exe') -PassThru -WindowStyle Hidden
    if (!$fixture.WaitForExit(30000)) {
        $fixture.Kill()
        throw "Tray test timed out. Reports: $testPath"
    }
    $report = Get-Content -LiteralPath (Join-Path $testPath 'tray-results.txt')
    $report
    if ($report -notcontains 'SUCCESS' -or $fixture.ExitCode -ne 0) {
        throw "Tray test failed. Reports: $testPath"
    }
    Write-Output 'PASS: tray Exit drains pending saves and terminates the isolated process'
    Write-Output "Tray screenshots and report: $testPath"
} finally {
    $env:CLIPFLOW_TEST_DATA_ROOT = $oldData
    $env:CLIPFLOW_SELF_TEST = $oldTest
}
