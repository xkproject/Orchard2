nuget.exe sources Remove -Name "KeTePongoOrchardCore" 
nuget.exe sources Add -Name "KeTePongoOrchardCore" -Source "https://pkgs.dev.azure.com/ketepongo/KTP/_packaging/KeTePongoOrchardCore/nuget/v3/index.json" -username jersio@hotmail.com -password t4p76c7idjjktllwriofrzpc4v4xth7uaejx7ibwif4kl246n4sq
dotnet clean OrchardCore.sln
dotnet restore /p:VersionSuffix=rc1.private.8 OrchardCore.sln
dotnet build --configuration Release OrchardCore.sln
dotnet restore /p:VersionSuffix=rc1.private.8 OrchardCore.sln
dotnet pack --configuration Release /p:VersionSuffix=rc1.private.8 OrchardCore.sln
dotnet restore /p:VersionSuffix=rc1.private.8 OrchardCore.sln
dotnet pack --configuration Release /p:VersionSuffix=rc1.private.8 OrchardCore.sln
Get-ChildItem src\ -Recurse -File *.private.8.nupkg | Foreach {dotnet nuget push --source "KeTePongoOrchardCore" --api-key KeTePongo $_.FullName }
