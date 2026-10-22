$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\CamtranslatorXpraim'))
$actual = [IO.Path]::GetFullPath($PSScriptRoot)
if ($actual -ne $expected) { throw 'Удаление разрешено только из папки установленного CamtranslatorXpraim.' }
if (Get-Process CamtranslatorXpraim -ErrorAction SilentlyContinue) { throw 'Закройте CamtranslatorXpraim перед удалением.' }
Add-Type -AssemblyName System.Windows.Forms
if ([Windows.Forms.MessageBox]::Show('Удалить приложение? Настройки, сертификаты и записи сохранятся.', 'CamtranslatorXpraim', 'YesNo') -ne 'Yes') { exit }
foreach ($relative in Get-Content -LiteralPath (Join-Path $actual 'installed-files.txt')) {
    $target = [IO.Path]::GetFullPath((Join-Path $actual $relative))
    if (-not $target.StartsWith($expected + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Недопустимый путь в манифесте.' }
    if (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
}
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'CamtranslatorXpraim.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name CamtranslatorXpraim -ErrorAction SilentlyContinue
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\CamtranslatorXpraim' -ErrorAction SilentlyContinue
