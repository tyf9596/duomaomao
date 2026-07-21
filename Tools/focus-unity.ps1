param([int]$WaitSeconds = 8, [string]$Project = "")
# Default: the repo folder this script lives in (<repo>\Tools\focus-unity.ps1) — so each
# repo's copy targets its own editor when several Unity windows are open.
if (-not $Project) { $Project = Split-Path (Split-Path $PSScriptRoot -Parent) -Leaf }
$sig = @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
'@
$type = Add-Type -MemberDefinition $sig -Name ("W" + [System.Guid]::NewGuid().ToString("N").Substring(0,8)) -Namespace Native -PassThru
$candidates = @(Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 })
# Unity main window title = "<project> - <scene> - <targets> - Unity <ver> ..."
# (transiently becomes e.g. "Compiling Scripts" — fall back to the -projectPath cmdline)
$unity = $candidates | Where-Object { $_.MainWindowTitle -like "$Project -*" } | Select-Object -First 1
if ($null -eq $unity) {
    $byCmdline = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
        Where-Object { $_.CommandLine -like "*$Project*" } | Select-Object -First 1
    if ($null -ne $byCmdline) { $unity = $candidates | Where-Object { $_.Id -eq $byCmdline.ProcessId } | Select-Object -First 1 }
}
if ($null -eq $unity) {
    if ($candidates.Count -eq 1) { $unity = $candidates[0] }  # single editor: no ambiguity
    elseif ($candidates.Count -eq 0) { "Unity not found"; exit 1 }
    else { "no Unity window matching '$Project'; open: " + (($candidates | ForEach-Object { $_.MainWindowTitle }) -join ' | '); exit 1 }
}
$type::keybd_event(0x12, 0, 0, [System.UIntPtr]::Zero)
$type::keybd_event(0x12, 0, 2, [System.UIntPtr]::Zero)
$type::ShowWindow($unity.MainWindowHandle, 9) | Out-Null
$type::SetForegroundWindow($unity.MainWindowHandle) | Out-Null
Start-Sleep -Seconds $WaitSeconds
"focused '" + $unity.MainWindowTitle + "', waited $WaitSeconds s; still foreground = " + ($type::GetForegroundWindow() -eq $unity.MainWindowHandle)
