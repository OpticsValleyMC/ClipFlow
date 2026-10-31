param(
    [string]$Executable = 'artifacts/publish/win-x64/ClipFlow.exe'
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$assetPath = Join-Path $repoPath 'ClipFlow/ClipFlow/Assets'
$report = [Collections.Generic.List[string]]::new()

function Assert-Brand([bool]$condition, [string]$message) {
    if (!$condition) {
        throw $message
    }

    $report.Add('PASS: ' + $message)
}

Add-Type -AssemblyName System.Drawing

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $repoPath $Executable))
[xml]$props = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/Directory.Build.props')
$expectedVersion=[string]$props.Project.PropertyGroup.FileVersion

Assert-Brand (
    $version.FileVersion -eq $expectedVersion -and
    $version.ProductVersion -eq $expectedVersion
) "Executable file/product version is exactly $expectedVersion"

Assert-Brand (
    $version.CompanyName -eq 'OpticsValley' -and
    $version.ProductName -eq 'ClipFlow'
) 'Executable author/company is OpticsValley and product is ClipFlow'

[xml]$props = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/Directory.Build.props')

Assert-Brand (
    $props.Project.PropertyGroup.Authors -eq 'OpticsValley' -and
    $props.Project.PropertyGroup.AssemblyVersion -eq $expectedVersion
) 'Project author and assembly version configured centrally'

[xml]$manifest = Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/ClipFlow/Package.appxmanifest')

Assert-Brand (
    $manifest.Package.Identity.Version -eq $expectedVersion -and
    $manifest.Package.Properties.PublisherDisplayName -eq 'OpticsValley'
) 'Package manifest version and publisher display name match'

$master = [Drawing.Bitmap]::new((Join-Path $assetPath 'ClipFlow.master.png'))

try {
    Assert-Brand (
        $master.Width -eq $master.Height -and
        $master.Width -ge 1024 -and
        $master.GetPixel(0, 0).A -eq 0 -and
        $master.GetPixel([int]($master.Width / 2), [int]($master.Height / 2)).A -eq 0
    ) 'ImageGen master has square high-resolution canvas and transparent exterior/interior'
} finally {
    $master.Dispose()
}

$count = 0

foreach ($scale in 100, 125, 150, 200, 250, 300, 400) {
    foreach ($pair in @(
        @('Square44x44Logo', 44, 44),
        @('Square71x71Logo', 71, 71),
        @('Square150x150Logo', 150, 150),
        @('Square310x310Logo', 310, 310),
        @('StoreLogo', 50, 50),
        @('Wide310x150Logo', 310, 150),
        @('SplashScreen', 620, 300)
    )) {
        $imageFile = Join-Path $assetPath ($pair[0] + '.scale-' + $scale + '.png')
        $bitmap = [Drawing.Bitmap]::new($imageFile)

        try {
            $expectedWidth = [int][Math]::Round($pair[1] * $scale / 100, [MidpointRounding]::AwayFromZero)
            $expectedHeight = [int][Math]::Round($pair[2] * $scale / 100, [MidpointRounding]::AwayFromZero)

            if (
                $bitmap.Width -ne $expectedWidth -or
                $bitmap.Height -ne $expectedHeight -or
                $bitmap.GetPixel(0, 0).A -ne 0
            ) {
                throw "Invalid dimensions/transparency: $imageFile"
            }

            $count++
        } finally {
            $bitmap.Dispose()
        }
    }
}

Assert-Brand ($count -eq 49) '49 package PNG assets have correct 100-400% dimensions and alpha'

$targetCount = 0

foreach ($size in 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 128, 256) {
    foreach ($suffix in '', '_altform-unplated', '_altform-lightunplated') {
        $bitmap = [Drawing.Bitmap]::new((Join-Path $assetPath "Square44x44Logo.targetsize-$size$suffix.png"))

        try {
            if (
                $bitmap.Width -ne $size -or
                $bitmap.Height -ne $size -or
                $bitmap.GetPixel(0, 0).A -ne 0
            ) {
                throw 'Invalid taskbar target-size asset'
            }

            $targetCount++
        } finally {
            $bitmap.Dispose()
        }
    }
}

Assert-Brand ($targetCount -eq 45) '45 exact-size default/dark/light taskbar variants match and preserve alpha'

$bytes = [IO.File]::ReadAllBytes((Join-Path $assetPath 'ClipFlow.ico'))

Assert-Brand (
    [BitConverter]::ToUInt16($bytes, 2) -eq 1 -and
    [BitConverter]::ToUInt16($bytes, 4) -eq 15
) 'ICO contains 15 resolutions from 16 to 256 pixels'

$ico = [Drawing.Icon]::new((Join-Path $assetPath 'ClipFlow.ico'), 32, 32)

try {
    Assert-Brand ($ico.Width -eq 32 -and $ico.Height -eq 32) 'Windows decodes the multi-size ICO'
} finally {
    $ico.Dispose()
}

$report.Add('SUCCESS')
$report | Set-Content -LiteralPath (Join-Path $repoPath 'docs/brand-results.txt') -Encoding utf8
$report
