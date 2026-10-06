[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'GHMR-compatibility.json'),
    [switch]$IncludeFullPaths,
    [string[]]$AdditionalSearchRoot = @()
)

$ErrorActionPreference = 'Stop'

function Get-SteamRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    $registryKeys = @(
        'HKCU:\Software\Valve\Steam',
        'HKLM:\Software\WOW6432Node\Valve\Steam',
        'HKLM:\Software\Valve\Steam'
    )

    foreach ($key in $registryKeys) {
        try {
            $item = Get-ItemProperty -Path $key
            foreach ($property in @('SteamPath', 'InstallPath')) {
                if ($item.$property -and (Test-Path -LiteralPath $item.$property)) {
                    $roots.Add([IO.Path]::GetFullPath($item.$property))
                }
            }
        } catch {
            # Steam is not registered at this location.
        }
    }

    foreach ($root in @($roots)) {
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (-not (Test-Path -LiteralPath $vdf)) { continue }
        foreach ($line in Get-Content -LiteralPath $vdf) {
            if ($line -match '"path"\s+"([^"]+)"') {
                $candidate = $Matches[1] -replace '\\\\', '\'
                if (Test-Path -LiteralPath $candidate) {
                    $roots.Add([IO.Path]::GetFullPath($candidate))
                }
            }
        }
    }

    $roots | Sort-Object -Unique
}

function Get-RockstarInstallRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    $registryRoots = @(
        'HKLM:\Software\Rockstar Games',
        'HKLM:\Software\WOW6432Node\Rockstar Games',
        'HKCU:\Software\Rockstar Games'
    )

    foreach ($registryRoot in $registryRoots) {
        if (-not (Test-Path -LiteralPath $registryRoot)) { continue }
        $keys = @((Get-Item -LiteralPath $registryRoot))
        $keys += @(Get-ChildItem -LiteralPath $registryRoot -Recurse -ErrorAction SilentlyContinue)

        foreach ($key in $keys) {
            try {
                $item = Get-ItemProperty -LiteralPath $key.PSPath
                foreach ($property in @('InstallFolder', 'InstallLocation', 'Path')) {
                    $candidate = $item.$property
                    if ($candidate -and (Test-Path -LiteralPath $candidate)) {
                        $roots.Add([IO.Path]::GetFullPath($candidate))
                    }
                }
            } catch {
                # This registry entry does not expose an accessible install path.
            }
        }
    }

    $roots | Sort-Object -Unique
}

function Get-UninstallInstallRoots {
    $roots = [System.Collections.Generic.List[string]]::new()
    $uninstallKeys = @(
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($key in $uninstallKeys) {
        foreach ($item in @(Get-ItemProperty -Path $key -ErrorAction SilentlyContinue)) {
            if ($item.DisplayName -notmatch '(Grand Theft Auto|GTA)') { continue }
            if ($item.InstallLocation -and (Test-Path -LiteralPath $item.InstallLocation)) {
                $roots.Add([IO.Path]::GetFullPath($item.InstallLocation))
            }
        }
    }

    $roots | Sort-Object -Unique
}

function Get-FileReport([string]$Game, [string]$Integration, [string[]]$Candidates) {
    $exe = $Candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $exe) {
        return [ordered]@{ game = $Game; integration = $Integration; found = $false; executable = $null }
    }

    $exe = (Resolve-Path -LiteralPath $exe).Path
    $directory = Split-Path -Parent $exe
    $version = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
    $cleoLogCandidates = @(
        (Join-Path $directory 'cleo_redux.log'),
        (Join-Path $env:APPDATA 'CLEO Redux\cleo_redux.log')
    )

    $integrationFiles = switch ($Integration) {
        'CLEO Redux x64' {
            [ordered]@{
                cleo_redux_asi = Test-Path -LiteralPath (Join-Path $directory 'cleo_redux64.asi')
                asi_loader = Test-Path -LiteralPath (Join-Path $directory 'version.dll')
                cleo_directory = Test-Path -LiteralPath (Join-Path $directory 'CLEO')
                cleo_log = (Get-DisplayPath ($cleoLogCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1))
            }
        }
        'CLEO Redux x86' {
            [ordered]@{
                cleo_redux_asi = Test-Path -LiteralPath (Join-Path $directory 'cleo_redux.asi')
                asi_loader = Test-Path -LiteralPath (Join-Path $directory 'dinput8.dll')
                cleo_directory = Test-Path -LiteralPath (Join-Path $directory 'CLEO')
                cleo_log = (Get-DisplayPath ($cleoLogCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1))
            }
        }
        'ScriptHookV bridge' {
            [ordered]@{
                script_hook_v = Test-Path -LiteralPath (Join-Path $directory 'ScriptHookV.dll')
                asi_loader = Test-Path -LiteralPath (Join-Path $directory 'dinput8.dll')
            }
        }
    }

    [ordered]@{
        game = $Game
        integration = $Integration
        found = $true
        executable = (Get-DisplayPath $exe)
        file_version = $version
        integration_files = $integrationFiles
    }
}

