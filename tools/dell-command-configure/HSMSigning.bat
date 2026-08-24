
@echo off
::echo Inside batch file
set OPENSSL_CONF=C:\Temp\engine.conf
set "outloc=%USERPROFILE%"
set "outlocat=%USERPROFILE%\AppData\Local\DELL\CCTK\ABI"
::echo %outlocat%
set "OpensslPath=C:\Program Files (x86)\Garantir\GRS"
set "ObfuscatedKeyPath=C:\Program Files (x86)\Dell\Command Configure\X86_64"
set "ObfuscatedKeyName="
::echo %ObfuscatedKeyPath%

"%OpensslPath%\openssl.exe" dgst -sha384 -sign "%ObfuscatedKeyPath%\%ObfuscatedKeyName%" -out "%outlocat%\blobsignature.txt" %1

exit /b