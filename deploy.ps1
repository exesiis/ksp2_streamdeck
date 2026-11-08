# Builds the mod and copies it to the game's mods folder.
# Usage: powershell -ExecutionPolicy Bypass -File deploy.ps1 [-GameDir <game folder>]
# Without -GameDir, the game is looked for in your Steam libraries, then in a folder named like "Kerbal Space Program 2".
# To remove the mod, delete its folder from mods/.
param([string]$GameDir = '')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = 'KSP2_x64.exe'

function Test-GameDir([string]$dir) { $dir -and (Test-Path -LiteralPath (Join-Path $dir $exe)) }

# 1. Steam: every library listed in libraryfolders.vdf, plus the Steam folder itself
function Find-InSteam {
	$steam = @(
		(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath,
		(Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam' -ErrorAction SilentlyContinue).InstallPath,
		"${env:ProgramFiles(x86)}\Steam"
	) | Where-Object { $_ } | ForEach-Object { $_ -replace '/', '\' } | Select-Object -Unique
	$libraries = @()
	foreach ($s in $steam) {
		$libraries += $s
		$vdf = Join-Path $s 'steamapps\libraryfolders.vdf'
		if (Test-Path -LiteralPath $vdf) {
			foreach ($m in [regex]::Matches((Get-Content -LiteralPath $vdf -Raw), '"path"\s+"([^"]+)"')) {
				$libraries += ($m.Groups[1].Value -replace '\\\\', '\')
			}
		}
	}
	foreach ($lib in ($libraries | Select-Object -Unique)) {
		$dir = Join-Path $lib 'steamapps\common\Kerbal Space Program 2'
		if (Test-GameDir $dir) { return $dir }
	}
}

# 2. Any other install (GOG, copy of the game...): a folder named like "Kerbal Space Program 2" on a local drive, a few levels deep
function Find-Anywhere {
	$skip = '^(Windows|\$Recycle\.Bin|System Volume Information|ProgramData|AppData|node_modules)$'
	$roots = Get-PSDrive -PSProvider FileSystem | Where-Object { $_.Root -match '^[A-Za-z]:\\$' -and (Get-Item $_.Root -ErrorAction SilentlyContinue) } |
		ForEach-Object { $_.Root }
	foreach ($root in $roots) {
		$level = @($root)
		for ($depth = 0; $depth -lt 4 -and $level.Count; $depth++) {
			$next = @()
			foreach ($d in $level) {
				foreach ($sub in (Get-ChildItem -LiteralPath $d -Directory -Force -ErrorAction SilentlyContinue)) {
					if ($sub.Name -match $skip) { continue }
					if ($sub.Name -like '*Kerbal*Space*Program*2*' -and (Test-GameDir $sub.FullName)) { return $sub.FullName }
					$next += $sub.FullName
				}
			}
			$level = $next
		}
	}
}

if (-not $GameDir) {
	Write-Host 'Looking for Kerbal Space Program 2 in Steam...'
	$GameDir = Find-InSteam
	if (-not $GameDir) {
		Write-Host 'Not found in Steam. Looking for a "Kerbal Space Program 2" folder on your drives (this can take a minute)...'
		$GameDir = Find-Anywhere
	}
	if (-not $GameDir) { throw "Game not found. Run again with -GameDir `"C:\path\to\Kerbal Space Program 2`" (the folder that contains $exe)." }
	Write-Host "Game found: $GameDir"
}
elseif (-not (Test-GameDir $GameDir)) { throw "$exe not found in '$GameDir'. Check the -GameDir path." }

$target = Join-Path $GameDir 'mods\ksp2_streamdeck'

if (Get-Process KSP2_x64 -ErrorAction SilentlyContinue) { throw 'KSP2 is running: close the game before deploying.' }

dotnet build "$here\Ksp2StreamDeck.csproj" -c Release -nologo -v q -p:GameDir=$GameDir
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item "$here\swinfo.json" $target -Force
Copy-Item "$here\bin\Release\netstandard2.1\Ksp2StreamDeck.dll" $target -Force
Copy-Item "$here\bin\Release\netstandard2.1\Ksp2StreamDeck.pdb" $target -Force
Write-Host "Mod deployed to $target"
