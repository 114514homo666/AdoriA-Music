param([string]$OutputPath = "$PSScriptRoot\AdoriA-Music.exe")
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 "/out:$OutputPath" "/r:$framework\WPF\WindowsBase.dll" "/r:$framework\WPF\PresentationCore.dll" "/r:$framework\WPF\PresentationFramework.dll" /r:System.Xaml.dll /r:System.Web.Extensions.dll "$PSScriptRoot\CloudMusicFloat.cs" "$PSScriptRoot\Motion.cs" "$PSScriptRoot\SystemMediaReader.cs" "$PSScriptRoot\QQMusicAssets.cs" "$PSScriptRoot\LyricService.cs" "$PSScriptRoot\Appearance.cs" "$PSScriptRoot\NativeBackdrop.cs" "$PSScriptRoot\AppearanceControls.cs" "$PSScriptRoot\OverlayAppearance.cs" "$PSScriptRoot\ResponsiveLayout.cs" "$PSScriptRoot\ResizeGeometry.cs" "$PSScriptRoot\CursorGuard.cs" "$PSScriptRoot\RecoveryChecks.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }



