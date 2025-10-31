@echo off
echo PRESS ANY KEY TO INSTALL TO LOCAL NUGET FEED
echo Remember to generate the up-to-date package.
c:\exe\nuget add .\Cadmus.Export\bin\Debug\Cadmus.Export.8.0.10.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Export.ML\bin\Debug\Cadmus.Export.ML.8.0.10.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Export.Rdf\bin\Debug\Cadmus.Export.Rdf.0.0.2.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import\bin\Debug\Cadmus.Import.6.0.4.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import.Proteus\bin\Debug\Cadmus.Import.Proteus.6.0.6.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Cadmus.Import.Excel\bin\Debug\Cadmus.Import.Excel.6.0.6.nupkg -source C:\Projects\_NuGet
c:\exe\nuget add .\Proteus.Rendering\bin\Debug\Proteus.Rendering.0.0.9.nupkg -source C:\Projects\_NuGet
pause
