$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot)
try {
    New-Item -ItemType Directory -Path dist -Force | Out-Null
    dotnet publish apps/windows/PhoneCam.Desktop/PhoneCam.Desktop.csproj -c Release -r win-x64 --self-contained true -o dist/app -m:1
    if ($LASTEXITCODE) { throw 'Не удалось собрать приложение Windows.' }
    Copy-Item scripts/Uninstall.ps1 dist/app/Uninstall.ps1 -Force
    New-Item -ItemType Directory -Path dist/app/docs -Force | Out-Null
    Copy-Item docs/* dist/app/docs -Recurse -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Path]::GetFullPath('dist/payload.zip')
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
    [IO.Compression.ZipFile]::CreateFromDirectory([IO.Path]::GetFullPath('dist/app'), $archive)
    dotnet publish apps/windows/PhoneCam.Setup/PhoneCam.Setup.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist/installer -m:1
    if ($LASTEXITCODE) { throw 'Не удалось собрать установщик.' }
    Copy-Item dist/installer/CamtranslatorXpraim-Setup.exe dist/CamtranslatorXpraim-Setup.exe -Force
    Get-FileHash dist/CamtranslatorXpraim-Setup.exe -Algorithm SHA256 | Format-List
} finally { Pop-Location }
