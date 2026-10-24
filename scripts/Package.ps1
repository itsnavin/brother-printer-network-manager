$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

function Invoke-Dotnet([string[]]$Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $Arguments failed ($LASTEXITCODE)" }
}

Invoke-Dotnet @('run', '--project', 'tests\Core.Tests\Core.Tests.csproj', '-c', 'Release')
Invoke-Dotnet @('publish', 'src\App\App.csproj', '-c', 'Release', '-r', 'win-x64',
    '--self-contained', 'true', '-p:PublishSingleFile=false', '-o', 'artifacts\win-x64')

$publish = Join-Path (Get-Location) 'artifacts\win-x64'
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$assets = Get-Content 'src\App\obj\project.assets.json' -Raw | ConvertFrom-Json
$licenseRoot = Join-Path $publish 'runtime-licenses'
New-Item $licenseRoot -ItemType Directory -Force | Out-Null
$dependencies = @($assets.project.frameworks.PSObject.Properties | ForEach-Object { $_.Value.downloadDependencies })
foreach ($id in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
    $package = @($dependencies | Where-Object { $_.name -ieq $id } | Select-Object -Unique name, version)
    if (@($package).Count -ne 1) { throw "Cannot find exact runtime package for $id" }
    if ($package[0].version -notmatch '^\[([^,]+), \1\]$') { throw "Expected exact runtime version: $id" }
    $version = $Matches[1]
    $source = Join-Path $packages "$id\$version"
    $destination = Join-Path $licenseRoot $id
    New-Item $destination -ItemType Directory -Force | Out-Null
    $notices = Get-ChildItem $source -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }
    if (-not ($notices | Where-Object { $_.Name -match '^LICENSE' })) { throw "Runtime license missing: $source" }
    if ($id -eq 'microsoft.netcore.app.runtime.win-x64' -and
        -not ($notices | Where-Object { $_.Name -match '^THIRD-PARTY-NOTICES' })) { throw "Runtime notices missing: $source" }
    $notices | Copy-Item -Destination $destination
}

$process = Start-Process (Join-Path $publish 'BrotherNetworkManager.exe') -ArgumentList '--smoke-test' -PassThru
if (-not $process.WaitForExit(30000)) {
    Stop-Process -Id $process.Id
    throw 'Offline GUI smoke test timed out'
}
if ($process.ExitCode -ne 0) { throw "Offline GUI smoke test failed ($($process.ExitCode))" }
Write-Host 'PASS: published native GUI offline smoke test'

foreach ($required in @('BrotherNetworkManager.exe', 'BrotherNetworkManager.dll', 'BrotherNetworkManager.runtimeconfig.json',
    'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt')) {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "Missing package file: $required" }
}
$zip = Join-Path (Get-Location) 'artifacts\BrotherNetworkManager-win-x64.zip'
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
(Get-FileHash $zip -Algorithm SHA256).Hash + '  BrotherNetworkManager-win-x64.zip' |
    Set-Content 'artifacts\SHA256SUMS.txt' -Encoding ascii
Write-Host "Packaged $zip"
