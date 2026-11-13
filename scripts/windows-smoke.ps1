param([string]$Exe = "artifacts/manager/Saturn-Mod-Manager.exe", [string]$EvidenceName = "ui-evidence")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowBounds {
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
'@
function Focus-WithTabs([string]$Name) {
 for ($attempt=0; $attempt -lt 20; $attempt++) {
  if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq $Name) {
   Start-Sleep -Milliseconds 100
   if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq $Name) { return }
  }
  [System.Windows.Forms.SendKeys]::SendWait("{TAB}")
  Start-Sleep -Milliseconds 100
 }
 throw "Keyboard cannot focus: $Name"
}
$resolved = (Resolve-Path $Exe).Path
# The shell folder picker hangs unless the UI thread is a single-threaded apartment.
$apartment = Start-Process -FilePath $resolved -ArgumentList '--check-apartment' -PassThru -Wait
if ($apartment.ExitCode -ne 0) { throw 'UI thread is not STA: the Browse folder dialog would hang' }
$evidence = Join-Path (Resolve-Path artifacts/manager)  $EvidenceName
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$cases = @(
 @{name="initial"; args="--ui-preview initial"},
 @{name="checked"; args="--ui-preview checked"},
 @{name="keyboard"; args="--ui-preview checked"},
 @{name="uia-pages"; args="--ui-preview checked"},
 @{name="busy"; args="--ui-preview busy"},
 @{name="success"; args="--ui-preview success"},
 @{name="error"; args="--ui-preview error"},
 @{name="offline"; args="--ui-preview offline"},
 @{name="manager"; args="--ui-preview manager"},
 @{name="settings"; args="--ui-preview settings"},
 @{name="activity"; args="--ui-preview activity"},
 @{name="narrow"; args="--ui-preview checked --narrow"},
 @{name="large-text"; args="--ui-preview checked --large-text"},
 @{name="narrow-large-text"; args="--ui-preview checked --narrow --large-text"},
 @{name="narrow-busy-large-text"; args="--ui-preview busy --narrow --large-text"},
 @{name="narrow-error-large-text"; args="--ui-preview error --narrow --large-text"},
 @{name="narrow-manager-large-text"; args="--ui-preview manager --narrow --large-text"},
 @{name="long-values"; args="--ui-preview checked --long-values --narrow"}
)
foreach ($case in $cases) {
 [Environment]::SetEnvironmentVariable("SATURN_UI_TRACE_FILE", (Join-Path $evidence "$($case.name)-focus-trace.txt"))
 $p = Start-Process -FilePath $resolved -ArgumentList $case.args -PassThru
 try {
  Start-Sleep -Seconds 3
  $p.Refresh()
  if ($p.HasExited -or $p.MainWindowHandle -eq 0) { throw "No live native window: $($case.name)" }
  $null = [WindowBounds]::SetForegroundWindow($p.MainWindowHandle)
  $window = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
  $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $rows = @()
  foreach ($element in $all) {
   $c = $element.Current
   $rows += [pscustomobject]@{name=$c.Name; type=$c.ControlType.ProgrammaticName; enabled=$c.IsEnabled; offscreen=$c.IsOffscreen; bounds=$c.BoundingRectangle.ToString()}
  }
  $rows | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $evidence "$($case.name)-uia.json")
  function Shown([string]$Name) {
   $row = $rows | Where-Object {$_.name -eq $Name} | Select-Object -First 1
   if (-not $row) { throw "Accessible control missing: $Name in $($case.name)" }
   if ($row.offscreen) { throw "Control is outside the window: $Name in $($case.name)" }
   $row
  }
  foreach ($name in @("Mods page", "Settings page", "Activity page", "Update status")) { $null = Shown $name }
  if ($case.name -eq "settings") {
   foreach ($name in @("Minecraft game folder", "Browse for Minecraft game folder", "Advanced pack manifest URL")) { $null = Shown $name }
  } elseif ($case.name -eq "activity") {
   $null = Shown "Activity log"
  } else {
   # The main action and the two versions are always on screen on the Mods page.
   $installName = if ($case.name -eq "success") { "Reinstall checked update" } elseif ($case.name -like "*error*") { "Retry checked installation" } else { "Install checked update" }
   foreach ($name in @($installName, "Check for updates", "Change game folder", "Pack status", "Installed", "Latest")) { $null = Shown $name }
  }
  if ($case.name -like "*busy*") {
   if (-not (Shown "Cancel current operation safely").enabled) { throw "Busy cancellation is not enabled" }
  }
  if ($case.name -like "*manager*") {
   if (-not (Shown "Install manager update").enabled) { throw "Manager update is not offered as an enabled action" }
  } elseif ($rows | Where-Object {$_.name -eq "Install manager update" -and -not $_.offscreen}) { throw "Manager update offered without a newer release: $($case.name)" }
  if ($case.name -eq "uia-pages") {
   # Separate MTA automation client; a provider hang is a real failing gate.
   $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { "pwsh" } else { "powershell" }
   $worker = Start-Process $shell -ArgumentList @("-Mta", "-NoProfile", "-File", "scripts/windows-uia-pages.ps1", "-ProcessId", $p.Id) -PassThru -RedirectStandardOutput (Join-Path $evidence "uia-pages-worker.txt") -RedirectStandardError (Join-Path $evidence "uia-pages-worker-error.txt")
   $null = $worker.Handle   # Windows PowerShell 5.1 only reports ExitCode once the handle is cached.
   if (-not $worker.WaitForExit(30000)) { $worker.Kill(); throw "UIA page navigation timed out" }
   if ($worker.ExitCode -ne 0) { throw "UIA page navigation worker failed: $($worker.ExitCode)" }
  }
  if ($case.name -eq "keyboard") {
   $names = @()
   $sameFocus = 0; $lastFocus = ""
   for ($i=0; $i -lt 14; $i++) {
    [System.Windows.Forms.SendKeys]::SendWait("{TAB}")
    Start-Sleep -Milliseconds 80
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($focused) {
     $focusName = $focused.Current.Name; $names += $focusName; "TAB $i -> $focusName" | Write-Host
     $sameFocus = if ($focusName -eq $lastFocus) { $sameFocus + 1 } else { 0 }; $lastFocus = $focusName
     if ($sameFocus -ge 2) { $names | Set-Content (Join-Path $evidence "keyboard-tab-order.txt"); throw "Keyboard focus trapped after Tab: $focusName" }
    }
   }
   $names | Set-Content (Join-Path $evidence "keyboard-tab-order.txt")
   foreach ($required in @("Install checked update", "Check for updates", "Change game folder", "Mods page", "Settings page", "Activity page")) {
    if ($names -notcontains $required) { throw "Keyboard Tab cannot reach: $required" }
   }
   # Open Settings with the keyboard only, then reach its controls.
   Focus-WithTabs "Settings page"
   [System.Windows.Forms.SendKeys]::SendWait(" ")
   Start-Sleep -Milliseconds 500
   $field = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Minecraft game folder")))
   if (-not $field -or $field.Current.IsOffscreen) { throw "Keyboard Space did not open Settings" }
   $value = $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
   if ($value -ne 'C:\Users\Player\AppData\Roaming\.minecraft') { throw "Keyboard flow changed the game folder unexpectedly" }
   Focus-WithTabs "Browse for Minecraft game folder"
   Focus-WithTabs "Mods page"
   [System.Windows.Forms.SendKeys]::SendWait(" ")
   Start-Sleep -Milliseconds 500
   Focus-WithTabs "Check for updates"
   Start-Sleep -Milliseconds 200
  }
  $r = New-Object WindowBounds+RECT
  if (-not [WindowBounds]::GetWindowRect($p.MainWindowHandle, [ref]$r)) { throw "No window bounds" }
  $bitmap = New-Object System.Drawing.Bitmap(($r.Right-$r.Left),($r.Bottom-$r.Top))
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.CopyFromScreen($r.Left,$r.Top,0,0,$bitmap.Size)
  $bitmap.Save((Join-Path $evidence "$($case.name).png"))
  $graphics.Dispose(); $bitmap.Dispose()
  "PASS native Windows $($case.name): window + named UIA controls" | Add-Content artifacts/manager/windows-smoke.txt
 } finally {
  if (-not $p.HasExited) {
   try {
    $lastRect = New-Object WindowBounds+RECT
    $null = [WindowBounds]::GetWindowRect($p.MainWindowHandle, [ref]$lastRect)
    $last = New-Object System.Drawing.Bitmap(($lastRect.Right-$lastRect.Left),($lastRect.Bottom-$lastRect.Top))
    $g = [System.Drawing.Graphics]::FromImage($last)
    $g.CopyFromScreen($lastRect.Left,$lastRect.Top,0,0,$last.Size)
    $last.Save((Join-Path $evidence "$($case.name)-last.png")); $g.Dispose(); $last.Dispose()
   } catch { Write-Warning "Could not capture final frame: $_" }
  }
  if (-not $p.HasExited) { $null=$p.CloseMainWindow(); if(-not $p.WaitForExit(5000)){$p.Kill()} }
 }
}
Copy-Item (Join-Path $evidence "initial.png") artifacts/manager/windows-smoke.png
# Fixture states are actual native rendering, not network/install, Narrator or real DPI proof.
