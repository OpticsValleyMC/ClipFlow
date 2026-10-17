param(
    [string]$Executable = 'artifacts/publish/win-x64/ClipFlow.exe'
)

$ErrorActionPreference = 'Stop'

$repoPath = Split-Path $PSScriptRoot -Parent
$exePath = [IO.Path]::GetFullPath((Join-Path $repoPath $Executable))

if (!(Test-Path -LiteralPath $exePath)) {
    throw 'Publish the Release app first.'
}

if (Get-Process ClipFlow -ErrorAction SilentlyContinue) {
    throw 'Exit running ClipFlow instances before performing the Release smoke test.'
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClipFlowReleaseSmoke {
    [StructLayout(LayoutKind.Sequential)] public struct Identifier { public uint Size; public IntPtr Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32")] public static extern bool IsHungAppWindow(IntPtr hwnd);
    [DllImport("user32")] public static extern bool PostMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("shell32")] private static extern int Shell_NotifyIconGetRect(ref Identifier id, out Rect rect);
    public static bool HasIcon(IntPtr hwnd) {
        var id = new Identifier { Size=(uint)Marshal.SizeOf<Identifier>(), Window=hwnd, Id=1 };
        return Shell_NotifyIconGetRect(ref id, out _) == 0;
    }
}
'@

$report = [Collections.Generic.List[string]]::new()

function Assert-Smoke([bool]$condition, [string]$message) {
    if (!$condition) {
        throw $message
    }

    $report.Add('PASS: ' + $message)
}

$appProcess = $null
$windowHandle = [IntPtr]::Zero

try {
    $appProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        Start-Sleep -Milliseconds 100
        $appProcess.Refresh()

        if ($appProcess.HasExited) {
            throw 'Release app exited during startup.'
        }

        $windowHandle = $appProcess.MainWindowHandle

        if ($windowHandle -ne [IntPtr]::Zero -and [ClipFlowReleaseSmoke]::HasIcon($windowHandle)) {
            break
        }
    }

    Assert-Smoke (
        $windowHandle -ne [IntPtr]::Zero -and
        ![ClipFlowReleaseSmoke]::IsHungAppWindow($windowHandle)
    ) 'Release window starts and responds'

    Assert-Smoke ([ClipFlowReleaseSmoke]::HasIcon($windowHandle)) 'Release tray icon registered in Shell'

    $null = [ClipFlowReleaseSmoke]::PostMessage($windowHandle, 0x10, [UIntPtr]::Zero, [IntPtr]::Zero)

    for (
        $attempt = 0;
        $attempt -lt 30 -and [ClipFlowReleaseSmoke]::IsWindowVisible($windowHandle);
        $attempt++
    ) {
        Start-Sleep -Milliseconds 100
    }

    Assert-Smoke (
        !$appProcess.HasExited -and
        ![ClipFlowReleaseSmoke]::IsWindowVisible($windowHandle)
    ) 'System close hides Release window and keeps process running'

    $secondProcess = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

    Assert-Smoke (
        $secondProcess.WaitForExit(10000) -and
        $secondProcess.ExitCode -eq 0
    ) 'Second Release launch redirects activation and exits cleanly'

    for (
        $attempt = 0;
        $attempt -lt 30 -and ![ClipFlowReleaseSmoke]::IsWindowVisible($windowHandle);
        $attempt++
    ) {
        Start-Sleep -Milliseconds 100
    }

    Assert-Smoke (
        [ClipFlowReleaseSmoke]::IsWindowVisible($windowHandle)
    ) 'Existing Release instance restores after relaunch'

    $null = [ClipFlowReleaseSmoke]::PostMessage($windowHandle, 0x111, [UIntPtr]::new(0xCF02), [IntPtr]::Zero)

    Assert-Smoke (
        $appProcess.WaitForExit(10000) -and
        $appProcess.ExitCode -eq 0
    ) 'Native tray Exit command terminates Release process with code 0'

    Assert-Smoke (![ClipFlowReleaseSmoke]::HasIcon($windowHandle)) 'Exit removes tray icon from Shell'

    $report.Add('SUCCESS')
} finally {
    if ($appProcess -and !$appProcess.HasExited -and $windowHandle -ne [IntPtr]::Zero) {
        $null = [ClipFlowReleaseSmoke]::PostMessage($windowHandle, 0x111, [UIntPtr]::new(0xCF02), [IntPtr]::Zero)
        $null = $appProcess.WaitForExit(10000)
    }

    $report | Set-Content -LiteralPath (Join-Path $repoPath 'docs/release-smoke-results.txt') -Encoding utf8
    $report
}
