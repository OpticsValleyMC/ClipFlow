param(
    [Parameter(Mandatory)]
    [string]$UploadPath
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$storeIdentity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StoreIdentity.json') -Raw -Encoding utf8 |
    ConvertFrom-Json
$UploadPath = [IO.Path]::GetFullPath($UploadPath)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$workPath = Join-Path $repoPath ('artifacts/package-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workPath -Force | Out-Null

$report = [Collections.Generic.List[string]]::new()

function Assert-Package([bool]$value, [string]$message) {
    if (!$value) {
        throw $message
    }

    $report.Add('PASS: ' + $message)
}

$upload = [IO.Compression.ZipFile]::OpenRead($UploadPath)

try {
    $packages = @($upload.Entries | Where-Object FullName -match '^[^/\\]+\.msix$')
    Assert-Package ($packages.Count -eq 1) 'Upload ZIP contains one x64 MSIX'

    $symbols = @($upload.Entries | Where-Object FullName -match '\.appxsym$')
    Assert-Package ($symbols.Count -eq 1) 'Upload ZIP contains a symbol package'

    $packageFile = Join-Path $workPath ([IO.Path]::GetFileName($packages[0].FullName))
    [IO.Compression.ZipFileExtensions]::ExtractToFile($packages[0], $packageFile)
} finally {
    $upload.Dispose()
}

$package = [IO.Compression.ZipFile]::OpenRead($packageFile)

try {
    $names = @($package.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })

    foreach ($file in
        'AppxManifest.xml',
        'AppxBlockMap.xml',
        '[Content_Types].xml',
        'resources.pri',
        'ClipFlow.exe',
        'ClipFlow.dll',
        'ClipFlow.Core.dll',
        'Assets/ClipFlow.ico',
        'Assets/ClipFlow.256.png',
        'Licenses/ClipFlow-MIT.txt',
        'Licenses/WindowsAppSDK-LICENSE.txt',
        'Licenses/WinUI-NOTICE.txt',
        'Licenses/dotnet-THIRD-PARTY-NOTICES.txt'
    ) {
        Assert-Package ($names -contains $file) "Required package file exists: $file"
    }

    Assert-Package (
        $names -contains 'coreclr.dll' -and
        $names -contains 'hostfxr.dll' -and
        $names -contains 'Microsoft.UI.Xaml.dll'
    ) 'Self-contained .NET and WinUI runtime binaries are included'

    Assert-Package (
        @(
            $names | Where-Object {
                $_ -match '(?i)(^|/)(history\.db|.*\.(db|sqlite|user|pfx)|integration-results\.txt|ClipFlow\.Tests\.exe|ClipFlow\.(master|reference.*)\.png)$'
            }
        ).Count -eq 0
    ) 'No user history, test executable, signing key or design source image in payload'

    $reader = [IO.StreamReader]::new($package.GetEntry('AppxManifest.xml').Open())

    try {
        [xml]$manifest = $reader.ReadToEnd()
    } finally {
        $reader.Dispose()
    }

    [xml]$source = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/ClipFlow/Package.appxmanifest')

    Assert-Package (
        $manifest.Package.Identity.Name -eq $storeIdentity.Name -and
        $manifest.Package.Identity.Name -eq $source.Package.Identity.Name -and
        $manifest.Package.Identity.Publisher -eq $storeIdentity.Publisher -and
        $manifest.Package.Identity.Version -eq $source.Package.Identity.Version -and
        $manifest.Package.Identity.ProcessorArchitecture -eq 'x64'
    ) 'Packaged identity/version/architecture match Partner Center configuration'

    Assert-Package (
        $manifest.Package.Properties.DisplayName -ceq $storeIdentity.DisplayName -and
        $manifest.Package.Properties.PublisherDisplayName -ceq $storeIdentity.PublisherDisplayName
    ) "Packaged display name matches configured Store reservation: $($storeIdentity.DisplayName)"

    $family = $manifest.Package.Dependencies.TargetDeviceFamily

    Assert-Package (
        $family.Name -eq 'Windows.Desktop' -and
        $family.MinVersion -eq '10.0.19041.0'
    ) 'Packaged OS target is Windows 10 build 19041 or later'

    $caps = @($manifest.Package.Capabilities.ChildNodes | Where-Object NodeType -eq Element)

    Assert-Package (
        $caps.Count -eq 1 -and
        $caps[0].Name -eq 'runFullTrust'
    ) 'Packaged capabilities contain only runFullTrust'

    $startup=@($manifest.GetElementsByTagName('StartupTask','http://schemas.microsoft.com/appx/manifest/desktop/windows10'))
    Assert-Package ($startup.Count -eq 1 -and $startup[0].TaskId -eq 'ClipFlowStartup' -and $startup[0].Enabled -eq 'false' -and $startup[0].ParentNode.Executable -eq 'ClipFlow.exe' -and $names -contains $startup[0].ParentNode.Executable) 'Packaged StartupTask defaults off and resolves to ClipFlow.exe'
    $application = $manifest.Package.Applications.Application
    $visual = $manifest.GetElementsByTagName(
        'VisualElements',
        'http://schemas.microsoft.com/appx/manifest/uap/windows10'
    )[0]

    Assert-Package (
        $visual.DisplayName -ceq $storeIdentity.DisplayName
    ) 'Application display name agrees with the reserved package display name'

    Assert-Package (
        $application.Executable -eq 'ClipFlow.exe' -and
        $application.EntryPoint -eq 'Windows.FullTrustApplication'
    ) 'Executable and full-trust desktop entry point resolve correctly'

    foreach ($node in $manifest.SelectNodes(
        '//@Logo | //@Square150x150Logo | //@Square44x44Logo | //@Wide310x150Logo | //@Square71x71Logo | //@Square310x310Logo | //@Image | //*[local-name()="Properties"]/*[local-name()="Logo"]'
    )) {
        $asset = ([string]$node.get_InnerText()).Replace('\', '/')
        $stem = $asset.Substring(0, $asset.Length - 4)
        $found = ($names -contains $asset) -or @(
            $names | Where-Object {
                $_.StartsWith($stem + '.scale-') -and $_.EndsWith('.png')
            }
        ).Count -gt 0

        Assert-Package $found "Packaged manifest logo resolves: $asset"
    }

    $manifest.Save((Join-Path $workPath 'verified-AppxManifest.xml'))
} finally {
    $package.Dispose()
}

$nugetRoot = if ($env:NUGET_PACKAGES) {
    $env:NUGET_PACKAGES
} else {
    Join-Path $env:USERPROFILE '.nuget/packages'
}

$sdkPackageRoot = Join-Path $nugetRoot 'microsoft.windows.sdk.buildtools'
$makeAppx = Get-ChildItem -LiteralPath $sdkPackageRoot -Recurse -File -Filter 'makeappx.exe' |
    Where-Object FullName -match '[\\/]x64[\\/]' |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (!$makeAppx) {
    throw 'Windows SDK MakeAppx.exe was not found in restored BuildTools.'
}

& $makeAppx.FullName unpack /p $packageFile /d (Join-Path $workPath 'unpacked') /o |
    Out-File (Join-Path $workPath 'makeappx-validation.txt') -Encoding utf8

Assert-Package ($LASTEXITCODE -eq 0) 'Windows SDK MakeAppx unpack/validation succeeds without /nv'

$report.Add('SHA256: ' + (Get-FileHash -LiteralPath $UploadPath -Algorithm SHA256).Hash)
$report.Add('SCOPE: structural validation only; not Partner Center acceptance or WACK certification.')
$report.Add('SUCCESS')

$report | Set-Content -LiteralPath (Join-Path $workPath 'store-package-results.txt') -Encoding utf8
$report

Write-Output "Package validation report: $workPath/store-package-results.txt"
