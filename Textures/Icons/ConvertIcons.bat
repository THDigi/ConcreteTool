@echo off
SET TEXCONV=C:\Steam\steamapps\common\SpaceEngineers\Tools\TexturePacking\Tools\texconv
cls

FOR %%f IN (*.png) DO (
  echo %%~nf
  %TEXCONV% -ft dds -f BC7_UNORM_SRGB %%~nf.png -pmalpha -y
)

pause