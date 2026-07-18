# Run on wake (e.g. Task Scheduler) to launch Xbox Full Screen Experience after controller wake.
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    powershell -NoProfile -Sta -ExecutionPolicy Bypass -File $MyInvocation.MyCommand.Path
    return
}

Start-Sleep -Seconds 3

try {
    $event = Get-WinEvent -FilterHashtable @{
        LogName      = 'System'
        ProviderName = 'Microsoft-Windows-Power-Troubleshooter'
        Id           = 1
    } -MaxEvents 1 -ErrorAction Stop
} catch {
    return
}

$xml = [xml]$event.ToXml()
$wakeSource = $xml.Event.EventData.Data |
    Where-Object { $_.Name -eq 'WakeSourceText' } |
    Select-Object -ExpandProperty '#text'

if ($wakeSource -notmatch 'USB Composite Device') {
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$form = New-Object System.Windows.Forms.Form
$form.FormBorderStyle = 'None'
$form.WindowState = 'Maximized'
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::Black
$form.ShowInTaskbar = $false
$form.Show()
[System.Windows.Forms.Application]::DoEvents()

$timeout = (Get-Date).AddSeconds(6)
while (-not (Get-Process explorer -ErrorAction SilentlyContinue) -and (Get-Date) -lt $timeout) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 200
}
Start-Sleep -Milliseconds 500

if (-not ('KeyboardSim' -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KeyboardSim {
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
"@
}

$VK_LWIN = 0x5B
$VK_F11 = 0x7A
$KEYEVENTF_KEYUP = 0x0002

[KeyboardSim]::keybd_event($VK_LWIN, 0, 0, [UIntPtr]::Zero)
[KeyboardSim]::keybd_event($VK_F11, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 100
[KeyboardSim]::keybd_event($VK_F11, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
[KeyboardSim]::keybd_event($VK_LWIN, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)

Start-Sleep -Seconds 3

$form.Close()
$form.Dispose()
