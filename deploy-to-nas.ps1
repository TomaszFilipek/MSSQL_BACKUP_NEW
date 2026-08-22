# =============================================================================
#  deploy-to-nas.ps1
#  Kopiuje projekt na NAS (bez artefaktow build) i przebudowuje kontenery Docker
# =============================================================================

$NAS_IP   = "192.168.1.2"
$NAS_USER = "tomasz"
$NAS_PASS = "Wronski_Nas_2022"
$NAS_ROOT = "/volume1/TOMASZ/YTScriptTracker"             # katalog z Dockerfile i docker-compose.yml
$NAS_PATH = "$NAS_ROOT/YTScriptTracker"                   # katalog z kodem C#

$LOCAL_SOURCE = Join-Path $PSScriptRoot "YTScriptTracker"

# ---------------------------------------------------------------------------
#  Funkcje pomocnicze
# ---------------------------------------------------------------------------

function Write-Step([string]$msg) {
    Write-Host "`n==> $msg" -ForegroundColor Cyan
}

function Write-OK([string]$msg) {
    Write-Host "    [OK] $msg" -ForegroundColor Green
}

function Write-Fail([string]$msg) {
    Write-Host "    [BLAD] $msg" -ForegroundColor Red
}

function Find-Exe([string[]]$names) {
    foreach ($name in $names) {
        $found = Get-Command $name -ErrorAction SilentlyContinue
        if ($found) { return $found.Source }

        $candidates = @(
            "C:\Program Files\PuTTY\$name.exe",
            "C:\Program Files (x86)\PuTTY\$name.exe",
            "$env:LOCALAPPDATA\Programs\PuTTY\$name.exe"
        )
        foreach ($c in $candidates) {
            if (Test-Path $c) { return $c }
        }
    }
    return $null
}

# ---------------------------------------------------------------------------
#  Sprawdz narzedzia: plink + pscp (PuTTY)
# ---------------------------------------------------------------------------

Write-Step "Szukam narzedzi SSH (PuTTY)..."

$plink = Find-Exe @("plink")
$pscp  = Find-Exe @("pscp")

if (-not $plink -or -not $pscp) {
    Write-Fail "Nie znaleziono plink.exe lub pscp.exe (PuTTY)."
    Write-Host ""
    Write-Host "  Zainstaluj PuTTY: winget install PuTTY.PuTTY" -ForegroundColor Yellow
    exit 1
}

Write-OK "plink : $plink"
Write-OK "pscp  : $pscp"

# ---------------------------------------------------------------------------
#  Przygotuj katalog tymczasowy (czysty - bez artefaktow)
# ---------------------------------------------------------------------------

Write-Step "Przygotowuje czysta kopie projektu..."

$tempDir = Join-Path $env:TEMP "YTScriptTracker_deploy"

if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

$excludeDirs = @(
    "node_modules", "dist", ".angular",
    "bin", "obj",
    ".git", ".vs", ".vscode", ".github",
    ".idea"
)

$excludeFiles = @("*.user", "*.suo", "Thumbs.db", ".DS_Store")

$robocopyArgs = @(
    $LOCAL_SOURCE,
    $tempDir,
    "/E",
    "/XD"
) + $excludeDirs + @(
    "/XF"
) + $excludeFiles + @(
    "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS"
)

$rc = Start-Process -FilePath "robocopy" -ArgumentList $robocopyArgs -Wait -PassThru -NoNewWindow

if ($rc.ExitCode -ge 8) {
    Write-Fail "Robocopy zakonczyl sie bledem (kod $($rc.ExitCode))."
    exit 1
}

Write-OK "Skopiowano do: $tempDir"

$fileCount = (Get-ChildItem $tempDir -Recurse -File).Count
Write-Host "    Pliki do transferu: $fileCount" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
#  Wyczysc katalog docelowy na NAS i skopiuj pliki
# ---------------------------------------------------------------------------

Write-Step "Przesylam pliki na NAS ($NAS_USER@${NAS_IP}:$NAS_PATH)..."

