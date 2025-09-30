@echo off
echo PUSH PACKAGES TO NUGET
prompt
set nu=C:\Exe\nuget.exe
set src=-Source https://api.nuget.org/v3/index.json

%nu% push .\Cadmus.Export\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Cadmus.Export.ML\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Cadmus.Export.Rdf\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Cadmus.Import\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Cadmus.Import.Proteus\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Cadmus.Import.Excel\bin\Debug\*.nupkg %src% -SkipDuplicate
%nu% push .\Proteus.Rendering\bin\Debug\*.nupkg %src% -SkipDuplicate

echo COMPLETED
echo on
