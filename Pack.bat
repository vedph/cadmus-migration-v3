@echo off
echo BUILD Packages
del .\Cadmus.Export\bin\Debug\*.*nupkg
del .\Cadmus.Export.ML\bin\Debug\*.*nupkg
del .\Cadmus.Import\bin\Debug\*.*nupkg
del .\Cadmus.Import.Proteus\bin\Debug\*.*nupkg
del .\Cadmus.Import.Excel\bin\Debug\*.*nupkg

cd .\Cadmus.Export
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
cd..
cd .\Cadmus.Export.ML
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
cd..
cd .\Cadmus.Import
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
cd..
cd .\Cadmus.Import.Proteus
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
cd..
cd .\Cadmus.Import.Excel
dotnet pack -c Debug -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
cd..

pause
