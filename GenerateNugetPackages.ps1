nuget.exe sources Remove -Name "KeTePongoOrchardCore" 
nuget.exe sources Add -Name "KeTePongoOrchardCore" -Source "https://pkgs.dev.azure.com/ketepongo/KTP/_packaging/KeTePongoOrchardCore/nuget/v3/index.json" -username jersio@hotmail.com -password t4p76c7idjjktllwriofrzpc4v4xth7uaejx7ibwif4kl246n4sq
dotnet pack
Get-ChildItem -Recurse -File *.nupkg | Foreach {dotnet nuget push --source "KeTePongoOrchardCore" --api-key KeTePongo $_.FullName }


