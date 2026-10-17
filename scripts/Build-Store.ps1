param(
    [ValidateSet('x64')]
    [string]$Platform = 'x64',
    [string]$MSBuildPath
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$projectPath = Join-Path $repoPath 'ClipFlow/ClipFlow/ClipFlow.csproj'
$env:MSBuildEnableWorkloadResolver = 'false'

if (!$MSBuildPath) {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'

    if (!(Test-Path -LiteralPath $vswherePath)) {
        throw 'Install Visual Studio with WinUI / single-project MSIX tools, or supply -MSBuildPath.'
    }

    $MSBuildPath = & $vswherePath -latest -products '*' -requires Microsoft.Component.MSBuild `
        -find 'MSBuild/**/Bin/MSBuild.exe' |
        Select-Object -First 1
}

if (!$MSBuildPath -or !(Test-Path -LiteralPath $MSBuildPath)) {
    throw 'Visual Studio MSBuild.exe was not found. dotnet build alone is not the Store packaging toolchain.'
}

# A fresh destination prevents an old upload file from being mistaken for this build.
$packagePath = Join-Path $repoPath ('artifacts/store-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $packagePath -Force | Out-Null

$storeManifestPath = Join-Path $packagePath 'Package.Store.appxmanifest'
[xml]$storeManifest = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/ClipFlow/Package.appxmanifest') -Encoding utf8
$storeIdentity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StoreIdentity.json') -Raw -Encoding utf8 |
    ConvertFrom-Json

$storeManifest.Package.Identity.Name = $storeIdentity.Name
$storeManifest.Package.Identity.Publisher = $storeIdentity.Publisher
$storeManifest.Package.Properties.DisplayName = $storeIdentity.DisplayName
$storeManifest.Package.Properties.PublisherDisplayName = $storeIdentity.PublisherDisplayName
$storeVisual = $storeManifest.GetElementsByTagName(
    'VisualElements',
    'http://schemas.microsoft.com/appx/manifest/uap/windows10'
)[0]
$storeVisual.SetAttribute('DisplayName', $storeIdentity.DisplayName)
$storeManifest.Save($storeManifestPath)

& (Join-Path $PSScriptRoot 'Test-ReleaseReadiness.ps1') -ManifestPath $storeManifestPath

$buildPath = Join-Path $packagePath 'build'
$packageOutput = Join-Path $packagePath 'packages'
$buildArguments = @(
    $projectPath,
    '/restore',
    '/t:Build',
    '/m',
    '/nologo',
    '/v:minimal',
    '/p:Configuration=Release',
    "/p:Platform=$Platform",
    '/p:RuntimeIdentifier=win-x64',
    '/p:WindowsPackageType=MSIX',
    '/p:GenerateAppxPackageOnBuild=true',
    "/p:StoreManifestPath=$storeManifestPath",
    '/p:UapAppxPackageBuildMode=StoreUpload',
    '/p:AppxBundle=Never',
    '/p:AppxPackageSigningEnabled=false',
    '/p:AppxSymbolPackageEnabled=true',
    "/p:AppxPackageDir=$packageOutput/",
    "/p:OutputPath=$buildPath/",
    "/bl:$packagePath/store-build.binlog"
)

$installationPath = Split-Path (
    Split-Path (
        Split-Path (
            Split-Path $MSBuildPath -Parent
        ) -Parent
    ) -Parent
) -Parent
$vcToolsPath = Join-Path $installationPath 'VC/Tools/MSVC'
$symbolTool = if (Test-Path -LiteralPath $vcToolsPath) {
    Get-ChildItem -LiteralPath $vcToolsPath -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'bin/Hostx64/x64/mspdbcmf.exe' } |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1
}

if (!$symbolTool) {
    throw 'Install the Visual Studio C++ build tools (mspdbcmf.exe) to generate Store upload symbols.'
}

$buildArguments += "/p:MsPdbCmfExeFullpath=$symbolTool"
$buildArguments += "/p:PdbCmfx64ExeFullPath=$symbolTool"
$buildArguments += "/p:PdbCmfx86ExeFullPath=$symbolTool"

& $MSBuildPath @buildArguments

if ($LASTEXITCODE -ne 0) {
    throw "Store package build failed; inspect $packagePath/store-build.binlog"
}

$uploads = @(Get-ChildItem -LiteralPath $packageOutput -Recurse -File -Filter '*.msixupload')

if ($uploads.Count -ne 1) {
    throw 'Expected exactly one fresh .msixupload; do not upload a portable publish folder.'
}

& (Join-Path $PSScriptRoot 'Test-StorePackage.ps1') -UploadPath $uploads[0].FullName

Write-Output "Store upload file: $($uploads[0].FullName)"
Write-Output 'Package structure verified. Partner Center validation and Windows App Certification Kit still require the actual submission/install environment.'