$sshClean = "mkdir -p `"$NAS_PATH`" && mkdir -p `"$NAS_ROOT/Data`""

& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" $sshClean
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Nie mozna polaczyc sie z NAS. Sprawdz IP, login i haslo."
    exit 1
}
Write-OK "Polaczenie SSH OK"

# Wyczysc stare pliki zrodlowe na NAS (zapobiega duplikatom i starym plikoem)
Write-Host "    >> Czyszcze katalog zrodlowy na NAS..." -ForegroundColor DarkGray
& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "rm -rf `"$NAS_PATH`" && mkdir -p `"$NAS_PATH`""

# Skopiuj Dockerfile i docker-compose.yml do katalogu nadrzednego na NAS
Write-Host "    >> Kopiowanie Dockerfile i docker-compose.yml..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "Dockerfile")          "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/Dockerfile"
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "docker-compose.yml")  "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/docker-compose.yml"

# Skopiuj core script PS do katalogu Data na NAS (tam trafi do wolumenu Dockera)
Write-Host "    >> Kopiowanie YT-core.ps1 do Data na NAS..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "SH\YT-core.ps1") "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/Data/yt-core.ps1"

# Skopiuj kod C# do podkatalogu YTScriptTracker
& $pscp -pw $NAS_PASS -r -batch "$tempDir\*" "${NAS_USER}@${NAS_IP}:${NAS_PATH}"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Blad podczas kopiowania plikow na NAS."
    exit 1
}

Write-OK "Transfer zakonczony pomyslnie"

# ---------------------------------------------------------------------------
#  Przebuduj kontenery Docker na NAS
# ---------------------------------------------------------------------------

Write-Step "Przebudowuje kontenery Docker na NAS..."

# Generujemy skrypt bash lokalnie i kopiujemy na NAS — zero problemow z quoting
$deployScript = @"
#!/bin/bash
set -e
export PATH=/usr/local/bin:/usr/bin:/bin:`$PATH

COMPOSE_FILE="$NAS_ROOT/docker-compose.yml"

echo ">>> Zatrzymywanie kontenerow..."
docker-compose -f "`$COMPOSE_FILE" down --remove-orphans

mkdir -p "$NAS_ROOT/Data"

echo ">>> Budowanie obrazow (bez cache)..."
docker-compose -f "`$COMPOSE_FILE" build --no-cache

echo ">>> Uruchamianie kontenerow..."
docker-compose -f "`$COMPOSE_FILE" up -d

echo ">>> Status kontenerow:"
docker-compose -f "`$COMPOSE_FILE" ps
"@

$deployScriptLocal = Join-Path $env:TEMP "YTScriptTracker_deploy.sh"
[System.IO.File]::WriteAllText($deployScriptLocal, $deployScript.Replace("`r`n", "`n"))

Write-Host "    >> Kopiowanie skryptu deploy na NAS..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -batch $deployScriptLocal "${NAS_USER}@${NAS_IP}:/tmp/YTScriptTracker_deploy.sh"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Nie mozna skopiowac skryptu na NAS."
    exit 1
}
Remove-Item $deployScriptLocal -Force

Write-Host "    >> Uruchamianie Docker build na NAS..." -ForegroundColor DarkGray
& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "echo '$NAS_PASS' | sudo -S bash /tmp/YTScriptTracker_deploy.sh; EXIT_CODE=`$?; rm -f /tmp/YTScriptTracker_deploy.sh; exit `$EXIT_CODE"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Blad podczas przebudowy Docker na NAS (exit code $LASTEXITCODE)."
    exit 1
}

# ---------------------------------------------------------------------------
#  Sprzatanie
# ---------------------------------------------------------------------------

Write-Step "Sprzatam katalog tymczasowy..."
Remove-Item $tempDir -Recurse -Force
Write-OK "Gotowe!"

Write-Host ""
Write-Host "  Aplikacja dostepna pod: http://${NAS_IP}:8086" -ForegroundColor Magenta
Write-Host ""