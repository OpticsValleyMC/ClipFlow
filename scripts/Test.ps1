param(
    [switch]$Integration,
    [string]$BuildOutputDirectory,
    [switch]$LegacyChrome
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$env:MSBuildEnableWorkloadResolver = 'false'

Push-Location $repoPath

try {
    dotnet run --project 'ClipFlow\ClipFlow.Tests\ClipFlow.Tests.csproj'

    if ($LASTEXITCODE -ne 0) {
        throw 'Core tests failed.'
    }

    if ($Integration) {
        & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $BuildOutputDirectory

        $testPath = Join-Path $repoPath ('artifacts\integration-' + [Guid]::NewGuid().ToString('N'))

        $oldData = $env:CLIPFLOW_TEST_DATA_ROOT
        $oldTest = $env:CLIPFLOW_SELF_TEST
        $oldHelper = $env:CLIPFLOW_LOCK_HELPER
        $oldChrome = $env:CLIPFLOW_TEST_LEGACY_CHROME

        try {
            $env:CLIPFLOW_TEST_DATA_ROOT = $testPath
            $env:CLIPFLOW_TEST_LEGACY_CHROME = if ($LegacyChrome) {
                '1'
            } else {
                $null
            }
            $env:CLIPFLOW_SELF_TEST = '1'
            $env:CLIPFLOW_LOCK_HELPER = Join-Path $repoPath `
                'ClipFlow\ClipFlow.Tests\bin\Debug\net8.0\ClipFlow.Tests.exe'

            $exePath = Join-Path $repoPath `
                'ClipFlow\ClipFlow\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\ClipFlow.exe'

            if ($BuildOutputDirectory) {
                $exePath = Join-Path ([IO.Path]::GetFullPath($BuildOutputDirectory)) 'ClipFlow.exe'
            }

            $testProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

            if (!$testProcess.WaitForExit(60000)) {
                $null = $testProcess.CloseMainWindow()
                throw 'Integration test timed out. Check the isolated data directory.'
            }

            $reportPath = Join-Path $testPath 'integration-results.txt'

            if (!(Test-Path -LiteralPath $reportPath)) {
                throw 'App exited before writing its integration report.'
            }

            $report = Get-Content -LiteralPath $reportPath
            $report

            if ($report -notcontains 'SUCCESS') {
                throw 'Integration tests failed.'
            }

            $env:CLIPFLOW_SELF_TEST = 'restart-check'
            $restartProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

            if (!$restartProcess.WaitForExit(15000)) {
                $null = $restartProcess.CloseMainWindow()
                throw 'Restart check timed out.'
            }

            $restartReport = Get-Content -LiteralPath (Join-Path $testPath 'restart-results.txt')
            $restartReport

            if ($restartReport -notcontains 'SUCCESS') {
                throw 'Process restart test failed.'
            }

            $env:CLIPFLOW_SELF_TEST = 'tray-check'
            $trayProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

            if (!$trayProcess.WaitForExit(25000)) {
                throw 'Tray test timed out. Check the isolated data directory.'
            }

            $trayReport = Get-Content -LiteralPath (Join-Path $testPath 'tray-results.txt')
            $trayReport

            if ($trayReport -notcontains 'SUCCESS' -or $trayProcess.ExitCode -ne 0) {
                throw 'Tray/background/exit test failed.'
            }

            Write-Output 'PASS: native tray Exit command drains pending saves and terminates the process'
            Write-Output "UI renders and test report: $testPath"
        } finally {
            $env:CLIPFLOW_TEST_DATA_ROOT = $oldData
            $env:CLIPFLOW_SELF_TEST = $oldTest
            $env:CLIPFLOW_LOCK_HELPER = $oldHelper
            $env:CLIPFLOW_TEST_LEGACY_CHROME = $oldChrome
        }
    }
} finally {
    Pop-Location
}
