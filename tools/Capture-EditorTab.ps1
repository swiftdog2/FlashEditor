<#
.SYNOPSIS
    Launches FlashEditor, selects a tab by name, and saves a PNG of the window.

.DESCRIPTION
    Nothing in the test suite covers the renderer or WinForms, so a layout or paint defect
    passes every test. This is the only automated check that a tab actually draws. It exists
    so adding an index editor can be verified the same way every time rather than by eye once.

    Tab selection goes through UI Automation rather than Ctrl+Tab, because Ctrl+Tab depends on
    which control holds focus and silently lands on the wrong tab when a list view has it.

.PARAMETER Tab
    The tab's display text, for example "Maps" or "Textures". Case insensitive, matched on
    the first tab whose name contains it.

.PARAMETER Out
    Where to write the PNG.

.PARAMETER SettleSeconds
    How long to wait after selecting the tab before capturing. Tabs that load in a background
    worker need this; the map tab sweeps the whole world and wants the most.

.PARAMETER LaunchSeconds
    How long to wait for the main window to appear and the cache to open.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Tab,
    [Parameter(Mandatory)][string] $Out,
    [int] $SettleSeconds = 12,
    [int] $LaunchSeconds = 25,
    [string] $Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes

# PrintWindow captures a window that is partially covered or off-screen, which a plain
# screen-region grab cannot. Flag 2 is PW_RENDERFULLCONTENT.
#
# IT DOES NOT CAPTURE THE OPENGL SURFACE, and an earlier version of this comment claimed it did.
# Measured on this machine: a GLControl clearing to magenta, with its paint handler confirmed
# firing, captures as blank through CopyFromScreen and through PrintWindow flags 0, 1, 2 and 3 -
# in this application and in a minimal one outside the repository. Every BitBlt-family capture
# reads whatever GDI last blitted into that rectangle.
#
# So a screenshot of the Models page is NOT evidence about the renderer. It reliably shows the
# previously visible page's pixels there, which looks exactly like a repaint defect and is not
# one. That false reading cost a full investigation. Judge the 3D viewer on the monitor, or with
# a Desktop Duplication or Windows.Graphics.Capture grab, never with this script.
$sig = @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
'@
Add-Type -MemberDefinition $sig -Name Win -Namespace Cap

# Resolved here rather than as a param default: $PSScriptRoot is empty in a param block under
# `powershell -File`, which silently produced a relative path and a "not found" that looked like
# a missing build rather than a broken default.
if (-not $Exe) {
    $root = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $Exe = Join-Path $root '..\FlashEditor\bin\Debug\net9.0-windows\FlashEditor.exe'
}
if (-not (Test-Path $Exe)) { throw "FlashEditor.exe not found at $Exe. Build the solution first." }
$Exe = (Resolve-Path $Exe).Path

$proc = Start-Process -FilePath $Exe -PassThru
try {
    # Wait for a real main window rather than sleeping a fixed amount: cache open time varies.
    $deadline = (Get-Date).AddSeconds($LaunchSeconds)
    while ((Get-Date) -lt $deadline) {
        $proc.Refresh()
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 400
    }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw "No main window after $LaunchSeconds s." }

    [void][Cap.Win]::ShowWindow($proc.MainWindowHandle, 3)   # maximise, so layout is captured at size
    [void][Cap.Win]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Seconds 3

    $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::TabItem)
    $items = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

    $names = @()
    $target = $null
    foreach ($i in $items) {
        $n = $i.Current.Name
        $names += $n
        if (-not $target -and $n -and $n -like "*$Tab*") { $target = $i }
    }
    Write-Host "Tabs: $($names -join ', ')"
    if (-not $target) { throw "No tab matching '$Tab'. Available: $($names -join ', ')" }

    $sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $sel.Select()
    Write-Host "Selected '$($target.Current.Name)', settling $SettleSeconds s"
    Start-Sleep -Seconds $SettleSeconds

    $r = New-Object Cap.Win+RECT
    [void][Cap.Win]::GetWindowRect($proc.MainWindowHandle, [ref]$r)
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { throw "Window rect is empty ($w x $h)." }

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    $ok = [Cap.Win]::PrintWindow($proc.MainWindowHandle, $dc, 2)
    $g.ReleaseHdc($dc)
    if (-not $ok) {
        # PrintWindow refuses some composited windows; fall back to grabbing the screen region.
        $g.CopyFromScreen($r.L, $r.T, 0, 0, (New-Object System.Drawing.Size $w, $h))
    }
    $g.Dispose()

    $dir = Split-Path -Parent $Out
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $Out ($w x $h)"
}
finally {
    if ($proc -and -not $proc.HasExited) { $proc.Kill(); $proc.WaitForExit(5000) }
}
