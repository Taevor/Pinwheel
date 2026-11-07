# 划词翻译自动化执行脚本
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Windows.Forms

# 1. 模拟 Ctrl+C 复制当前鼠标划选的文本
[System.Windows.Forms.SendKeys]::SendWait("^c")
Start-Sleep -Milliseconds 60

# 2. 读取系统剪贴板中的文字
$text = ""
if ([System.Windows.Forms.Clipboard]::ContainsText()) {
    $text = [System.Windows.Forms.Clipboard]::GetText().Trim()
}

# 3. 呼出 Pinwheel 独立单例翻译窗口
$appExe = $env:PINWHEEL_APP
if ([string]::IsNullOrWhiteSpace($appExe) -or -not (Test-Path -LiteralPath $appExe)) {
    $possible = @(
        (Join-Path $PSScriptRoot "..\..\Pinwheel.exe"),
        (Join-Path $PSScriptRoot "..\Pinwheel.exe"),
        (Join-Path (Get-Location) "Pinwheel.exe")
    )
    foreach ($p in $possible) {
        if (Test-Path -LiteralPath $p) {
            $appExe = (Get-Item $p).FullName
            break
        }
    }
}

if ($appExe -and (Test-Path -LiteralPath $appExe)) {
    if ([string]::IsNullOrWhiteSpace($text)) {
        & $appExe translate
    } else {
        & $appExe translate "$text"
    }
    Write-Output "划词翻译已就绪 #silent"
} else {
    Write-Error "未找到 Pinwheel.exe 主程序，无法拉起翻译窗口"
    exit 1
}