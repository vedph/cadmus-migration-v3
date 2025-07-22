cd .\cadmus-mig

dotnet build -c Release
dotnet publish -c Release
compress-archive -path .\bin\Release\net9.0\publish\* -DestinationPath .\bin\Release\cadmus-mig-any.zip -Force

dotnet publish -c Release -r win-x64 --self-contained False
dotnet publish -c Release -r win-arm64 --self-contained False
dotnet publish -c Release -r linux-x64 --self-contained False
dotnet publish -c Release -r osx-x64 --self-contained False

compress-archive -path .\bin\Release\net9.0\win-x64\publish\* -DestinationPath .\bin\Release\cadmus-mig-win-x64.zip -Force
compress-archive -path .\bin\Release\net9.0\win-arm64\publish\* -DestinationPath .\bin\Release\cadmus-mig-win-arm64.zip -Force
compress-archive -path .\bin\Release\net9.0\linux-x64\publish\* -DestinationPath .\bin\Release\cadmus-mig-linux-x64.zip -Force
compress-archive -path .\bin\Release\net9.0\osx-x64\publish\* -DestinationPath .\bin\Release\cadmus-mig-osx-x64.zip -Force
