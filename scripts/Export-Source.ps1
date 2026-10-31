$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$workPath = Join-Path $repoPath ('artifacts/source-export-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workPath -Force | Out-Null

# A throwaway Git directory evaluates .gitignore without initializing/changing this checkout.
git init --quiet $workPath

if ($LASTEXITCODE -ne 0) {
    throw 'Git is required to export source using .gitignore.'
}

$gitPath = Join-Path $workPath '.git'
$files = @(git -c core.quotePath=false "--git-dir=$gitPath" "--work-tree=$repoPath" ls-files --others --exclude-standard)

if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) {
    throw 'Could not enumerate distributable source.'
}

[xml]$versionProps=Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/Directory.Build.props')
$archivePath = Join-Path $workPath ("ClipFlow-$($versionProps.Project.PropertyGroup.FileVersion)-source.zip")

Add-Type -AssemblyName System.IO.Compression.FileSystem

$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)

try {
    foreach ($file in $files) {
        $sourcePath = Join-Path $repoPath $file

        if ($file -match '(?i)(^|/)(bin|obj|artifacts|\.git|\.vs|AppPackages)(/|$)|\.(db|pfx|user)$') {
            throw "Unexpected private/build file: $file"
        }

        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $sourcePath,
            $file.Replace('\', '/'),
            [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
} finally {
    $archive.Dispose()
}

$files | Set-Content -LiteralPath (Join-Path $workPath 'source-files.txt') -Encoding utf8

Write-Output "Source archive: $archivePath"
Write-Output "Files: $($files.Count); SHA256: $((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash)"
