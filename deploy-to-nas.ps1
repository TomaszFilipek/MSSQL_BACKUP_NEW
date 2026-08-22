# =============================================================================
#  deploy-to-nas.ps1
#  Kopiuje projekt MSSQL_BACKUP_NEW na NAS (bez artefaktow build) i przebudowuje
#  kontenery Docker
# =============================================================================

$NAS_IP   = "192.168.1.2"
$NAS_USER = "tomasz"
$NAS_PASS = "Wronski_Nas_2022"
$NAS_ROOT = "/volume1/TOMASZ/MSSQL_BACKUP_NEW"

$LOCAL_SOURCE = $PSScriptRoot

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

Write-OK "Skopiowano do: $tempDir"

$fileCount = (Get-ChildItem $tempDir -Recurse -File).Count
Write-Host "    Pliki do transferu: $fileCount" -ForegroundColor DarkGray

# ---------------------------------------------------------------------------
#  Wyczysc katalog docelowy na NAS i skopiuj pliki
# ---------------------------------------------------------------------------

Write-Step "Przesylam pliki na NAS ($NAS_USER@${NAS_IP}:$NAS_ROOT)..."

$sshClean = "mkdir -p `"$NAS_ROOT`""

& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" $sshClean
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Nie mozna polaczyc sie z NAS. Sprawdz IP, login i haslo."
    exit 1
}
Write-OK "Polaczenie SSH OK"

# Wyczysc stare pliki zrodlowe na NAS (zapobiega duplikatom i starym plikom)
Write-Host "    >> Czyszcze katalog zrodlowy na NAS..." -ForegroundColor DarkGray
& $plink -batch -pw $NAS_PASS "$NAS_USER@$NAS_IP" "rm -rf `"$NAS_ROOT/src`" && rm -f `"$NAS_ROOT/Dockerfile`" && rm -f `"$NAS_ROOT/docker-compose.yml`" && rm -f `"$NAS_ROOT/.dockerignore`" && rm -f `"$NAS_ROOT/MSSQL_BACKUP_NEW.slnx`""

# Skopiuj Dockerfile, docker-compose.yml, .dockerignore i plik rozwiazania
Write-Host "    >> Kopiowanie plikow głównych..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "Dockerfile")              "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/Dockerfile"
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "docker-compose.yml")      "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/docker-compose.yml"
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot ".dockerignore")           "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/.dockerignore"
& $pscp -pw $NAS_PASS -batch (Join-Path $PSScriptRoot "MSSQL_BACKUP_NEW.slnx")  "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/MSSQL_BACKUP_NEW.slnx"

# Skopiuj kod zrodlowy (src/)
Write-Host "    >> Kopiowanie kodu zrodlowego (src/)..." -ForegroundColor DarkGray
& $pscp -pw $NAS_PASS -r -batch "$tempDir\src\*" "${NAS_USER}@${NAS_IP}:${NAS_ROOT}/src/"
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
ENV_FILE="$NAS_ROOT/.env"

echo ">>> Tworzenie pliku .env..."
cat > "`$ENV_FILE" << 'ENVEOF'
SA_PASSWORD=YourStrong!Password123
ENVEOF

echo ">>> Zatrzymywanie kontenerow..."
docker-compose -f "`$COMPOSE_FILE" down --remove-orphans

echo ">>> Budowanie obrazow (bez cache)..."
docker-compose -f "`$COMPOSE_FILE" build --no-cache

echo ">>> Uruchamianie kontenerow..."
docker-compose -f "`$COMPOSE_FILE" --env-file "`$ENV_FILE" up -d

echo ">>> Status kontenerow:"
docker-compose -f "`$COMPOSE_FILE" --env-file "`$ENV_FILE" ps
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
Write-Host "  API:        http://${NAS_IP}:8283" -ForegroundColor Magenta
Write-Host "  SQL Server: ${NAS_IP}:8284" -ForegroundColor Magenta
Write-Host ""
