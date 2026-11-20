# Contexto para continuar el trabajo — Análisis de Vibraciones v2.0

Fecha: 20/11/2026 · Autor del proyecto: Fabian Alaniz – ElectroNet (+54 9 351 284-8802 · www.developers-soft.com)

## Reglas del autor
- Los commits van a nombre de **Fabian Alaniz <hfaalaniz@users.noreply.github.com>**, **sin** líneas `Co-Authored-By` ni menciones de asistentes de IA (ni en commits, ni en PR, ni en el repo).
- Respuestas en español, cortas, priorizando código.
- El enlace web del autor debe mostrarse como `www.developers-soft.com` y apuntar a `https://www.developers-soft.com/`.

## Estado
- Repositorio: https://github.com/hfaalaniz/AnalisisDeVibraciones
- Rama de trabajo: `produccion-v2` (último commit: «App web completa, página del producto, firmware del sensor USB y documentación»).
- **Pull Request abierto: #1** `produccion-v2 → main` — https://github.com/hfaalaniz/AnalisisDeVibraciones/pull/1
- CI (GitHub Actions, `.github/workflows/build.yml`): **verde** en ambos trabajos
  - «App de escritorio (Windows)»: compila la solución, corre 28 pruebas del núcleo y publica `AnalisisVibraciones-win-x64.zip` como artefacto.
  - «Versión web (pruebas)»: genera vectores desde C# y corre 13 pruebas Node (incluida paridad numérica C# ↔ JS).
- Vista previa web publicada como artefacto: https://claude.ai/artifact/RH7N5vCjd45n1tALCNuyrr

## Próximo paso pedido por el autor
1. Mezclar el PR #1 en `main` (desde la nueva sesión). Con `gh` en estas sesiones GraphQL no está disponible: usar REST
   `gh api -X PUT repos/hfaalaniz/AnalisisDeVibraciones/pulls/1/merge -f merge_method=merge`
2. Opcional luego: crear la etiqueta `v2.0.0` en `main` y hacer push para que el CI publique el ejecutable en Releases.
3. Opcional: conectar Cloudflare Pages (preset None, sin build, directorio de salida `web`).

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
- **Pendiente de verificar en campo**: ejecutar la app de Windows (compila en CI, no se ejecutó); firmware no probado en hardware.

## Comandos útiles
```bash
git clone https://github.com/hfaalaniz/AnalisisDeVibraciones && cd AnalisisDeVibraciones && git checkout produccion-v2
dotnet run --project tests/VibrationAnalysis.Core.Tests -- --emit web/tests/vectors.json
node --test web/tests/*.test.mjs
gh run list --branch produccion-v2 --limit 3
```
Nota del entorno anterior: NuGet estaba bloqueado (se compiló con un `nuget.config` vacío); el SDK de .NET 8 se instaló con `apt-get install dotnet-sdk-8.0`. WinForms no compila en Linux sin el paquete de referencia de WindowsDesktop: se verifica en el CI de Windows.
