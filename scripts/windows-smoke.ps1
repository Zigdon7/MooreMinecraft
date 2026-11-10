param([string]$Exe = "artifacts/manager/Saturn-Mod-Manager.exe")
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
$resolved = (Resolve-Path $Exe).Path
$evidence = Join-Path (Resolve-Path artifacts/manager) "ui-evidence"
New-Item -ItemType Directory -Force -Path $evidence | Out-Null
$cases = @(
 @{name="initial"; args="--ui-preview initial"},
 @{name="checked"; args="--ui-preview checked"},
 @{name="keyboard"; args="--ui-preview checked"},
 @{name="uia-expand"; args="--ui-preview checked"},
 @{name="busy"; args="--ui-preview busy"},
 @{name="success"; args="--ui-preview success"},
 @{name="error"; args="--ui-preview error"},
 @{name="details"; args="--ui-preview details"},
 @{name="narrow"; args="--ui-preview checked --narrow"},
 @{name="large-text"; args="--ui-preview checked --large-text"}
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
  $installName = if ($case.name -eq "success") { "Reinstall checked update" } elseif ($case.name -eq "error") { "Retry checked installation" } else { "Install checked update" }
  foreach ($name in @("Check for updates", $installName, "Update status")) {
   if (-not ($rows | Where-Object {$_.name -eq $name})) { throw "Accessible control missing: $name in $($case.name)" }
  }
  $installRow = $rows | Where-Object {$_.name -eq $installName} | Select-Object -First 1
  if ($installRow.offscreen) { throw "Primary install action is below the viewport: $($case.name)" }
  if ($case.name -eq "busy") {
   $cancelRow = $rows | Where-Object {$_.name -eq "Cancel current operation safely"} | Select-Object -First 1
   if (-not $cancelRow.enabled -or $cancelRow.offscreen) { throw "Busy cancellation is not visible/enabled" }
  }
  if ($case.name -eq "uia-expand") {
   # Separate MTA automation client; a provider hang is a real failing gate.
   $worker = Start-Process pwsh -ArgumentList @("-Mta", "-NoProfile", "-File", "scripts/windows-uia-expand.ps1", "-ProcessId", $p.Id) -PassThru -RedirectStandardOutput (Join-Path $evidence "uia-expand-worker.txt") -RedirectStandardError (Join-Path $evidence "uia-expand-worker-error.txt")
   if (-not $worker.WaitForExit(20000)) { $worker.Kill(); throw "UIA expansion provider timed out" }
   if ($worker.ExitCode -ne 0) { throw "UIA expansion worker failed: $($worker.ExitCode)" }
  }
  if ($case.name -eq "keyboard") {
   $toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Change selected game folder")))
   # Expand/collapse through real keyboard input. Synchronous UIA Toggle across
   # a structural visibility change can deadlock the provider on hosted Windows.
   $toggle.SetFocus()
   for ($wait=0; $wait -lt 20; $wait++) {
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq "Change selected game folder") { break }
    Start-Sleep -Milliseconds 50
   }
   if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne "Change selected game folder") { throw "Checkbox focus did not settle before keyboard Space" }
   [System.Windows.Forms.SendKeys]::SendWait(" ")
   Start-Sleep -Milliseconds 300
   if ($toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw "Keyboard Space did not expand Change folder" }
   $expanded = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
   @($expanded | ForEach-Object { [pscustomobject]@{name=$_.Current.Name; enabled=$_.Current.IsEnabled; offscreen=$_.Current.IsOffscreen; bounds=$_.Current.BoundingRectangle.ToString()} }) | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $evidence "expanded-uia.json")
   $names = @()
   $sameFocus = 0; $lastFocus = ""
   for ($i=0; $i -lt 16; $i++) {
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
   foreach ($required in @("Browse for Minecraft game folder", "Check for updates", "Install checked update")) {
    if ($names -notcontains $required) { throw "Keyboard Tab cannot reach: $required" }
   }
   $field = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Minecraft game folder")))
   $value = $field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
   if ($value -ne 'C:\Users\Player\AppData\Roaming\.minecraft') { throw "Keyboard flow changed the game folder unexpectedly" }
   $field.SetFocus()
   # Expand/collapse through real keyboard input. Synchronous UIA Toggle across
   # a structural visibility change can deadlock the provider on hosted Windows.
   $toggle.SetFocus()
   for ($wait=0; $wait -lt 20; $wait++) {
    if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -eq "Change selected game folder") { break }
    Start-Sleep -Milliseconds 50
   }
   if ([System.Windows.Automation.AutomationElement]::FocusedElement.Current.Name -ne "Change selected game folder") { throw "Checkbox focus did not settle before keyboard Space" }
   [System.Windows.Forms.SendKeys]::SendWait(" ")
   $checkControl = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, ([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Check for updates")))
   $checkControl.SetFocus()
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
