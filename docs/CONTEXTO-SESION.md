# Contexto para continuar el trabajo — Análisis de Vibraciones v3.0

Actualizado: 21/11/2026 · Autor del proyecto: Fabian Alaniz – ElectroNet (+54 9 351 284-8802 · www.developers-soft.com)

## Reglas del autor
- Los commits van a nombre de **Fabian Alaniz <hfaalaniz@users.noreply.github.com>**, **sin** líneas `Co-Authored-By` ni menciones de asistentes de IA (ni en commits, ni en PR, ni en el repo).
- Respuestas en español, cortas, priorizando código.
- El enlace web del autor debe mostrarse como `www.developers-soft.com` y apuntar a `https://www.developers-soft.com/`.

## Estado
- Repositorio: https://github.com/hfaalaniz/AnalisisDeVibraciones — rama única `main`. PR #1 a #10 mezclados; GitHub borra la rama de cada PR al mezclarlo (`delete_branch_on_merge`).
- Versión de la app en `main`: **3.0.0** (`src/VibrationAnalysis.UI/VibrationAnalysis.UI.csproj`). Publicada en Releases: `v2.0.0`. **Pendiente: etiquetar `v3.0.0`** (comandos abajo).
- CI (`.github/workflows/build.yml`), verde. Se dispara en push a `main`, en PR y en etiquetas `v*`:
  - «App de escritorio (Windows)»: compila, corre **68 pruebas** del núcleo y publica `AnalisisVibraciones-win-x64.zip` (en Releases si es etiqueta `v*`).
  - «Versión web (pruebas)»: genera vectores desde C# y corre **14 pruebas** Node (paridad C# ↔ JS y protocolo v2).
  - «Firmware ESP32 (compilación)»: arduino-cli compila `esp32_vibraciones` y `esp32_multicanal` (ADXL345/ADXL355/IIS3DWB, ESP32 y ESP32-S3, con WiFi, MQTT y MLX90614).
- Vista previa web: https://claude.ai/artifact/RH7N5vCjd45n1tALCNuyrr
- Entorno del autor: Windows con **PowerShell 7.7** (consola por defecto; la 5.1 no admite `&&`), .NET 8, Node 26, Mosquitto como servicio en el puerto 1883. Clon local en `C:\Users\Fabian\analisisdevibraciones`.

## Flujo de trabajo
1. El asistente trabaja en una rama creada desde `main` (`analisis/…`, `sensores/…`, `red/…`, `docs/…`, `fix/…`), hace push y abre un PR por REST:
   `gh api repos/hfaalaniz/AnalisisDeVibraciones/pulls -f title=... -f head=RAMA -f base=main -f body=...`
   (GraphQL no está disponible en estas sesiones: `gh pr ...` falla, usar `gh api`.)
