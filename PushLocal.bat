@echo off
echo PRESS ANY KEY TO INSTALL TO LOCAL NUGET FEED
echo Remember to generate the up-to-date package.
c:\exe\nuget add .\Cadmus.Export\bin\Debug\Cadmus.Export.7.0.0.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Export.ML\bin\Debug\Cadmus.Export.ML.7.0.0.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import\bin\Debug\Cadmus.Import.7.0.0.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import.Proteus\bin\Debug\Cadmus.Import.Proteus.7.0.0.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import.Excel\bin\Debug\Cadmus.Import.Excel.7.0.0.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Proteus.Rendering\bin\Debug\Proteus.Rendering.0.0.2.nupkg -source C:\Projects\_NuGet
pause