$steamRoots = @(Get-SteamRoots)
$commonRoots = @($steamRoots | ForEach-Object { Join-Path $_ 'steamapps\common' })
$rockstarRoots = @(Get-RockstarInstallRoots)
$uninstallRoots = @(Get-UninstallInstallRoots)
$knownLauncherRoots = @(
    Join-Path $env:ProgramFiles 'Rockstar Games'
    if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Rockstar Games' }
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | ForEach-Object { [IO.Path]::GetFullPath($_) }
$validAdditionalRoots = @($AdditionalSearchRoot | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
    [IO.Path]::GetFullPath($_)
})
$searchRoots = @($commonRoots + $rockstarRoots + $uninstallRoots + $knownLauncherRoots + $validAdditionalRoots | Sort-Object -Unique)

function Get-DisplayPath([AllowNull()][string]$Path) {
    if (-not $Path) { return $null }
    $fullPath = [IO.Path]::GetFullPath($Path)
    if ($IncludeFullPaths) { return $fullPath }

    foreach ($root in $searchRoots) {
        $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\', '/')
        $isRoot = $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase)
        $isChild = $fullPath.StartsWith($fullRoot + '\', [StringComparison]::OrdinalIgnoreCase)
        if ($isRoot -or $isChild) {
            $relative = $fullPath.Substring($fullRoot.Length).TrimStart('\', '/')
            return "<GAME_LIBRARY>\$relative"
        }
    }

    $appData = [Environment]::GetFolderPath('ApplicationData').TrimEnd('\', '/')
    $isAppData = $appData -and (
        $fullPath.Equals($appData, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($appData + '\', [StringComparison]::OrdinalIgnoreCase)
    )
    if ($isAppData) {
        $relative = $fullPath.Substring($appData.Length).TrimStart('\', '/')
        return "<APPDATA>\$relative"
    }

    return "<LOCAL_PATH>\$(Split-Path $fullPath -Leaf)"
}

function Expand-Candidates([string[]]$RelativePaths) {
    @($searchRoots | ForEach-Object {
        $searchRoot = $_
        $RelativePaths | ForEach-Object { Join-Path $searchRoot $_ }
    })
}

$games = @(
    Get-FileReport 'GTA III Definitive Edition' 'CLEO Redux x64' (Expand-Candidates @(
        'Gameface\Binaries\Win64\LibertyCity.exe',
        'Grand Theft Auto III - Definitive Edition\Gameface\Binaries\Win64\LibertyCity.exe',
        'Grand Theft Auto III - The Definitive Edition\Gameface\Binaries\Win64\LibertyCity.exe'
    ))
    Get-FileReport 'GTA Vice City Definitive Edition' 'CLEO Redux x64' (Expand-Candidates @(
        'Gameface\Binaries\Win64\ViceCity.exe',
        'Grand Theft Auto Vice City - Definitive Edition\Gameface\Binaries\Win64\ViceCity.exe',
        'Grand Theft Auto Vice City - The Definitive Edition\Gameface\Binaries\Win64\ViceCity.exe'
    ))
    Get-FileReport 'GTA San Andreas Definitive Edition' 'CLEO Redux x64' (Expand-Candidates @(
        'Gameface\Binaries\Win64\SanAndreas.exe',
        'Grand Theft Auto San Andreas - Definitive Edition\Gameface\Binaries\Win64\SanAndreas.exe',
        'Grand Theft Auto San Andreas - The Definitive Edition\Gameface\Binaries\Win64\SanAndreas.exe'
    ))
    Get-FileReport 'GTA IV Complete Edition' 'CLEO Redux x86' (Expand-Candidates @(
        'Grand Theft Auto IV\GTAIV\GTAIV.exe',
        'Grand Theft Auto IV\GTAIV.exe'
    ))
    Get-FileReport 'GTA V Enhanced' 'ScriptHookV bridge' (Expand-Candidates @(
        'Grand Theft Auto V Enhanced\GTA5_Enhanced.exe',
        'Grand Theft Auto V Enhanced\GTA5.exe'
    ))
)

$report = [ordered]@{
    generated_utc = [DateTime]::UtcNow.ToString('o')
    windows = [Environment]::OSVersion.VersionString
    steam_library_count = $steamRoots.Count
    rockstar_install_root_count = $rockstarRoots.Count
    additional_search_root_count = $validAdditionalRoots.Count
    games = $games
    notes = @(
        'Read-only scan: no game or save files were changed.',
        'A missing result may mean the game is installed through Rockstar/Epic or in a non-standard folder.',
        'Paths are privacy-redacted unless -IncludeFullPaths is explicitly supplied.'
    )
}

if ($IncludeFullPaths) {
    $report['steam_roots'] = $steamRoots
    $report['rockstar_install_roots'] = $rockstarRoots
    $report['additional_search_roots'] = $validAdditionalRoots
}

$json = $report | ConvertTo-Json -Depth 6
$json | Set-Content -LiteralPath $OutputPath -Encoding UTF8
$json
Write-Host "`nSaved report to: $OutputPath" -ForegroundColor Green
