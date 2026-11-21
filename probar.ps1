<#
.SYNOPSIS
  Trae una rama del repositorio, la compila, corre las pruebas y ejecuta la app.

.EXAMPLE
  .\probar.ps1 -Listar                       # ramas disponibles en GitHub
  .\probar.ps1 -Rama fix/xxx                 # actualiza, prueba y abre la app de escritorio
  .\probar.ps1 -Rama fix/xxx -Ejecutar web   # sirve la web en http://localhost:8000/app/
  .\probar.ps1                               # repite la prueba en la rama actual (con pull)
  .\probar.ps1 -Volver                       # vuelve a main y borra ramas ya mezcladas

.NOTES
  Análisis de Vibraciones v2 · Fabian Alaniz – ElectroNet · www.developers-soft.com
  Si Windows bloquea el script:  powershell -ExecutionPolicy Bypass -File .\probar.ps1 ...
#>
[CmdletBinding()]
param(
    [string]$Rama,
    [ValidateSet('todo', 'pruebas', 'escritorio', 'web', 'nada')]
    [string]$Ejecutar = 'todo',
    [int]$Puerto = 8000,
    [switch]$Volver,
    [switch]$Listar,
    [switch]$Guardar,     # guarda cambios locales sin commit (git stash) en vez de abortar
    [switch]$Reiniciar    # descarta commits locales y deja la rama igual a GitHub
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# ---------- utilidades ----------
function Titulo([string]$t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Ok([string]$t)     { Write-Host "  OK  $t" -ForegroundColor Green }
function Aviso([string]$t)  { Write-Host "  !!  $t" -ForegroundColor Yellow }
function Falla([string]$t)  { Write-Host "  XX  $t" -ForegroundColor Red; exit 1 }

function Ejecutar-Nativo([string]$exe, [string[]]$argumentos, [string]$errorMsg) {
    & $exe @argumentos
    if ($LASTEXITCODE -ne 0) { Falla "$errorMsg (código $LASTEXITCODE)" }
}

function Existe([string]$cmd) { [bool](Get-Command $cmd -ErrorAction SilentlyContinue) }

function Rama-Actual { (git rev-parse --abbrev-ref HEAD).Trim() }

function Verificar-Limpio {
    $cambios = git status --porcelain
    if (-not $cambios) { return }
    if ($Guardar) {
        Ejecutar-Nativo git @('stash', 'push', '-u', '-m', "probar.ps1 $(Get-Date -Format s)") 'No se pudo guardar con stash'
        Aviso "Cambios locales guardados. Para recuperarlos: git stash pop"
    } else {
        Write-Host $cambios
        Falla "Hay cambios sin commit. Usá -Guardar para apartarlos (git stash) o descartalos."
    }
}

# ---------- requisitos ----------
if (-not (Existe git)) { Falla "No se encontró git en el PATH." }
if (-not (Test-Path .git)) { Falla "Este script debe estar en la raíz del repositorio." }

# ---------- -Listar ----------
if ($Listar) {
    Titulo "Ramas en GitHub"
    Ejecutar-Nativo git @('fetch', 'origin', '--prune', '--quiet') 'Falló git fetch'
    git for-each-ref --sort=-committerdate refs/remotes/origin `
        --format='%(refname:lstrip=3)|%(committerdate:relative)|%(contents:subject)' |
        Where-Object { $_ -notmatch '^HEAD\|' } |
        ForEach-Object {
            $p = $_ -split '\|', 3
            '{0,-35} {1,-18} {2}' -f $p[0], $p[1], $p[2]
        }
    exit 0
}

# ---------- -Volver ----------
if ($Volver) {
    Verificar-Limpio
    Titulo "Volviendo a main"
    Ejecutar-Nativo git @('fetch', 'origin', '--prune') 'Falló git fetch'
    Ejecutar-Nativo git @('switch', 'main') 'No se pudo cambiar a main'
    Ejecutar-Nativo git @('pull', '--ff-only') 'No se pudo actualizar main'
    $mezcladas = git branch --merged main --format='%(refname:short)' | Where-Object { $_ -ne 'main' }
    foreach ($r in $mezcladas) {
        git branch -d $r | Out-Null
        Ok "Rama local borrada: $r"
    }
    git log --oneline -3
    exit 0
}

# ---------- actualizar la rama ----------
Verificar-Limpio
Titulo "Actualizando desde GitHub"
Ejecutar-Nativo git @('fetch', 'origin', '--prune') 'Falló git fetch'

if (-not $Rama) { $Rama = Rama-Actual }
git rev-parse --verify --quiet "refs/remotes/origin/$Rama" | Out-Null
if ($LASTEXITCODE -ne 0) { Falla "La rama '$Rama' no existe en GitHub. Probá: .\probar.ps1 -Listar" }

if ((Rama-Actual) -ne $Rama) {
    Ejecutar-Nativo git @('switch', $Rama) "No se pudo cambiar a $Rama"
}

if ($Reiniciar) {
    Ejecutar-Nativo git @('reset', '--hard', "origin/$Rama") 'Falló git reset'
} else {
    git pull --ff-only
    if ($LASTEXITCODE -ne 0) {
        Falla "La rama local y la de GitHub divergieron. Si no tenés commits propios en '$Rama', repetí con -Reiniciar."
    }
}
Ok "Rama: $Rama"
git log --oneline -5

# ---------- pruebas ----------
$fallos = @()

if ($Ejecutar -in 'todo', 'pruebas') {
    if (-not (Existe dotnet)) { Falla "No se encontró el SDK de .NET 8 (dotnet)." }

    Titulo "Pruebas del núcleo (C#)"
    dotnet run --project tests\VibrationAnalysis.Core.Tests -c Release -- --emit web\tests\vectors.json
    if ($LASTEXITCODE -eq 0) { Ok "Núcleo C#" } else { $fallos += 'Núcleo C#' }

    Titulo "Pruebas de la web (Node)"
    if (Existe node) {
        Write-Host "  node $(node --version)"
        # Rutas relativas con '/': Node 21+ trata los argumentos de --test como glob y '\' es escape.
        $archivos = Get-ChildItem web\tests\*.test.mjs | ForEach-Object { 'web/tests/' + $_.Name }
        $argsNode = @('--test', '--test-timeout=60000') + $archivos
        $proc = Start-Process node -ArgumentList $argsNode -NoNewWindow -PassThru
        $null = $proc.Handle                       # necesario para leer ExitCode luego
        if (-not $proc.WaitForExit(180000)) {      # 3 min como máximo
            Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            Aviso "Las pruebas web superaron 3 minutos: proceso detenido."
            $fallos += 'Web (Node, tiempo agotado)'
        } elseif ($proc.ExitCode -eq 0) { Ok "Web / paridad C# ↔ JS" } else { $fallos += 'Web (Node)' }
    } else {
        Aviso "Node no está instalado: se omiten las pruebas web (https://nodejs.org)."
    }
    # vectors.json está versionado: se restaura para no dejar cambios sueltos
    git checkout -- web/tests/vectors.json

    if ($fallos.Count -gt 0) {
        Falla ("Fallaron: " + ($fallos -join ', ') + ". No se ejecuta la app.")
    }
}

# ---------- app de escritorio ----------
if ($Ejecutar -in 'todo', 'escritorio') {
    if (-not (Existe dotnet)) { Falla "No se encontró el SDK de .NET 8 (dotnet)." }
    Titulo "Compilando la app de escritorio"
    Ejecutar-Nativo dotnet @('build', 'src\VibrationAnalysis.UI', '-c', 'Debug', '--nologo', '-v', 'q') 'La app no compila'
    $exe = Get-ChildItem src\VibrationAnalysis.UI\bin\Debug -Recurse -Filter AnalisisVibraciones.exe |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { Falla "No se encontró AnalisisVibraciones.exe después de compilar." }
    Ok "Abriendo $($exe.FullName)"
    Start-Process $exe.FullName -WorkingDirectory $exe.DirectoryName
}

# ---------- web ----------
if ($Ejecutar -eq 'web') {
    Titulo "Sirviendo la web en http://localhost:$Puerto/"
    $url = "http://localhost:$Puerto/app/"
    $py = $null
    foreach ($c in 'py', 'python') {
        if (Existe $c) {
            try { & $c --version *> $null; if ($LASTEXITCODE -eq 0) { $py = $c; break } } catch { }   # descarta el alias de Microsoft Store
        }
    }
    if ($py) {
        Start-Process $url
        Aviso "Ctrl+C para detener el servidor."
        & $py -m http.server $Puerto --directory web
    } elseif (Existe npx) {
        Start-Process $url
        Aviso "Ctrl+C para detener el servidor."
        npx --yes http-server web -p $Puerto -c-1
    } else {
        Falla "Se necesita Python o Node para servir la web."
    }
}

Titulo "Listo"
Ok "Rama '$Rama' probada. Cuando esté aprobada y mezclada: .\probar.ps1 -Volver"
