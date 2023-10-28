# BitwardenExtender
Tool to extend the Bitwarden Client on Windows

## Installation
Powershell:
```
& {
  $dir=md "$Env:Temp\{$(New-Guid)}"
  $bkp=$ProgressPreference
  $ProgressPreference='SilentlyContinue'
  Write-Host 'Downloading...'
  Invoke-WebRequest (
    Invoke-RestMethod -Uri 'https://api.github.com/repos/ArgusMagnus/BitwardenExtender/releases/latest' |
    Select-Object -Expand assets |
    Select-String -InputObject { $_.browser_download_url } -Pattern '\.zip$' |
    Select-Object -Expand Line -First 1) -OutFile "$dir\BitwardenExtender.zip"
  Write-Host 'Expanding archive...'
  $dir2=md "$Env:Temp\{$(New-Guid)}"
  Expand-Archive -Path "$dir\BitwardenExtender.zip" -DestinationPath $dir2
  & "$dir2\BitwardenExtender.Terminal.exe" 'Install'
  Remove-Item $dir -Recurse
  $ProgressPreference=$bkp
  Write-Host 'Done'
}
```
