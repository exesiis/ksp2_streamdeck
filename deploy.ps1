# Compile le mod et le copie dans le dossier mods/ du jeu.
# Usage: powershell -File mod/deploy.ps1 [-GameDir <dossier>]   (supprimer le dossier du mod dans mods/ pour le retirer)
param([string]$GameDir = 'F:\Games\Games\Kerbal.Space.Program.2.v14669105')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $GameDir 'mods\ksp2_streamdeck'

if (Get-Process KSP2_x64 -ErrorAction SilentlyContinue) { throw 'KSP2 est ouvert: fermez le jeu avant de deployer.' }

dotnet build "$here\Ksp2StreamDeck.csproj" -c Release -nologo -v q -p:GameDir=$GameDir
if ($LASTEXITCODE -ne 0) { throw 'Compilation echouee.' }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$here\swinfo.json" $target -Force
Copy-Item "$here\bin\Release\netstandard2.1\Ksp2StreamDeck.dll" $target -Force
Copy-Item "$here\bin\Release\netstandard2.1\Ksp2StreamDeck.pdb" $target -Force
Write-Host "Mod deploye dans $target"
