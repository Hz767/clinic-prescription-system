@echo off
chcp 65001 >nul
title 陈医生诊所 - AI问诊服务启动器

echo ========================================
echo   AI智能问诊服务启动器
echo ========================================
echo.

cd /d "%~dp0"

REM === 1. 启动llama.cpp（如果未运行）===
echo [1/3] 检查llama.cpp服务...
netstat -ano | findstr ":8080" | findstr "LISTENING" >nul
if errorlevel 1 (
    echo       启动llama.cpp中...
    start "llama.cpp" /min "D:\llama.cpp\b10375\llama-server.exe" -m "D:\llama.cpp\models\qwen2.5-7b-instruct-q4_k_m_merged.gguf" --port 8080 -c 4096 -t 4
    echo       等待模型加载（约30-60秒）...
    timeout /t 45 /nobreak >nul
) else (
    echo       llama.cpp已在运行
)

REM === 2. 验证llama.cpp ===
echo.
echo [2/3] 验证llama.cpp服务...
curl -s http://127.0.0.1:8080/health >nul 2>&1
if errorlevel 1 (
    echo [警告] llama.cpp未就绪，病历生成功能将不可用
    echo        请手动启动: D:\llama.cpp\b10375\llama-server.exe
) else (
    echo [OK] llama.cpp服务正常
)

REM === 3. 启动AI问诊Agent服务 ===
echo.
echo [3/3] 启动AI问诊Agent服务（端口5080）...
echo.
echo ========================================
echo   服务启动完成！
echo   - AI问诊Agent: http://127.0.0.1:5080
echo   - llama.cpp:   http://127.0.0.1:8080
echo.
echo   关闭此窗口将停止AI问诊服务
echo ========================================
echo.

dotnet run --project src\Clinic.AiScribe\Clinic.AiScribe.csproj --no-build

pause
