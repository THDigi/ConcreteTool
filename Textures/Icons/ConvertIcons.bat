@echo off
set texconv=C:\Programs\Texconv\texconv.exe
cls

%texconv% *.png -y -ft dds -f BC7_UNORM_SRGB -pmalpha

ren *.DDS *.dds
pause