# Builds PhoneShare.exe and PhoneShare-Setup.exe with the C# compiler built into Windows (.NET Framework 4.8).
# Run:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root  = $PSScriptRoot
$fw    = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$csc   = "$fw\csc.exe"
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force $build | Out-Null

& (Join-Path $root 'tools\make-icon.ps1') -OutFile "$build\app.ico"

$common = @(
    '/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/platform:anycpu', '/nowarn:1607',
    "/r:$fw\WPF\PresentationFramework.dll", "/r:$fw\WPF\PresentationCore.dll",
    "/r:$fw\WPF\WindowsBase.dll", "/r:$fw\System.Xaml.dll",
    "/win32icon:$build\app.ico",
    "/resource:$root\common\Styles.xaml,Styles.xaml"
)

Write-Host 'Building PhoneShare.exe ...'
& $csc @common "/out:$build\PhoneShare.exe" `
    "/resource:$root\app\MainWindow.xaml,MainWindow.xaml" "/resource:$root\app\phone.html,phone.html" `
    "$root\common\*.cs" "$root\app\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'PhoneShare.exe failed to build' }

Write-Host 'Building PhoneShare-Setup.exe ...'
& $csc @common "/out:$root\PhoneShare-Setup.exe" "/win32manifest:$root\setup\setup.manifest" `
    "/resource:$root\setup\SetupWindow.xaml,SetupWindow.xaml" "/resource:$build\PhoneShare.exe,PhoneShare.exe" `
    "$root\common\*.cs" "$root\setup\*.cs"
if ($LASTEXITCODE -ne 0) { throw 'PhoneShare-Setup.exe failed to build' }

Write-Host "Done: $root\PhoneShare-Setup.exe"
