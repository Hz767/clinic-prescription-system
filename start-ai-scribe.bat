@echo off
chcp 65001 >nul
title 陈医生诊所 - AI问诊Agent服务

echo ========================================
echo   AI问诊Agent服务启动中...
echo ========================================
echo.

cd /d "%~dp0"

REM 检查.NET 9 SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到 .NET SDK，请先安装 .NET 9 SDK
    pause
    exit /b 1
)

REM 检查llama.cpp是否运行（端口8080）
netstat -ano | findstr ":8080" >nul
if errorlevel 1 (
    echo [警告] 未检测到 llama.cpp 服务（端口8080）
    echo        AI病历生成功能将不可用，但录音和转写仍可使用
    echo.
) else (
    echo [OK] llama.cpp 服务运行中（端口8080）
)

REM 检查ASR模型
if not exist "models\paraformer-zh.gguf" (
    echo [警告] 未检测到 FunASR 模型文件
    echo        请下载 paraformer-zh.gguf 到 models\ 目录
    echo        下载地址：https://github.com/mightychaos/funasr-llama.cpp/releases
    echo.
) else (
    echo [OK] FunASR 模型已就绪
)

echo.
echo 启动AI问诊Agent服务（端口5080）...
echo 按 Ctrl+C 停止服务
echo.

dotnet run --project src\Clinic.AiScribe\Clinic.AiScribe.csproj --no-build

pause
