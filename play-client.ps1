# 启动一个 Zytter 客户端窗口。
# 开第二个客户端：再运行一次本脚本（或复制此文件为 play-client2.ps1 双击）。
# 注意：两个客户端必须使用不同的账号登录（同一账号只保留最后一条大厅连接，无法互相对战）。

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$godot = "Godot_v4.7-stable_mono_win64.exe"
$project = Join-Path $root "src\Zytter.Client"

if (-not (Test-Path $godot)) {
    Write-Host "未找到 Godot：$godot（请修改脚本中的路径）"
    exit 1
}

Write-Host "启动客户端（服务器地址默认为 http://127.0.0.1:17717，可在界面修改）……"
Start-Process -FilePath $godot -ArgumentList "--path", "`"$project`""
