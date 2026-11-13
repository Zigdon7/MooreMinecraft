param([Parameter(Mandatory)][int]$ProcessId)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if ([Threading.Thread]::CurrentThread.ApartmentState -ne "MTA") { throw "UIA worker must run in MTA" }
$A = [System.Windows.Automation.AutomationElement]
$p = Get-Process -Id $ProcessId
$window = $A::FromHandle($p.MainWindowHandle)
function Find([string]$Name) { $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new($A::NameProperty, $Name)) }
function Open([string]$Name) {
 $nav = Find $Name
 if (-not $nav) { throw "Navigation missing: $Name" }
 $nav.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
 Start-Sleep -Milliseconds 600
}
function Usable([string]$Name) {
 $e = Find $Name
 if (-not $e -or -not $e.Current.IsEnabled -or $e.Current.IsOffscreen) { throw "Not usable after navigation: $Name" }
}
# Switching pages changes which child windows exist; a provider hang here is a real failure.
Open "Settings page"
foreach ($name in @("Minecraft game folder", "Browse for Minecraft game folder", "Advanced pack manifest URL", "Restore previous managed mod set")) { Usable $name }
$hidden = Find "Install checked update"
if ($hidden -and -not $hidden.Current.IsOffscreen) { throw "Mods page still shown behind Settings" }
Open "Activity page"
Usable "Activity log"
Open "Mods page"
foreach ($name in @("Install checked update", "Check for updates", "Change game folder")) { Usable $name }
"PASS MTA UIA page navigation: settings, activity and mods controls exposed"
