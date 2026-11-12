@echo off
rem Chamado pelo build.ps1 depois de achar o Visual Studio: %1 = vcvars64.bat, %2 = pasta native.
setlocal
call "%~1" >nul
if errorlevel 1 exit /b 1
cd /d "%~2"
if not exist build\obj\dll mkdir build\obj\dll
if not exist build\obj\test mkdir build\obj\test
set FLAGS=/nologo /O2 /Oi /fp:precise /std:c++20 /EHsc /W4 /WX /wd4324 /wd4815 /MT /GS /guard:cf /Qspectre- /DUNICODE /D_UNICODE /DWIN32 /D_WIN32_WINNT=0x0A00 /I.
cl %FLAGS% /Fobuild\obj\dll\ /LD eq_engine.cpp apo.cpp preview.cpp /Fe:build\VolumEqApo.dll /link /DEBUG:NONE /GUARD:CF /DYNAMICBASE /NXCOMPAT /DEF:VolumEqApo.def audiobaseprocessingobject.lib audiomediatypecrt.lib ole32.lib uuid.lib propsys.lib avrt.lib shell32.lib audioeng.lib legacy_stdio_definitions.lib
if errorlevel 1 exit /b 1
cl %FLAGS% /Fobuild\obj\test\ eq_engine.cpp eq_test.cpp /Fe:build\eq_test.exe /link /DEBUG:NONE
if errorlevel 1 exit /b 1
build\eq_test.exe
exit /b %ERRORLEVEL%
