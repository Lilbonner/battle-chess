# Сборка одного exe и архива для релиза: .\build.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish -c Release -o dist -p:Version=$Version
if ($LASTEXITCODE -ne 0) { throw "dotnet publish завершился с ошибкой" }

$stage = Join-Path "release" "BattleChess"
if (Test-Path "release") { Remove-Item "release" -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item "dist\BattleChess.exe" $stage
@"
Боевые шахматы $Version

Запуск: BattleChess.exe (Windows 10/11 x64, установка .NET не нужна).

Режим «Battle Chess» с оригинальной графикой включается, если рядом с BattleChess.exe
лежит папка original, полученная из вашей копии игры (см. tools/README.md в репозитории).
Ресурсы оригинальной игры в архив не входят.
"@ | Out-File -Encoding utf8 (Join-Path $stage "README.txt")

$zip = "release\BattleChess-$Version-win-x64.zip"
Compress-Archive -Path $stage -DestinationPath $zip -Force
Write-Host "Готово: $zip"
