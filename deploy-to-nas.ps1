# =============================================================================
#  deploy-to-nas.ps1
#  Kopiuje projekt MSSQL_BACKUP_NEW na NAS i przebudowuje kontenery Docker
# =============================================================================

param(
    [ValidateSet("api", "web", "all")]
    [string]$Service = ""
)

$NAS_IP   = "192.168.1.2"
$NAS_USER = "tomasz"
$NAS_PASS = "Wronski_Nas_2022"
$NAS_ROOT = "/volume1/TOMASZ/MSSQL_BACKUP_NEW"

$LOCAL_SOURCE = $PSScriptRoot

# ---------------------------------------------------------------------------
#  Menu wyboru serwisu
# ---------------------------------------------------------------------------

if (-not $Service) {
    Write-Host ""
    Write-Host "Co chcesz zdeploy'owac?" -ForegroundColor Cyan
    Write-Host "  1) Tylko API   (port 8283)" -ForegroundColor Yellow
    Write-Host "  2) Tylko Web   (port 8284)" -ForegroundColor Yellow
    Write-Host "  3) Oba (API + Web)" -ForegroundColor Yellow
    Write-Host ""
    $choice = Read-Host "Wybierz (1/2/3)"
    switch ($choice) {
        "1" { $Service = "api" }
        "2" { $Service = "web" }
        "3" { $Service = "all" }
        default { Write-Host "Nieprawidlowy wybor." -ForegroundColor Red; exit 1 }
    }
}

Write-Host ""
Write-Host ">>> Deploy serwisu: $Service" -ForegroundColor Cyan

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
#  Polacz z NAS
# ---------------------------------------------------------------------------

Write-Step "Lacze z NAS ($NAS_USER@${NAS_IP})..."

& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "mkdir -p `"$NAS_ROOT`""
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Nie mozna polaczyc sie z NAS. Sprawdz IP, login i haslo."
    exit 1
}
Write-OK "Polaczenie SSH OK"

# ---------------------------------------------------------------------------
#  Kopiowanie plikow na NAS
# ---------------------------------------------------------------------------

Write-Step "Przesylam pliki na NAS..."

# Zawsze kopiuj docker-compose.yml i .dockerignore (potrzebne dla obu serwisow)
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "docker-compose.yml") "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/docker-compose.yml"
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot ".dockerignore")      "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/.dockerignore"

# Skopiuj src/ (wspoldzielone dla obu serwisow)
Write-Host "    >> Kopiowanie kodu zrodlowego (src/)..." -ForegroundColor DarkGray

$tempDir = Join-Path $env:TEMP "MSSQL_BACKUP_NEW_deploy"
if (Test-Path $tempDir) {
    Remove-Item $tempDir -Recurse -Force
}
New-Item -ItemType Directory -Path $tempDir | Out-Null

$excludeDirs = @(
    ".git", ".vs", ".vscode", ".github", ".idea",
    "bin", "obj", "publish"
)
$excludeFiles = @("*.user", "*.suo", "Thumbs.db", ".DS_Store", "*.cache", "*.vsidx", "*.dtbcache.v2")

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

# Wyczysc src/ na NAS
& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "rm -rf `"$NAS_ROOT/src`" && mkdir -p `"$NAS_ROOT/src`""

# Skopiuj src/
& $pscp -pw $NAS_PASS -r -batch "$tempDir\src\*" "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/src/"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Blad podczas kopiowania plikow na NAS."
    exit 1
}

# Kopiuj pliki Docker na podstawie wybranego serwisu
if ($Service -eq "api" -or $Service -eq "all") {
    Write-Host "    >> Kopiowanie Dockerfile (API)..." -ForegroundColor DarkGray
    & $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "Dockerfile") "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/Dockerfile"
    & $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "rm -f `"$NAS_ROOT/Dockerfile.web`""
}

if ($Service -eq "web" -or $Service -eq "all") {
    Write-Host "    >> Kopiowanie Dockerfile.web..." -ForegroundColor DarkGray
    & $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "Dockerfile.web") "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/Dockerfile.web"
}

if ($Service -eq "all") {
    & $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "MSSQL_BACKUP_NEW.slnx") "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/MSSQL_BACKUP_NEW.slnx"
}

Write-OK "Transfer zakonczony"

# ---------------------------------------------------------------------------
#  Przebuduj kontenery Docker na NAS
# ---------------------------------------------------------------------------

Write-Step "Przebudowuje kontenery Docker na NAS..."

$deployServices = switch ($Service) {
    "api" { "api" }
    "web" { "web" }
    "all" { "api web" }
}

$deployScript = @"
#!/bin/bash
set -e
export PATH=/usr/local/bin:/usr/bin:/bin:`$PATH

COMPOSE_FILE="$NAS_ROOT/docker-compose.yml"

echo ">>> Zatrzymywanie serwisow: $deployServices..."
docker-compose -f "`$COMPOSE_FILE" stop $deployServices

echo ">>> Budowanie obrazow (bez cache)..."
docker-compose -f "`$COMPOSE_FILE" build --no-cache $deployServices

echo ">>> Uruchamianie serwisow..."
docker-compose -f "`$COMPOSE_FILE" up -d $deployServices

echo ">>> Status:"
docker-compose -f "`$COMPOSE_FILE" ps $deployServices
"@

$deployScriptLocal = Join-Path $env:TEMP "MSSQL_BACKUP_NEW_deploy.sh"
[System.IO.File]::WriteAllText($deployScriptLocal, $deployScript.Replace("`r`n", "`n"))

Write-Host "    >> Kopiowanie skryptu deploy na NAS..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -batch $deployScriptLocal "${NAS_USER}@${NAS_IP}:/tmp/MSSQL_BACKUP_NEW_deploy.sh"
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Nie mozna skopiowac skryptu na NAS."
    exit 1
}
Remove-Item $deployScriptLocal -Force

Write-Host "    >> Uruchamianie Docker build na NAS..." -ForegroundColor DarkGray
& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "echo '$NAS_PASS' | sudo -S bash /tmp/MSSQL_BACKUP_NEW_deploy.sh; EXIT_CODE=`$?; rm -f /tmp/MSSQL_BACKUP_NEW_deploy.sh; exit `$EXIT_CODE"
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
switch ($Service) {
    "api" { Write-Host "  API: http://${NAS_IP}:8283" -ForegroundColor Magenta }
    "web" { Write-Host "  Web: http://${NAS_IP}:8284" -ForegroundColor Magenta }
    "all" {
        Write-Host "  API: http://${NAS_IP}:8283" -ForegroundColor Magenta
        Write-Host "  Web: http://${NAS_IP}:8284" -ForegroundColor Magenta
    }
}
Write-Host ""
