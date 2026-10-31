param(
    [string]$ManifestPath
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent

if (!$ManifestPath) {
    $ManifestPath = Join-Path $repoPath 'ClipFlow/ClipFlow/Package.appxmanifest'
}

[xml]$manifest = Get-Content -LiteralPath $ManifestPath -Encoding utf8
$storeIdentity = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'StoreIdentity.json') -Raw -Encoding utf8 |
    ConvertFrom-Json
[xml]$version = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/Directory.Build.props')

$ns = [Xml.XmlNamespaceManager]::new($manifest.NameTable)
$ns.AddNamespace('p', $manifest.DocumentElement.NamespaceURI)
$ns.AddNamespace('uap', 'http://schemas.microsoft.com/appx/manifest/uap/windows10')

function Assert-Release([bool]$value, [string]$message) {
    if (!$value) {
        throw $message
    }

    Write-Output ('PASS: ' + $message)
}

$identity = $manifest.Package.Identity

Assert-Release (
    $identity.Name -eq $storeIdentity.Name -and
    $identity.Publisher -in 'CN=OpticsValley', $storeIdentity.Publisher
) 'Identity is the configured local-development or Store identity'

if ($identity.Publisher -eq 'CN=OpticsValley') {
    Write-Output 'NOTE: Local-development publisher; Build-Store.ps1 generates a separate Partner Center manifest.'
}

Assert-Release (
    $identity.Version -match '^\d+\.\d+\.\d+\.\d+$' -and
    $version.Project.PropertyGroup.FileVersion -eq $identity.Version -and
    $version.Project.PropertyGroup.AssemblyVersion -eq $identity.Version
) "Manifest and binary version agree: $($identity.Version)"

$expectedDisplayName = if ($identity.Publisher -eq $storeIdentity.Publisher) {
    $storeIdentity.DisplayName
} else {
    'ClipFlow'
}

$visual = $manifest.SelectSingleNode('//uap:VisualElements', $ns)

Assert-Release (
    $manifest.Package.Properties.DisplayName -ceq $expectedDisplayName -and
    $visual.DisplayName -ceq $expectedDisplayName -and
    $manifest.Package.Properties.PublisherDisplayName -ceq $storeIdentity.PublisherDisplayName
) "Package and application display names match configuration: $expectedDisplayName"

$family = $manifest.Package.Dependencies.TargetDeviceFamily

Assert-Release (
    $family.Name -eq 'Windows.Desktop' -and
    $family.MinVersion -eq '10.0.19041.0'
) 'Windows 10 desktop minimum is build 19041'

$startup=@($manifest.GetElementsByTagName('StartupTask','http://schemas.microsoft.com/appx/manifest/desktop/windows10'))
Assert-Release ($startup.Count -eq 1 -and $startup[0].TaskId -eq 'ClipFlowStartup' -and $startup[0].Enabled -eq 'false' -and $startup[0].ParentNode.Executable -eq 'ClipFlow.exe') 'StartupTask uses the packaged executable, defaults off and remains Windows-managed'

$capabilities = @($manifest.Package.Capabilities.ChildNodes | Where-Object NodeType -eq Element)

Assert-Release (
    $capabilities.Count -eq 1 -and
    $capabilities[0].Name -eq 'runFullTrust'
) 'Only runFullTrust is declared; no network, elevation or extended background capabilities'

$assetNames = @(
    [string]$manifest.Package.Properties.Logo,
    [string]$visual.Square150x150Logo,
    [string]$visual.Square44x44Logo
)
$assetNames += @($visual.DefaultTile.Attributes | Where-Object Name -like '*Logo' | ForEach-Object Value)
$assetNames += [string]$visual.SplashScreen.Image
$appPath = Join-Path $repoPath 'ClipFlow/ClipFlow'

foreach ($asset in $assetNames) {
    $directory = Join-Path $appPath (Split-Path $asset)
    $stem = [IO.Path]::GetFileNameWithoutExtension($asset)
    $exists = (Test-Path -LiteralPath (Join-Path $appPath $asset)) -or
        @(Get-ChildItem -LiteralPath $directory -File -Filter "$stem.scale-*.png").Count -gt 0

    Assert-Release $exists "Manifest asset exists (base or qualified scale): $asset"
}

$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $repoPath 'ClipFlow'), (Join-Path $repoPath 'scripts') -Recurse -File |
    Where-Object {
        $_.Extension -in '.cs', '.xaml', '.csproj', '.props', '.pubxml', '.ps1', '.json' -and
        $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]'
    }

$absolutePaths = @($sourceFiles | Select-String -Pattern '(?i)(?<![a-z0-9_])[A-Z]:[\\/]')
Assert-Release ($absolutePaths.Count -eq 0) 'Source and build scripts contain no hardcoded drive-qualified local paths'

$runtimeFiles = @(
    $sourceFiles | Where-Object {
        $_.Extension -eq '.cs' -and
        $_.FullName -notmatch '[\\/](Diagnostics|ClipFlow.Tests)[\\/]'
    }
)

$network = @(
    $runtimeFiles | Select-String -Pattern '\b(HttpClient|WebClient|HttpWebRequest|TcpClient|UdpClient|Socket|WebSocket)\b|\bSystem\.Net\b|\b(WinHttp|WinInet|InternetOpen|URLDownloadToFile)\b'
)
Assert-Release ($network.Count -eq 0) 'Runtime source scan found no networking/upload clients (not a packet-capture claim)'

foreach ($file in 'LICENSE', 'PRIVACY.md', 'CONTRIBUTING.md', '.gitignore') {
    Assert-Release (Test-Path -LiteralPath (Join-Path $repoPath $file)) "Open-source/release document present: $file"
}
