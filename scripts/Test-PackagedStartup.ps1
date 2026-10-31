$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
$testName='OpticsValley.ClipFlow.StartupAcceptance'
if(Get-AppxPackage -Name $testName){throw 'An acceptance package with this identity already exists; inspect/remove it before testing.'}
$testPath=Join-Path $repoPath ('artifacts/startup-package-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testPath | Out-Null
[xml]$manifest=Get-Content -LiteralPath (Join-Path $repoPath 'ClipFlow/ClipFlow/Package.appxmanifest') -Encoding utf8
$manifest.Package.Identity.Name=$testName
$manifest.Package.Identity.Publisher='CN=OpticsValley'
$manifest.Package.Properties.DisplayName='ClipFlow Startup Acceptance'
$manifestPath=Join-Path $testPath 'Package.Acceptance.appxmanifest'
$manifest.Save($manifestPath)
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild=& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if(!$msbuild){throw 'Visual Studio MSBuild is required.'}
$env:MSBuildEnableWorkloadResolver='false'
$buildPath=Join-Path $testPath 'build'
& $msbuild (Join-Path $repoPath 'ClipFlow/ClipFlow/ClipFlow.csproj') /restore /t:Build /nologo /v:minimal /p:Configuration=Debug /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:WindowsPackageType=MSIX /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false /p:AppxSymbolPackageEnabled=false /p:AppxBundle=Never /p:UapAppxPackageBuildMode=SideloadOnly "/p:StoreManifestPath=$manifestPath" "/p:OutputPath=$buildPath/" "/p:AppxPackageDir=$testPath/packages/"
if($LASTEXITCODE -ne 0){throw 'Acceptance package build failed.'}
$appxManifest=Join-Path $buildPath 'AppX/AppxManifest.xml'
if(!(Test-Path -LiteralPath $appxManifest)){$appxManifest=Join-Path $buildPath 'AppxManifest.xml'}
if(!(Test-Path -LiteralPath $appxManifest)){throw 'Acceptance AppX layout not found.'}
$exePath=[IO.Path]::GetFullPath((Join-Path (Split-Path $appxManifest -Parent) 'ClipFlow.exe'))
$registered=$false
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClipFlowPackageActivation {
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class Manager { }
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,[MarshalAs(UnmanagedType.LPWStr)] string args,uint options,out uint process);
    }
    public static uint Launch(string id,string args) {
        var manager=(IManager)new Manager();
        try { uint process; Marshal.ThrowExceptionForHR(manager.ActivateApplication(id,args,0,out process)); return process; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
'@
function Start-Acceptance([string]$mode,[string]$root){
    $payload=@{Mode=$mode;Root=$root}|ConvertTo-Json -Compress
    $arguments='--clipflow-test='+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload))
    $launchedId=[ClipFlowPackageActivation]::Launch($aumid,$arguments)
    Get-Process -Id $launchedId
}
$oldData=$env:CLIPFLOW_TEST_DATA_ROOT
$oldTest=$env:CLIPFLOW_SELF_TEST
try {
    # Current-user, developer-mode registration of a separate identity. No production package is changed.
    Add-AppxPackage -Register $appxManifest
    $registered=$true
    $package=Get-AppxPackage -Name $testName
    $aumid=$package.PackageFamilyName+'!App'
    $env:CLIPFLOW_TEST_DATA_ROOT=Join-Path $testPath 'data'
    $env:CLIPFLOW_SELF_TEST='startup-check'
    $fixture=Start-Acceptance 'startup-check' $env:CLIPFLOW_TEST_DATA_ROOT
    if(!$fixture.WaitForExit(45000)){throw 'Native StartupTask acceptance timed out.'}
    foreach($file in @('startup-results.txt','tray-results.txt')){
        $report=Get-Content -LiteralPath (Join-Path $env:CLIPFLOW_TEST_DATA_ROOT $file)
        $report
        if($report -notcontains 'SUCCESS'){throw "Packaged acceptance failed: $file"}
    }
    $env:CLIPFLOW_TEST_DATA_ROOT=Join-Path $testPath 'background-data'
    $env:CLIPFLOW_SELF_TEST='background-check'
    $fixture=Start-Acceptance 'background-check' $env:CLIPFLOW_TEST_DATA_ROOT
    if(!$fixture.WaitForExit(20000)){throw 'Background startup acceptance timed out.'}
    $report=Get-Content -LiteralPath (Join-Path $env:CLIPFLOW_TEST_DATA_ROOT 'background-startup-results.txt')
    $report
    if($report -notcontains 'SUCCESS'){throw 'Background startup acceptance failed.'}
    Write-Output "Native startup reports: $testPath"
} finally {
    $env:CLIPFLOW_TEST_DATA_ROOT=$oldData
    $env:CLIPFLOW_SELF_TEST=$oldTest
    # Only this script's generated executable/identity can be stopped and unregistered.
    Get-Process ClipFlow -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exePath } | Stop-Process
    if($registered){
        Get-AppxPackage -Name $testName | Where-Object { $_.Name -eq $testName } | Remove-AppxPackage
        if(Get-AppxPackage -Name $testName){throw 'Acceptance package cleanup failed.'}
        Write-Output 'PASS: isolated acceptance package removed; production startup preference untouched'
    }
}
