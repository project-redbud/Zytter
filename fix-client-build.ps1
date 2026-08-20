# Zytter 客户端一键修复/构建脚本
# 用途：Godot 编辑器偶尔会用默认模板覆盖 Zytter.Client.csproj（丢失 SignalR 与 Zytter.Core 引用），
# 或 obj 还原产物被清理导致编辑器内构建失败。运行本脚本即可恢复。
#
# 使用前提：先完全关闭 Godot 编辑器（避免编辑器退出时再次覆盖 csproj）。

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$csproj = Join-Path $root "src\Zytter.Client\Zytter.Client.csproj"
$content = @"
<Project Sdk=""Godot.NET.Sdk/4.7.0"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <RootNamespace>Zytter.Client</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include=""..\Zytter.Core\Zytter.Core.csproj"" />
    <PackageReference Include=""Microsoft.AspNetCore.SignalR.Client"" Version=""8.0.30"" />
  </ItemGroup>
</Project>
"@

Write-Host "恢复 Zytter.Client.csproj ……"
Set-Content -Path $csproj -Value $content -Encoding UTF8

Write-Host "还原 NuGet 包并构建客户端 ……"
Push-Location $root
try {
    dotnet restore "src\Zytter.Client" --nologo
    if ($LASTEXITCODE -ne 0) { throw "restore 失败" }
    dotnet build "src\Zytter.Client" --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw "build 失败" }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "完成！现在可以重新打开 Godot 编辑器并直接运行（F5）。"
Write-Host "注意：不要在编辑器里点『创建 C# 解决方案』，该操作会覆盖本 csproj。"
