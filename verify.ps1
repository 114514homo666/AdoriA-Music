$ErrorActionPreference='Stop'
& "$PSScriptRoot\build.ps1"
$taskFramework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
& (Join-Path $taskFramework 'csc.exe') /nologo /target:exe /platform:x64 "/out:$PSScriptRoot\LyricsChecks.exe" "/r:$PSScriptRoot\AdoriA-Music.exe" /r:System.Web.Extensions.dll "$PSScriptRoot\LyricsChecks.cs"
if($LASTEXITCODE -ne 0){throw '歌词检查编译失败'}
$taskRun=Join-Path $PSScriptRoot ('verification-runs\'+[Guid]::NewGuid().ToString('N'))
& "$PSScriptRoot\LyricsChecks.exe" $taskRun
if($LASTEXITCODE -ne 0){throw '歌词检查未通过'}
$taskUiResult=Join-Path $PSScriptRoot 'ui-verification\result.json'
if(Test-Path -LiteralPath $taskUiResult){Remove-Item -LiteralPath $taskUiResult}
$taskUi=Start-Process -FilePath "$PSScriptRoot\AdoriA-Music.exe" -ArgumentList '--verify-ui' -WindowStyle Hidden -PassThru
if(-not $taskUi.WaitForExit(60000)){throw '界面检查超时'}
if($taskUi.ExitCode -ne 0){throw '界面检查进程退出异常'}
$taskResult=Get-Content -Raw -LiteralPath "$PSScriptRoot\ui-verification\result.json" | ConvertFrom-Json
if(-not $taskResult.passed){throw '界面检查未通过'}
'歌词与界面检查全部通过'
