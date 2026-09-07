@echo off
setlocal EnableExtensions
set "GAME=d:\Steam\steamapps\common\Nuclear Option"
set MANAGED=%GAME%\NuclearOption_Data\Managed
set BEP=%GAME%\BepInEx\core
set PLUGINS=%GAME%\BepInEx\plugins
set ROOT=%~dp0
set RC=%ROOT%RailcannonPod
set RR=%ROOT%RR106
set KM=%ROOT%KMO2
set OUT=%ROOT%TarusWeaponPack.dll
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC%" (
  echo csc.exe not found
  exit /b 1
)
if not exist "%MANAGED%\Assembly-CSharp.dll" (
  echo Assembly-CSharp.dll not found
  exit /b 1
)

"%CSC%" /noconfig /nostdlib /nologo /optimize+ /target:library /platform:anycpu /langversion:5 ^
  /define:TARUS_PACK ^
  /out:"%OUT%" ^
  /r:"%MANAGED%\mscorlib.dll" ^
  /r:"%MANAGED%\netstandard.dll" ^
  /r:"%MANAGED%\System.dll" ^
  /r:"%MANAGED%\System.Core.dll" ^
  /r:"%BEP%\BepInEx.dll" ^
  /r:"%BEP%\0Harmony.dll" ^
  /r:"%MANAGED%\Assembly-CSharp.dll" ^
  /r:"%MANAGED%\Mirage.dll" ^
  /r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  /r:"%MANAGED%\UnityEngine.dll" ^
  /r:"%MANAGED%\UnityEngine.PhysicsModule.dll" ^
  /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
  /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
  /r:"%MANAGED%\UnityEngine.UI.dll" ^
  /r:"%MANAGED%\UnityEngine.UIModule.dll" ^
  /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
  /r:"%MANAGED%\UnityEngine.ParticleSystemModule.dll" ^
  /r:"%MANAGED%\UnityEngine.AudioModule.dll" ^
  "%ROOT%src\Plugin.cs" ^
  "%RC%\src\Plugin.cs" ^
  "%RC%\src\Pod.cs" ^
  "%RC%\src\Patches.cs" ^
  "%RC%\src\HaloVisual.cs" ^
  "%RC%\src\LaunchFx.cs" ^
  "%RC%\src\Hud.cs" ^
  "%RC%\src\Impact.cs" ^
  "%RC%\src\Icon.cs" ^
  "%RR%\Plugin.cs" ^
  "%RR%\Rifle.cs" ^
  "%RR%\Patches.cs" ^
  "%RR%\Visual.cs" ^
  "%RR%\Hud.cs" ^
  "%RR%\Impact.cs" ^
  "%RR%\Icon.cs" ^
  "%KM%\src\Plugin.cs" ^
  "%KM%\src\Munition.cs" ^
  "%KM%\src\Pylon.cs" ^
  "%KM%\src\Patches.cs" ^
  "%KM%\src\Visual.cs" ^
  "%KM%\src\Hud.cs" ^
  "%KM%\src\Icon.cs" ^
  "%KM%\src\LaunchFx.cs" ^
  "%KM%\src\GBurst.cs"

if errorlevel 1 (
  echo TarusWeaponPack BUILD FAILED
  exit /b 1
)

echo Built: %OUT%
set ASSETS_DST=%PLUGINS%\TarusWeaponPackAssets
if not exist "%ASSETS_DST%" mkdir "%ASSETS_DST%"
copy /Y "%RC%\assets\57mmRailgun.obj" "%ASSETS_DST%\" >nul
copy /Y "%RC%\assets\57mmRailgun.mtl" "%ASSETS_DST%\" >nul
copy /Y "%RC%\assets\57mmRailgun_icon.png" "%ASSETS_DST%\" >nul
copy /Y "%RR%\assets\106mmRR.obj" "%ASSETS_DST%\" >nul
copy /Y "%RR%\assets\106mmRR.mtl" "%ASSETS_DST%\" >nul
copy /Y "%RR%\assets\106mmRR.png" "%ASSETS_DST%\" >nul
copy /Y "%RR%\assets\106mmRR_icon.png" "%ASSETS_DST%\" >nul
copy /Y "%KM%\assets\KMO2.obj" "%ASSETS_DST%\" >nul
copy /Y "%KM%\assets\KMO2.mtl" "%ASSETS_DST%\" >nul
copy /Y "%KM%\assets\KMO2.png" "%ASSETS_DST%\" >nul
copy /Y "%KM%\assets\KMO2_icon.png" "%ASSETS_DST%\" >nul
copy /Y "%OUT%" "%PLUGINS%\TarusWeaponPack.dll"
if errorlevel 1 (
  echo INSTALL FAILED - close the game and rebuild
  exit /b 1
)
echo Installed: %PLUGINS%\TarusWeaponPack.dll
echo Assets:    %ASSETS_DST%
endlocal
