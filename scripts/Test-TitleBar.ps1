param(
    [switch]$LegacyChrome,
    [string]$BuildOutputDirectory = 'artifacts/titlebar-debug'
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$testPath = Join-Path $repoPath ('artifacts/titlebar-' + [Guid]::NewGuid().ToString('N'))

$oldData = $env:CLIPFLOW_TEST_DATA_ROOT
$oldTest = $env:CLIPFLOW_SELF_TEST
$oldChrome = $env:CLIPFLOW_TEST_LEGACY_CHROME

Push-Location $repoPath

try {
    if (Get-Process ClipFlow -ErrorAction SilentlyContinue) {
        throw 'Exit running ClipFlow instances before testing.'
    }

    & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $BuildOutputDirectory
    $exePath = Join-Path ([IO.Path]::GetFullPath($BuildOutputDirectory)) 'ClipFlow.exe'

    $env:CLIPFLOW_TEST_DATA_ROOT = $testPath
    $env:CLIPFLOW_TEST_LEGACY_CHROME = if ($LegacyChrome) {
        '1'
    } else {
        $null
    }

    foreach ($scenario in @('chrome-check', 'tray-check')) {
        $env:CLIPFLOW_SELF_TEST = $scenario
        $testProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

        if (!$testProcess.WaitForExit(30000)) {
            throw 'Titlebar/tray scenario timed out; inspect the isolated directory.'
        }

        $name = if ($scenario -eq 'chrome-check') {
            'mica-results.txt'
        } else {
            'tray-results.txt'
        }

        $report = Get-Content -LiteralPath (Join-Path $testPath $name)
        $report

        if ($testProcess.ExitCode -ne 0 -or $report -notcontains 'SUCCESS') {
            throw 'Titlebar/tray scenario failed.'
        }
    }

    if (Test-Path -LiteralPath (Join-Path $testPath 'titlebar-results.txt')) {
        $report = Get-Content -LiteralPath (Join-Path $testPath 'titlebar-results.txt')
        $report

        if ($report -notcontains 'SUCCESS') {
            throw 'Caption button checks failed.'
        }
    }

    Write-Output "Reports and composed screenshots: $testPath"
} finally {
    $env:CLIPFLOW_TEST_DATA_ROOT = $oldData
    $env:CLIPFLOW_SELF_TEST = $oldTest
    $env:CLIPFLOW_TEST_LEGACY_CHROME = $oldChrome

    Pop-Location
}
