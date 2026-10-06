[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is required to run this check.'
}

$repoRoot = (& git rev-parse --show-toplevel 2>$null)
if (-not $repoRoot) {
    throw 'Run this script from inside the Git repository.'
}

Push-Location $repoRoot
try {
    $failed = $false
    $tracked = @(& git ls-files)

    $forbiddenNames = @(
        '*.log', '*.dmp', '*.dump', '*.pdb', '*.pfx', '*.key',
        '.env', '.env.*', 'GHMR-compatibility*.json'
    )

    foreach ($file in $tracked) {
        foreach ($pattern in $forbiddenNames) {
            if ($file -like $pattern -or (Split-Path $file -Leaf) -like $pattern) {
                Write-Host "Tracked private/generated file: $file" -ForegroundColor Red
                $failed = $true
            }
        }
    }

    $self = 'tools/Test-RepoHygiene.ps1'
    $textFiles = $tracked | Where-Object {
        $_ -ne $self -and $_ -match '\.(cs|csproj|sln|js|ts|json|md|txt|yml|yaml|xml|props|targets|ps1)$'
    }

    $sensitivePatterns = @(
        'C:\\Users\\',
        '/Users/',
        '/home/',
        '/workspace/',
        '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
    )

    foreach ($file in $textFiles) {
        $lineNumber = 0
        foreach ($line in Get-Content -LiteralPath $file) {
            $lineNumber++
            foreach ($pattern in $sensitivePatterns) {
                if ($line -match $pattern) {
                    Write-Host "Review possible private data: ${file}:${lineNumber}" -ForegroundColor Yellow
                    $failed = $true
                    break
                }
            }
        }
    }

    if ($failed) {
        Write-Host 'Repository hygiene check failed. Review every item above.' -ForegroundColor Red
        exit 1
    }

    Write-Host 'Repository hygiene check passed.' -ForegroundColor Green
} finally {
    Pop-Location
}

