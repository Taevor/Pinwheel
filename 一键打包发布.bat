@echo off
chcp 65001 >nul
title Pinwheel 开源发布包一键打包工具

echo ========================================================
echo  Pinwheel 开源便携版一键打包发布工具
echo  正在自动解析当前项目版本并生成纯净出厂发布包...
echo ========================================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\package-portable.ps1" %*

echo.
echo ========================================================
echo  打包流程已结束，请检查上面输出。
echo ========================================================
pause
