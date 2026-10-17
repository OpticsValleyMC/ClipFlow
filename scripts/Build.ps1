param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64',
    [switch]$Run,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoPath 'ClipFlow\ClipFlow\ClipFlow.csproj'
$env:MSBuildEnableWorkloadResolver = 'false'

$buildArguments = @(
    'build',
    $projectPath,
    '-c',
    $Configuration,
    "-p:Platform=$Platform",
    '-p:WindowsPackageType=None'
)

if ($OutputDirectory) {
    $buildArguments += "-p:OutputPath=$([IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/'))/"
}

dotnet @buildArguments

if ($LASTEXITCODE -ne 0) {
    throw 'ClipFlow build failed.'
}

if ($Run) {
    $runtimeName = if ($Platform -eq 'ARM64') {
        'win-arm64'
    } else {
        'win-' + $Platform
    }

    $exePath = Join-Path $repoPath `
        "ClipFlow\ClipFlow\bin\$Platform\$Configuration\net8.0-windows10.0.22621.0\$runtimeName\ClipFlow.exe"

    if ($OutputDirectory) {
        $exePath = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'ClipFlow.exe'
    }

    # Explicit -Run asks to show the interactive application.
    Start-Process -FilePath $exePath
}
