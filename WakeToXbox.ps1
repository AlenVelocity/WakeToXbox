Start-Sleep -Seconds 3

$event = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Power-Troubleshooter'; Id=1} -MaxEvents 1
$xml = [xml]$event.ToXml()
$wakeSource = $xml.Event.EventData.Data | Where-Object { $_.Name -eq 'WakeSourceText' } | Select-Object -ExpandProperty '#text'

if ($wakeSource -match 'USB Composite Device') {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class KeyboardSim {
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
"@

    $VK_LWIN = 0x5B
    $VK_F11 = 0x7A
    $KEYEVENTF_KEYUP = 0x0002

    [KeyboardSim]::keybd_event($VK_LWIN, 0, 0, [UIntPtr]::Zero)
    [KeyboardSim]::keybd_event($VK_F11, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 100
    [KeyboardSim]::keybd_event($VK_F11, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
    [KeyboardSim]::keybd_event($VK_LWIN, 0, $KEYEVENTF_KEYUP, [UIntPtr]::Zero)
}