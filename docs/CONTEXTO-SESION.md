# Contexto para continuar el trabajo — Análisis de Vibraciones v2.0

Actualizado: 20/11/2026 · Autor del proyecto: Fabian Alaniz – ElectroNet (+54 9 351 284-8802 · www.developers-soft.com)

## Reglas del autor
- Los commits van a nombre de **Fabian Alaniz <hfaalaniz@users.noreply.github.com>**, **sin** líneas `Co-Authored-By` ni menciones de asistentes de IA (ni en commits, ni en PR, ni en el repo).
- Respuestas en español, cortas, priorizando código.
- El enlace web del autor debe mostrarse como `www.developers-soft.com` y apuntar a `https://www.developers-soft.com/`.

## Estado
- Repositorio: https://github.com/hfaalaniz/AnalisisDeVibraciones — rama única `main` (PR #1 y #2 mezclados; ramas de trabajo borradas).
- **Versión publicada: `v2.0.0`** — https://github.com/hfaalaniz/AnalisisDeVibraciones/releases/tag/v2.0.0 (`AnalisisVibraciones-win-x64.zip`).
- CI (GitHub Actions, `.github/workflows/build.yml`), verde. Se dispara en push a `main`, en PR y en etiquetas `v*`:
  - «App de escritorio (Windows)»: compila la solución, corre 28 pruebas del núcleo y publica `AnalisisVibraciones-win-x64.zip` (en Releases si es etiqueta `v*`).
  - «Versión web (pruebas)»: genera vectores desde C# y corre 13 pruebas Node (incluida paridad numérica C# ↔ JS).
- Vista previa web publicada como artefacto: https://claude.ai/artifact/RH7N5vCjd45n1tALCNuyrr
- Entorno del autor: Windows con PowerShell 5.1 (no admite `&&`: un comando por línea), .NET 8 y Node 26. Clon local en `C:\Users\Fabian\analisisdevibraciones`.

## Flujo de trabajo
1. El asistente trabaja en una rama (`fix/...`, `docs/...`, `herramientas/...`) creada desde `main`, hace push y abre un PR por REST:
   `gh api repos/hfaalaniz/AnalisisDeVibraciones/pulls -f title=... -f head=RAMA -f base=main -f body=...`
   (GraphQL no está disponible en estas sesiones: `gh pr ...` falla, usar `gh api`.)
2. El autor prueba en Windows: `.\probar.ps1 -Rama RAMA` (fetch + pull, pruebas C# y web, abre la app). `-Ejecutar web` sirve la web en `http://localhost:8000/app/`.
3. Mezclar solo con autorización **explícita** del autor en ese mensaje (p. ej. «mezclá el PR #N»):
   `gh api -X PUT repos/hfaalaniz/AnalisisDeVibraciones/pulls/N/merge -f merge_method=merge`
4. El autor vuelve a `main`: `.\probar.ps1 -Volver` (pull y borra ramas locales mezcladas). Ramas remotas: `git push origin --delete RAMA`.
5. **Etiquetas**: la sesión no puede empujar ni crear etiquetas (bloqueado por el proxy). Se le pasan los comandos al autor:
   ```powershell
   git switch main
   git pull
   git tag -a v2.0.1 -m "Análisis de Vibraciones v2.0.1"
   git push origin v2.0.1
   ```

## Pendientes
- Opcional: conectar Cloudflare Pages (preset None, sin build, directorio de salida `web`).
- Probar el firmware ESP32 en hardware real.
- Probar la app de escritorio en campo con sensor real (ya se ejecutó en Windows y abre correctamente).

## Qué se hizo (resumen)
- **Errores corregidos** del código original: coeficientes de balanceo inventados → método de coeficientes de influencia con peso de prueba; fase siempre ≈270° → ajuste por mínimos cuadrados referido al tacómetro; corrección "simulada" en 0,02 → simulador físico; temas no recargables (Color en JSON) y guardados en Program Files → %LOCALAPPDATA%; log de depuración en cada repintado → desactivado por defecto y con rotación; `.csproj` con formularios inexistentes; se quitó MathNet (FFT propia).
- **Estructura nueva**
  - `src/VibrationAnalysis.Core/` (.NET 8, sin dependencias): `Dsp/` (FFT radix-2 + Bluestein, ventanas, espectro, FitSinusoid, integración, tacómetro), `Analysis/` (severidad ISO 10816-1/-3, diagnóstico por reglas, frecuencias de rodamiento), `Balancing/` (InfluenceBalancer 1 y 2 planos + trim, BalancingSession con orden de pasos, ISO 1940, peso de prueba, reparto entre álabes, perforación), `Simulation/RotorSimulator`, `IO/` (Recording, CSV, WAV, protocolo serie `#VA`), `Model/Machine`, `Report/HtmlReport`.
  - `src/VibrationAnalysis.UI/` WinForms: `MainForm.cs` (pestañas 1 Máquina, 2 Adquisición, 3 Diagnóstico, 4 Balanceo guiado, 5 Informe; proyectos `.vaproj`; puerto serie con System.IO.Ports), `Views.cs` (PropertyGrid en español), `Controls/BalancePolarControl.cs`, controles originales reutilizados (SignalControl, SpectrumPlotControl, PolarGraphControl), ThemeManager y LogManager corregidos.
  - `tests/VibrationAnalysis.Core.Tests/` consola con 28 pruebas; `--emit <archivo>` genera vectores para la web.
  - `web/index.html` página del producto; `web/app/` app web (Análisis, Balanceo, Máquina, Informe); `web/js/core/` motor JS equivalente al C#; `web/js/ui/` interfaz y gráficos en canvas; `web/tests/` pruebas Node; `web/_headers` para Cloudflare.
  - `firmware/esp32_vibraciones/` ESP32 + ADXL345 (SPI, 3200 Hz) + tacómetro óptico; protocolo de texto a 921600 baudios.
  - `AnalisisDeVibraciones.sln`, `README.md` reescrito.
- **Verificado**: balanceo de punta a punta con el simulador (6,35 → 0,09 mm/s en 3 corridas, cumple G6,3); web probada en navegador (escritorio y celular).
- **Herramientas**: `probar.ps1` (raíz) automatiza la prueba de ramas en Windows; `web/js/package.json` declara módulos ES para Node.
- **Verificado en Windows**: `probar.ps1` completo (28 + 13 pruebas) y la app de escritorio abre.

## Comandos útiles
```bash
git clone https://github.com/hfaalaniz/AnalisisDeVibraciones && cd AnalisisDeVibraciones
dotnet run --project tests/VibrationAnalysis.Core.Tests -- --emit web/tests/vectors.json
node --test web/tests/*.test.mjs        # rutas relativas con '/': Node 21+ las trata como glob
gh run list --repo hfaalaniz/AnalisisDeVibraciones --limit 3
```
Nota del entorno anterior: NuGet estaba bloqueado (se compiló con un `nuget.config` vacío); el SDK de .NET 8 se instaló con `apt-get install dotnet-sdk-8.0`. WinForms no compila en Linux sin el paquete de referencia de WindowsDesktop: se verifica en el CI de Windows.
