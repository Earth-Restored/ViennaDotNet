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

if ((-not $isWindows) -and (-not $isLinux)) {
    $isWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    $isLinux = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Unix
}

try {
    Set-Location -Path $launcherDir
    
    if ($isWindows) {
        $originalTitle = $Host.UI.RawUI.WindowTitle
        $Host.UI.RawUI.WindowTitle = "ViennaDotNet Launcher"

        $fullPath = Join-Path $launcherDir "Launcher.exe"
        $launcher = Start-Process -FilePath $fullPath -PassThru
        Wait-Process -Id $launcher.Id
    } elseif ($isLinux) {
        $originalTitle = $null
        Write-Host "`e]0;ViennaDotNet Launcher`a"

        $fullPath = Join-Path $launcherDir "Launcher"
        chmod +x $fullPath
        $launcher = Start-Process -FilePath $fullPath -PassThru
        Wait-Process -Id $launcher.Id
    } else {
        Write-Host "Unsupported platform"
    }
}
catch {
    Write-Error "Failed to launch: $($_.Exception.Message)"
}
finally {
    Set-Location -Path $originalPath
    
    if ($isWindows) {
        $Host.UI.RawUI.WindowTitle = $originalTitle
    } elseif ($isLinux) {
        Write-Host "`e]0;$originalTitle`a"
    } else {
        Write-Host "Unsupported platform"
    }
}
'@
    $startScriptContent | Out-File -FilePath "$publishDir/run_launcher.ps1" -Encoding utf8
    
    if (!$IsWindows) {
        chmod +x "$publishDir/run_launcher.ps1"
    }
}
