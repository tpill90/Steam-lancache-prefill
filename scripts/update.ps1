Set-Location $PSScriptRoot

# Getting latest version tag
Write-Host -ForegroundColor Yellow "Checking for latest version"
$versionApi = "https://api.github.com/repos/tpill90/steam-lancache-prefill/releases"
$versions = Invoke-RestMethod -Uri $versionApi

# Finding latest asset
$windowsAsset = $versions[0].assets | Where-Object { $_.name.Contains("win-x64")}
Write-Host "Found latest version : " -NoNewline
Write-Host -ForegroundColor Cyan $windowsAsset.name

# Downloading
Write-Host -ForegroundColor Yellow "Downloading..."
Invoke-WebRequest $windowsAsset.browser_download_url -OutFile $windowsAsset.name

# Unzipping
Write-Host -ForegroundColor Yellow "Unzipping..."
Expand-Archive -Force $windowsAsset.name -DestinationPath .
Copy-Item "$($windowsAsset.name.Replace('.zip', ''))\SteamPrefill.exe"

# Cleanup 
Remove-Item $windowsAsset.name
Remove-Item -Force -Recurse "$($windowsAsset.name.Replace('.zip', ''))"

Write-Host -ForegroundColor Cyan "Complete !"