#Requires AutoHotkey v2.0
#SingleInstance Force
Persistent

OnMessage(0x218, PowerBroadcast)  ; WM_POWERBROADCAST

PowerBroadcast(wParam, lParam, msg, hwnd) {
    ; PBT_APMRESUMEAUTOMATIC = 0x12
    if (wParam = 0x12) {
        SetTimer(HandleWake, -100)
    }
}

HandleWake() {
    wakeStamp := DateAdd(A_NowUTC, -5, "Seconds")

    overlay := Gui("+AlwaysOnTop -Caption +ToolWindow")
    overlay.BackColor := "Black"
    overlay.Show("x0 y0 w" . A_ScreenWidth . " h" . A_ScreenHeight)

    tmpFile := A_Temp . "\wakecheck.txt"
    cmd := 'wevtutil qe System "/q:*[System[Provider[@Name=' . "'Microsoft-Windows-Power-Troubleshooter'" . '] and (EventID=1)]]" /c:1 /rd:true /f:xml'

    matched := false
    deadline := A_TickCount + 12000

    while (A_TickCount < deadline) {
        RunWait(A_ComSpec . ' /c ' . cmd . ' > "' . tmpFile . '"', , "Hide")
        wakeText := ""
        if FileExist(tmpFile)
            wakeText := FileRead(tmpFile)

        eventStamp := ""
        if RegExMatch(wakeText, "SystemTime='(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})", &m)
            eventStamp := m[1] . m[2] . m[3] . m[4] . m[5] . m[6]

        if (eventStamp != "" && eventStamp >= wakeStamp) {
            if InStr(wakeText, "USB Composite Device") {
                matched := true
            }
            break
        }

        Sleep(750)
    }

    if !matched {
        overlay.Destroy()
        return
    }

    timeout := A_TickCount + 6000
    while !ProcessExist("explorer.exe") && (A_TickCount < timeout)
        Sleep(200)
    Sleep(500)

    Send("#{F11}")

    Sleep(3000)
    overlay.Destroy()
}