2. El CI de Windows es la única verificación de la interfaz: en Linux no compila WinForms. Esperar los 3 trabajos en verde antes de entregar.
3. El autor prueba en Windows: `.\probar.ps1 -Rama RAMA` (fetch + pull, pruebas C# y web, compila y abre la app). `-Ejecutar web` sirve la web en `http://localhost:8000/app/`.
4. Mezclar solo con autorización **explícita** del autor en ese mensaje (p. ej. «mezclá el PR #N»):
   `gh api -X PUT repos/hfaalaniz/AnalisisDeVibraciones/pulls/N/merge -f merge_method=merge`
5. El autor vuelve a `main`: `.\probar.ps1 -Volver`.
6. **Etiquetas**: la sesión no puede crear ni empujar etiquetas (bloqueado). Se le pasan los comandos al autor:
   ```powershell
   git switch main
   git pull
   git tag -a v3.0.0 -m "Análisis de Vibraciones v3.0.0"
   git push origin v3.0.0
   ```
7. En PowerShell 7 los argumentos se pasan tal cual: JSON en comillas simples **sin** `\"` (p. ej. `mosquitto_pub -m '{"id":"vt01"}'`).

## App de escritorio (pestañas)
1 Máquina · 2 Adquisición · 3 Diagnóstico · 4 Análisis avanzado · 5 Órdenes y transitorios · 6 Tendencias · 7 Red y planta · 8 Balanceo · 9 Informe.

Fuentes de señal (pestaña 2): simulador (fallas, arranque/parada, sensores X-Y), archivo CSV/WAV, puerto serie (sensor USB), ingreso manual, placa de sonido (IEPE), NI-DAQmx (IEPE), sensor por red (WiFi/TCP).

## Qué se hizo por fase
| Fase | PR | Contenido |
|---|---|---|
| v2.0 | #1–#3 | Rehecho completo (núcleo sin dependencias, balanceo por coeficientes de influencia, app web, firmware USB), `probar.ps1`, documentación. |
| 1 | #4 | Filtros Butterworth de fase cero, Welch (lineal/exponencial/picos), PSD, zoom FFT, cepstrum, envolvente con banda por curtosis espectral, ventanas Blackman-Harris y Kaiser, rótulo de picos, diagnóstico de rodamientos por envolvente (BPFO/BPFI/BSF/FTF) y engranajes (GMF ± 1X). Pestaña «4. Análisis avanzado». |
| 2 | #5 | Remuestreo angular, espectro de órdenes, seguimiento de órdenes (Bode, Nyquist), velocidades críticas con factor Q, cascada, espectrograma, órbita X-Y con precesión. Simulador de arranque/parada. |
| 3 | #6 | Base de mediciones en carpeta (planta → máquina → punto), 17+ indicadores, alarmas (absolutas, por base, estadísticas, ISO 20816), tendencia lineal/exponencial con pronóstico, rutas, demo de 12 meses. |
| 4 | #7 | Firmware multicanal (ADXL345, ADXL355, IIS3DWB; hasta 4 sensores o triaxial; ESP32-S3), protocolo binario v2 con CRC-16 en C# y web. |
| 5 | #8 | Placa de sonido (waveIn, conversión de Windows) y NI-DAQmx (P/Invoke a nicaiu.dll), canales IEPE/velocidad/proximidad/tacómetro, calibración con referencia, controles de calidad de señal. |
| 6 | #9 | Sensor por WiFi/TCP, cliente MQTT propio (publica mediciones y alarmas, recibe indicadores del ESP32), servidor Modbus TCP con mapa de registros y orden del PLC, cliente Modbus TCP/RTU (velocidad del variador). |
| 7 | #10 | MCSA (barras del rotor en dB, excentricidad, deslizamiento, THD, desbalance), ultrasonido en dBµV con audio heterodinado, temperatura (manual, Modbus, MQTT, MLX90614), alarmas en dB y «menos es peor». |

## Estructura
- `src/VibrationAnalysis.Core/` (.NET 8, sin dependencias):
  - `Dsp/` FFT, ventanas, espectro, filtros, `AdvancedSpectrum`, `OrderTracking`, conversión de unidades (A y V no se convierten).
  - `Analysis/` severidad ISO 20816, diagnóstico, `SpectralFeatures` y `FrequencyCatalog`, `Orbit`, `MotorCurrent` (MCSA y ultrasonido).
  - `Balancing/`, `Simulation/RotorSimulator`, `Model/Machine` (incluye dientes de engranaje y polos).
  - `IO/` `Recording`, CSV, WAV (lectura y escritura), `SensorProtocol` (texto v1 y binario v2).
  - `Trending/` modelo, `FeatureExtractor`, alarmas y tendencias, `TrendDatabase` (JSON + JSONL + CSV, escritura atómica).
  - `Acquisition/` canales, `StreamBuffer`, calidad de señal, calibración, `IAcquisitionDevice` y equipo simulado.
  - `Network/` MQTT 3.1.1, Modbus (servidor TCP, cliente TCP/RTU), `PlantGateway` (mapa de registros y JSON).
  - `Report/HtmlReport`.
- `src/VibrationAnalysis.UI/` WinForms: `MainForm.cs` y `MainForm.Network.cs` (partial), `Views.cs`, `TrendDialogs.cs`, `Devices/` (placa de sonido, NI-DAQmx, configuración de canales), `Controls/` (`XYPlotControl`, `TransientCharts`, controles originales).
- `tests/VibrationAnalysis.Core.Tests/` 68 pruebas; `--emit <archivo>` genera los vectores de la web.
- `web/` app web, motor JS y pruebas Node. `firmware/` dos sketches y `README.md` (sensores, conexiones, protocolo, WiFi/MQTT, temperatura, montaje).

## Datos y configuración del usuario (`%LOCALAPPDATA%\AnalisisVibraciones\`)
`Tendencias\` (base por defecto; la carpeta se cambia en la pestaña 6 y queda en `tendencias.ruta`), `placa_sonido.json`, `ni_daqmx.json`, `red.json` (sin contraseñas), temas y registro.

## Verificado
- Simulador: balanceo de punta a punta (6,35 → 0,09 mm/s en 3 corridas, G6,3).
- En el Windows del autor: `probar.ps1` completo y la app abre; análisis avanzado; placa de sonido con el micrófono de la PC; MQTT con Mosquitto (recibe sensores simulados); servidor y cliente Modbus TCP en bucle local.

## Pendientes
- Etiquetar y publicar **`v3.0.0`** (lo hace el autor, comandos arriba).
- Probar con hardware real: firmware multicanal (ADXL355/IIS3DWB), WiFi/MQTT del ESP32, NI-DAQmx, pinza de corriente, sensor de ultrasonido a 192 kHz, MLX90614.
- La app web sigue en el alcance de v2 (análisis básico, balanceo, protocolo v1/v2): las funciones de las fases 1–7 son de la app de escritorio.
- Opcional: Cloudflare Pages (preset None, sin build, directorio de salida `web`).

## Comandos útiles
```bash
git clone https://github.com/hfaalaniz/AnalisisDeVibraciones && cd AnalisisDeVibraciones
dotnet run --project tests/VibrationAnalysis.Core.Tests -- --emit web/tests/vectors.json
node --test web/tests/*.test.mjs        # rutas relativas con '/': Node 21+ las trata como glob
gh run list --repo hfaalaniz/AnalisisDeVibraciones --limit 3
```
Entorno de la sesión: NuGet bloqueado (compilar con `--configfile` de un `nuget.config` sin orígenes y `--no-restore`); SDK de .NET 8 con `apt-get install dotnet-sdk-8.0`. WinForms y el firmware se verifican en el CI.
