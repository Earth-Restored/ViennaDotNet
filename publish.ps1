Param (
    [string] $configuration = 'Release',
	[string[]] $profiles = @('framework-dependent-win-x64', 'framework-dependent-linux-x64')#@('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'framework-dependent-win-x64', 'framework-dependent-linux-x64')
)

foreach ($profile in $profiles) {
	Write-Host "Publishing $profile"
	if ($profile -eq 'framework-dependent') {
		dotnet publish ViennaDotNet.sln --no-self-contained -c $configuration /p:PublishSingleFile=false
	}
	elseif ($profile -like 'framework-dependent-*') {
		dotnet publish ViennaDotNet.sln --no-self-contained -c $configuration -r $profile.Substring('framework-dependent-'.Length) /p:PublishSingleFile=false
	}
	else {
		dotnet publish ViennaDotNet.sln --sc -c $configuration -r $profile
	}
}