param([int]$WaitSeconds = 8)
$sig = @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, System.UIntPtr dwExtraInfo);
[DllImport("user32.dll")] public static extern System.IntPtr GetForegroundWindow();
'@
$type = Add-Type -MemberDefinition $sig -Name ("W" + [System.Guid]::NewGuid().ToString("N").Substring(0,8)) -Namespace Native -PassThru
$unity = Get-Process Unity | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if ($null -eq $unity) { "Unity not found"; exit 1 }
$type::keybd_event(0x12, 0, 0, [System.UIntPtr]::Zero)
$type::keybd_event(0x12, 0, 2, [System.UIntPtr]::Zero)
$type::ShowWindow($unity.MainWindowHandle, 9) | Out-Null
$type::SetForegroundWindow($unity.MainWindowHandle) | Out-Null
Start-Sleep -Seconds $WaitSeconds
"focused Unity, waited $WaitSeconds s; still foreground = " + ($type::GetForegroundWindow() -eq $unity.MainWindowHandle)
