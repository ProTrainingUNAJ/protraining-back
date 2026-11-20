# ==============================================================================
# ProTraining - Script de Prueba de Integración Real de Purga de Atletas (KAN-501)
# ==============================================================================
param(
    [string]$IdentidadUrl = "http://localhost:8081",
    [string]$EstructuraUrl = "http://localhost:8082",
    [string]$EntrenamientoUrl = "http://localhost:8083",
    [string]$SeguimientoUrl = "http://localhost:8084",
    [string]$AsistenteUrl = "http://localhost:8085"
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ProjectPath = [System.IO.Path]::Combine($RepoRoot, "services", "estructura", "Tests", "Estructura.Tests.csproj")

Write-Host ""
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " [PROTRAINING] PRUEBA E2E REAL DE PURGA DE ATLETA (KAN-501)" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Comprobar disponibilidad de los 5 microservicios en Docker Compose
Write-Host "[1/2] Verificando conectividad con los 5 servicios..." -ForegroundColor Yellow

$services = @(
    @{ Name = "Identidad"; Url = "$IdentidadUrl/health" },
    @{ Name = "Estructura"; Url = "$EstructuraUrl/health" },
    @{ Name = "Entrenamiento"; Url = "$EntrenamientoUrl/health" },
    @{ Name = "Seguimiento"; Url = "$SeguimientoUrl/health" },
    @{ Name = "Asistente"; Url = "$AsistenteUrl/health" }
)

$offline = @()
foreach ($s in $services) {
    try {
        $null = Invoke-RestMethod -Uri $s.Url -Method Get -TimeoutSec 3 -ErrorAction Stop
        Write-Host "  -> $($s.Name): ONLINE (HTTP 200)" -ForegroundColor Green
    }
    catch {
        Write-Host "  -> $($s.Name): OFFLINE ($($s.Url))" -ForegroundColor Red
        $offline += $s.Name
    }
}

if ($offline.Count -gt 0) {
    Write-Host ""
    Write-Host "No se puede ejecutar la prueba de integracion real: faltan servicios activos ($($offline -join ', '))." -ForegroundColor Red
    Write-Host "Levanta el entorno antes de correr este script:" -ForegroundColor Yellow
    Write-Host "  docker compose up -d --build --wait identidad estructura entrenamiento seguimiento asistente" -ForegroundColor White
    exit 1
}

# 2. Ejecutar la prueba real de integración entre microservicios (sin mocks)
Write-Host ""
Write-Host "[2/2] Ejecutando prueba de integracion real entre los 5 servicios (Category=ServiceIntegration)..." -ForegroundColor Yellow

$env:PROTRAINING_TEST_IDENTIDAD_URL = $IdentidadUrl
$env:PROTRAINING_TEST_ESTRUCTURA_URL = $EstructuraUrl
$env:PROTRAINING_TEST_ENTRENAMIENTO_URL = $EntrenamientoUrl
$env:PROTRAINING_TEST_SEGUIMIENTO_URL = $SeguimientoUrl
$env:PROTRAINING_TEST_ASISTENTE_URL = $AsistenteUrl

dotnet test "$ProjectPath" --filter "Category=ServiceIntegration&FullyQualifiedName~AthletePurgeServiceIntegrationTests" --logger "console;verbosity=normal"
$exitCode = $LASTEXITCODE

Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "Prueba de integracion real finalizada correctamente (exit code 0)." -ForegroundColor Green
}
else {
    Write-Host "La prueba de integracion real fallo (exit code $exitCode)." -ForegroundColor Red
}
Write-Host ""
exit $exitCode
