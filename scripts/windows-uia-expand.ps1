param([Parameter(Mandatory)][int]$ProcessId)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
if ([Threading.Thread]::CurrentThread.ApartmentState -ne "MTA") { throw "UIA worker must run in MTA" }
$p = Get-Process -Id $ProcessId
$window = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
$condition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Change selected game folder")
$toggle = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
$pattern = $toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$pattern.Toggle()
Start-Sleep -Milliseconds 500
if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { throw "UIA did not expand folder editor" }
$field = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Minecraft game folder"))
if (-not $field -or -not $field.Current.IsEnabled -or $field.Current.IsOffscreen) { throw "Expanded game folder is not usable" }
$pattern.Toggle()
Start-Sleep -Milliseconds 500
if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::Off) { throw "UIA did not collapse folder editor" }
$detail = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Show technical details"))
$detailPattern = $detail.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
$detailPattern.Toggle()
Start-Sleep -Milliseconds 500
$manifest = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, "Advanced pack manifest URL"))
if (-not $manifest -or -not $manifest.Current.IsEnabled) { throw "UIA details editor was not exposed" }
$detailPattern.Toggle()
"PASS MTA UIA folder/details expand/collapse and accessible editors"
