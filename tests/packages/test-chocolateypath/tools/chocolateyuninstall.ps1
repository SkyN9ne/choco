﻿$ErrorActionPreference = 'Stop'

$packagePath = Get-ChocolateyPath -PathType 'PackagePath'
$installPath = Get-ChocolateyPath -PathType 'InstallPath'

Write-Host "Package Path in Uninstall Script: $packagePath"
Write-Host "Install Path in Uninstall Script: $installPath"