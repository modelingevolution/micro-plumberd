New-Cake -Name "micro-plumberd" -Root "./"

Add-CakeStep -Name "Build All" -Action {  Build-Dotnet -All  }
Add-CakeStep -Name "Build Documentation" -Action { 
    Copy-Item README.md .\docs\index.md
    markdown-to-toc .\docs\ .\docs\
    docfx .\docfx.json  
    Copy-Item .\*.png .\doc\
    Copy-Item .\*.png .\_site\
}
Add-CakeStep -Name "Publish to nuget.org" -Action { Publish-Nuget -SourceUrl "https://nuget.org" }
