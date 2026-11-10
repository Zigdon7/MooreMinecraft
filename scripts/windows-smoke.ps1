param([string]$Exe = "artifacts/manager/Saturn-Mod-Manager.exe")
$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path $Exe).Path
$p = Start-Process -FilePath $resolved -PassThru
try {
  Start-Sleep -Seconds 5
  $p.Refresh()
  if ($p.HasExited) { throw "Manager exited during startup: $($p.ExitCode)" }
  if ($p.MainWindowHandle -eq 0) { throw "Manager did not create a native Windows window" }
  "PASS native Windows startup; title=$($p.MainWindowTitle)" | Tee-Object -FilePath artifacts/manager/windows-smoke.txt
  try {
    Add-Type -AssemblyName System.Drawing
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowBounds {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
}
'@
    $r = New-Object WindowBounds+RECT
    if (-not [WindowBounds]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { throw "No window bounds" }
    $image = New-Object System.Drawing.Bitmap(($r.Right-$r.Left),($r.Bottom-$r.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($image)
    $graphics.CopyFromScreen($r.Left,$r.Top,0,0,$image.Size)
    $image.Save((Join-Path (Resolve-Path artifacts/manager) "windows-smoke.png"))
    $graphics.Dispose(); $image.Dispose()
  } catch { Write-Warning "Screenshot unavailable: $_" }
} finally {
  if (-not $p.HasExited) { $null=$p.CloseMainWindow(); if(-not $p.WaitForExit(5000)){$p.Kill()} }
}
