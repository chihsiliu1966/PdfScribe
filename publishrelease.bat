echo off
if EXIST "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat" (
    call "%ProgramFiles%\Microsoft Visual Studio\2022\Professional\Common7\Tools\VsDevCmd.bat"
    echo on
    msbuild Th290Scribe.sln  -t:Rebuild -p:Configuration=Release -p:Platform="x64" -r
    if NOT EXIST "publish\signed" mkdir "publish\signed"
    del /q "publish\signed\*"
    copy /y  "publish\unsigned\Th290Scribe*" "publish\signed\"
    cd "publish\signed\"
    signtool sign /sha1 %CodeSignHash% /t http://time.certum.pl /fd sha256 /v Th290Scribe*.msi
) ELSE (
    echo Could not set build tools environment.
    exit 1
)

pause