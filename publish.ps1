#!/usr/bin/env pwsh
Param (
    [string] $configuration = 'Release',
    [string[]] $profiles = @('framework-dependent-win-x64', 'framework-dependent-linux-x64')#@('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'framework-dependent-win-x64', 'framework-dependent-linux-x64')
)

function Invoke-ProjectPublish {
    param (
        [Parameter(Mandatory = $true)] [string]$ProjectPath,
        [Parameter(Mandatory = $true)] [string]$OutDir,
        [Parameter(Mandatory = $true)] [string]$Configuration,
        [Parameter(Mandatory = $true)] [string]$BuildProfile
    )

    Write-Host "Publishing project $(Split-Path $ProjectPath -Leaf) for profile: $BuildProfile" -ForegroundColor Gray

    if ($BuildProfile -eq 'framework-dependent') {
        dotnet publish $ProjectPath -o $OutDir --no-self-contained -c $Configuration /p:PublishSingleFile=false
    }
    elseif ($BuildProfile -like 'framework-dependent-*') {
        $rid = $BuildProfile.Replace('framework-dependent-', '')
        dotnet publish $ProjectPath -o $OutDir --no-self-contained -c $Configuration -r $rid /p:PublishSingleFile=false
    }
    else {
        dotnet publish $ProjectPath -o $OutDir --sc -c $Configuration -r $BuildProfile
    }
}

git submodule update --init --remote --merge --recursive

$projects = "ViennaDotNet.ApiServer", "ViennaDotNet.Buildplate", "ViennaDotNet.EventBus.Server", "ViennaDotNet.ObjectStore.Server", "ViennaDotNet.TappablesGenerator", "ViennaDotNet.TileRenderer"

foreach ($buildProfile in $profiles) {
    $publishDir = "./build/$configuration/$buildProfile"

    Write-Host "Publishing profile $buildProfile"
    foreach ($name in $projects) {
        $projectPath = "./src/$name/$name.csproj"
        $projectDest = "$publishDir/components"

       	Invoke-ProjectPublish `
            -ProjectPath $projectPath `
            -OutDir $projectDest `
            -Configuration $configuration `
            -BuildProfile $buildProfile
    }

    Invoke-ProjectPublish `
        -ProjectPath "./src/ViennaDotNet.LauncherUI/ViennaDotNet.LauncherUI.csproj" `
        -OutDir "$publishDir/launcher" `
        -Configuration $configuration `
        -BuildProfile $buildProfile

    Copy-Item -Path "staticdata" -Destination "$publishDir/staticdata" -Recurse -Force

    $startScriptContent = @'
#!/usr/bin/env pwsh
$originalPath = Get-Location
$launcherDir = Join-Path $PSScriptRoot "launcher"

try {
    Set-Location -Path $launcherDir

    if ((-not (Test-Path variable:IsWindows)) -or $IsWindows) {
        $fullPath = Join-Path $launcherDir "Launcher.exe"
        Start-Process -FilePath $fullPath -Wait
    } else {
        chmod +x ./Launcher
        ./Launcher
    }
}
catch {
    Write-Error "Failed to launch: $($_.Exception.Message)"
}
finally {
    Set-Location -Path $originalPath
}
'@
    $startScriptContent | Out-File -FilePath "$publishDir/run_launcher.ps1" -Encoding utf8
    
    if (!$IsWindows) {
        chmod +x "$publishDir/run_launcher.ps1"
    }
}