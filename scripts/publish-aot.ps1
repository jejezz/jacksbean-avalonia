# NativeAOT로 게시한다: publish\aot\JacksBean.exe (단일 실행 파일 + 네이티브 라이브러리 몇 개).
# NativeAOT 링크에는 vswhere.exe가 PATH에 있어야 한다. 없으면 링크가 실패하므로 VS 설치 폴더를 더한다.
# 사용: powershell -File scripts\publish-aot.ps1 [-Rid win-x64]
param([string]$Rid = "win-x64")

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer"
if (Test-Path $vswhere) { $env:PATH += ";$vswhere" }

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
dotnet publish src\JackBean.App -c Release -r $Rid -p:PublishAot=true -o publish\aot
if ($LASTEXITCODE -ne 0) { throw "게시 실패 (코드 $LASTEXITCODE)" }

$files = Get-ChildItem publish\aot -File | Where-Object { $_.Extension -ne ".pdb" }
$mb = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "완료: publish\aot\$((Get-Item publish\aot\*.exe).Name)  ($mb MB, 파일 $($files.Count)개, pdb 제외)"
