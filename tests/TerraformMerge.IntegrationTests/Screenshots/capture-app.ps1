# Screenshot-based visual test of the Terraform Merge desktop app.
#
# Launches the REAL TerraformMerge.exe (its own UI thread + message loop), drives
# it with UI Automation, and captures the actual on-screen window to PNGs:
#   01-empty-window.png        the window at startup
#   02-after-compute-diff.png  Original/Diff/Target populated, diff computed
#   03-operation-editor.png    the per-field editor dialog (double-click a row)
#
# Each capture fronts our own window first, so it grabs only the app (whole-screen
# capture would otherwise include other desktop windows).
#
# Usage:  powershell -File capture-app.ps1 [-OutDir <dir>]
param(
  [string]$Exe      = (Join-Path $PSScriptRoot "..\..\..\src\TerraformMerge\bin\Debug\net10.0-windows\TerraformMerge.exe"),
  [string]$OutDir   = (Join-Path $env:TEMP "tfmerge-shots"),
  [string]$Fixtures = (Join-Path $PSScriptRoot "..\Fixtures")
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$ErrorActionPreference = "Stop"
$Exe = [System.IO.Path]::GetFullPath($Exe)
$Fixtures = [System.IO.Path]::GetFullPath($Fixtures)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$UIA  = [System.Windows.Automation.AutomationElement]
$TREE = [System.Windows.Automation.TreeScope]::Descendants
$CT   = [System.Windows.Automation.ControlType]
$PROP = [System.Windows.Automation.AutomationElement]::ControlTypeProperty

function Capture-Rect($left, $top, $w, $h, $path) {
  $bmp = New-Object System.Drawing.Bitmap $w, $h
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size $w, $h))
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Host "saved $path ($w x $h)"
}

function Wait-Stable($h) {
  for ($i = 0; $i -lt 30; $i++) {
    [Win]::SetForegroundWindow($h) | Out-Null
    $r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
    if (($r.Right - $r.Left) -ge 1000 -and ($r.Bottom - $r.Top) -ge 600) { return }
    Start-Sleep -Milliseconds 200
  }
}

function Capture-Handle($h, $path) {
  Wait-Stable $h
  [Win]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 500
  $r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
  Capture-Rect $r.Left $r.Top ($r.Right - $r.Left) ($r.Bottom - $r.Top) $path
}

function Elements($root, $ct) {
  return $root.FindAll($TREE, (New-Object System.Windows.Automation.PropertyCondition($PROP, $ct)))
}
function Find-ByName($root, $ct, $name) {
  foreach ($e in Elements $root $ct) { if ($e.Current.Name -eq $name) { return $e } }; return $null
}
function Invoke-Element($e) { $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
# An editable ComboBox (the panes' Env pickers) exposes an Edit child of its
# own, so the file-path boxes are the Edits whose parent is not a ComboBox.
function Path-Edits($root) {
  $walker = [System.Windows.Automation.TreeWalker]::RawViewWalker
  return @(Elements $root $CT::Edit | Where-Object {
    $parent = $walker.GetParent($_)
    ($parent -eq $null) -or ($parent.Current.ControlType -ne $CT::ComboBox)
  })
}
function Set-EditValue($e, $v) { $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($v) }

$p = Start-Process $Exe -PassThru
for ($i = 0; $i -lt 60 -and $p.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 200; $p.Refresh() }
Start-Sleep -Milliseconds 2500

try {
  Capture-Handle $p.MainWindowHandle (Join-Path $OutDir "01-empty-window.png")
  $win = $UIA::FromHandle($p.MainWindowHandle)

  $edits = Path-Edits $win
  $loads = @(Elements $win $CT::Button | Where-Object { $_.Current.Name -eq "Load" })
  Set-EditValue $edits[0] (Join-Path $Fixtures "original.tf"); Invoke-Element $loads[0]; Start-Sleep -Milliseconds 400
  Set-EditValue $edits[2] (Join-Path $Fixtures "target.tf");   Invoke-Element $loads[2]; Start-Sleep -Milliseconds 400
  Invoke-Element (Find-ByName $win $CT::Button "Compute Diff"); Start-Sleep -Milliseconds 500
  Capture-Handle $p.MainWindowHandle (Join-Path $OutDir "02-after-compute-diff.png")

  $lists = Elements $win $CT::List
  $items = $lists[$lists.Count - 1].FindAll($TREE, (New-Object System.Windows.Automation.PropertyCondition($PROP, $CT::ListItem)))
  if ($items.Count -gt 0) {
    $rect = $items[0].Current.BoundingRectangle
    $cx = [int]($rect.X + $rect.Width / 2); $cy = [int]($rect.Y + $rect.Height / 2)
    $dlg = $null
    for ($try = 0; $try -lt 4 -and $dlg -eq $null; $try++) {
      [Win]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 300
      [Win]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 80
      [Win]::mouse_event(0x02,0,0,0,[IntPtr]::Zero); [Win]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
      Start-Sleep -Milliseconds 60
      [Win]::mouse_event(0x02,0,0,0,[IntPtr]::Zero); [Win]::mouse_event(0x04,0,0,0,[IntPtr]::Zero)
      Start-Sleep -Milliseconds 900
      $dlg = Find-ByName $UIA::RootElement $CT::Window "Edit operation"
    }
    if ($dlg) {
      $dh = [IntPtr]$dlg.Current.NativeWindowHandle
      [Win]::SetForegroundWindow($dh) | Out-Null; Start-Sleep -Milliseconds 500
      $r = New-Object Win+RECT; [Win]::GetWindowRect($dh, [ref]$r) | Out-Null
      Capture-Rect $r.Left $r.Top ($r.Right - $r.Left) ($r.Bottom - $r.Top) (Join-Path $OutDir "03-operation-editor.png")
    } else { Write-Host "editor dialog not found" }
  }
}
catch { Write-Host "STAGE ERROR: $($_.Exception.Message)" }
finally { Get-Process -Id $p.Id -ErrorAction SilentlyContinue | Stop-Process -Force }
Write-Host "DONE -> $OutDir"
