# Análisis de Vibraciones y Balanceo

Análisis de vibraciones mecánicas y **balanceo de campo en 1 y 2 planos** con el método de **coeficientes de influencia**, en dos versiones que comparten la misma matemática:

- **App web** (`web/`): funciona en el navegador, sin instalación. Se publica en Cloudflare Pages.
- **App de escritorio para Windows** (`src/VibrationAnalysis.UI`): WinForms (.NET 8), ejecutable único.

Autor: **Fabian Alaniz – ElectroNet** · +54 9 351 284-8802 · [www.developers-soft.com](https://www.developers-soft.com/)

## Funciones

| Área | Qué hace |
|---|---|
| Adquisición | Simulador de rotor (con fallas configurables), archivos CSV/WAV, sensor USB en vivo (ESP32 con hasta 4 acelerómetros ADXL345 / ADXL355 / IIS3DWB, triaxial opcional, protocolo binario con CRC hasta 26,7 kHz + tacómetro) e ingreso manual de lecturas |
| Análisis | FFT (Hanning, flat-top, Hamming, rectangular), forma de onda, conversión aceleración ↔ velocidad ↔ desplazamiento, RMS, pico, factor de cresta, curtosis, lectura 1X (amplitud ∠ fase) referida al tacómetro |
| Análisis avanzado (escritorio) | Espectro promediado (Welch: lineal, exponencial, retención de picos), PSD, zoom FFT, envolvente/demodulación con banda sugerida por curtosis espectral, cepstrum (familias de armónicos y bandas laterales), filtros Butterworth de fase cero, ventanas Blackman-Harris y Kaiser, rótulo automático de picos (1X…, BPFO/BPFI/BSF/FTF, GMF y bandas laterales, álabes, red) y exportación CSV/PNG |
| Órdenes y transitorios (escritorio) | Remuestreo angular con tacómetro, espectro de órdenes, seguimiento de órdenes en arranques y paradas (Bode y Nyquist), velocidades críticas con factor Q (media potencia, API 684), cascada (waterfall), espectrograma y órbita X-Y filtrada 1X con precesión y parámetros de la elipse |
| Tendencias y alarmas (escritorio) | Base de mediciones en carpeta (planta → máquina → punto → dirección, JSON sin dependencias), 17 indicadores por medición (velocidad RMS, aceleración, curtosis, 1X/2X/3X, subsincrónico, alta frecuencia, envolvente, BPFO/BPFI, GMF…), alarmas absolutas, por múltiplo de la línea base, estadísticas (media + k·σ) e ISO 20816, tendencia lineal o exponencial con pronóstico de la fecha de alarma, rutas de medición y planta de demostración |
| IEPE / DAQ (escritorio) | Acelerómetros IEPE (mV/g), sensores de velocidad (mV/(mm/s)) y sondas de proximidad (mV/µm, gap aparte) por **placa de sonido** (waveIn, 16/24 bits hasta 192 kHz) o **NI-DAQmx** (NI 9234/9230/USB-4431…, excitación IEPE desde la placa), sin dependencias. Canales configurables, calibración con calibrador de mano (1 g a 159,2 Hz) y controles de calidad: saturación, señal plana, polarización IEPE (abierto/corto), tacómetro sin pulsos, «bajada de esquí» |
| Red y planta (escritorio) | Sensor ESP32 por WiFi/TCP con el mismo protocolo; cliente **MQTT** propio (publica cada medición con indicadores y alarmas, recibe los indicadores que calcula el ESP32 y los guarda en la base); **servidor Modbus TCP** con mapa de registros para PLC/SCADA (medición actual, estado de cada punto, orden «adquirir y guardar»); **cliente Modbus TCP/RTU** para leer la velocidad del variador o una variable del PLC |
| Otras magnitudes (escritorio) | **Corriente del motor (MCSA)** con pinza amperométrica: barras del rotor por bandas f·(1 ± 2s) con escala de severidad en dB, excentricidad f ± fr, deslizamiento (dado o estimado), THD y desbalance entre fases; **ultrasonido** 20–80 kHz en dBµV con audio heterodinado para escuchar y criterios por aumento sobre la base; **temperatura** (manual, Modbus, MQTT o MLX90614 en el ESP32). Todo con tendencias y alarmas |
| Severidad | Velocidad RMS 10–1000 Hz según ISO 20816-1 / 10816-1 (clases I–IV) e ISO 20816-3 / 10816-3 (grupos 1 y 2, base rígida o flexible), zonas A–D |
| Diagnóstico | Desbalance, desalineación, holgura mecánica, remolino de aceite, defectos de rodamiento (BPFO, BPFI, BSF, FTF; confirmados por envolvente automática), engranajes (GMF con bandas laterales a ±1X), paso de álabes y problemas eléctricos (2× red) |
| Balanceo | Flujo guiado: original → peso de prueba (plano 1, plano 2) → corrección → verificación y ajuste (trim). Control de calidad de la corrida de prueba, número de condición en 2 planos, opción de dejar los pesos de prueba, ángulos a favor o en contra del giro |
| Herramientas | Peso de prueba sugerido, reparto entre posiciones fijas (álabes), profundidad de perforación, desbalance admisible ISO 1940 (grado G) y desbalance residual estimado |
| Informe | HTML imprimible o PDF con máquina, severidad, diagnóstico, corridas y correcciones. Proyectos `.vaproj` compatibles entre la web y Windows |

## Estructura

```
src/VibrationAnalysis.Core/     Núcleo de cálculo (.NET 8, sin dependencias externas)
src/VibrationAnalysis.UI/       App de escritorio WinForms
tests/VibrationAnalysis.Core.Tests/  Pruebas del núcleo (28) y vectores de referencia para la web
web/                            Página del producto (index.html) y app web (app/)
web/js/core/                    Motor de cálculo web (equivalente al núcleo C#)
web/tests/                      Pruebas del motor web, incluida la paridad con C#
firmware/                       Sensor USB de ejemplo (ESP32 + ADXL345)
.github/workflows/build.yml     Compila en Windows, corre las pruebas y genera el ejecutable
```

## Compilar y probar

```bash
# Pruebas del núcleo (cualquier sistema con .NET 8)
dotnet run --project tests/VibrationAnalysis.Core.Tests

# Vectores de referencia + pruebas de la web (Node 20+)
dotnet run --project tests/VibrationAnalysis.Core.Tests -- --emit web/tests/vectors.json
node --test web/tests/*.test.mjs

# App de Windows (en Windows, o en Visual Studio 2022 abriendo AnalisisDeVibraciones.sln)
dotnet run --project src/VibrationAnalysis.UI

# Ejecutable único para distribuir
dotnet publish src/VibrationAnalysis.UI -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Probar una rama en Windows (`probar.ps1`)

Trae la rama desde GitHub, corre las pruebas (C# y web) y abre la app. Una sola vez: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`.

```powershell
.\probar.ps1 -Listar                       # ramas disponibles
.\probar.ps1 -Rama fix/xxx                 # actualiza, prueba y abre la app de escritorio
.\probar.ps1 -Rama fix/xxx -Ejecutar web   # web en http://localhost:8000/app/
.\probar.ps1                               # repite en la rama actual (con pull)
.\probar.ps1 -Volver                       # vuelve a main y borra ramas ya mezcladas
```

Opciones: `-Ejecutar todo|pruebas|escritorio|web|nada`, `-Guardar` (aparta cambios locales con `git stash`), `-Reiniciar` (iguala la rama local a GitHub), `-Puerto`.

Cada push compila la app en Windows y corre todas las pruebas (GitHub Actions). Al crear una etiqueta `v*` (por ejemplo `v2.0.0`) se publica el ejecutable en **Releases**.

## Publicar la web en Cloudflare Pages

1. En Cloudflare → Workers & Pages → Crear → Pages → conectar este repositorio.
2. Configuración de compilación: *Framework preset* **None**, *Build command* vacío, *Build output directory* **`web`**.
3. La página del producto queda en la raíz y la aplicación en `/app/`.

## Cómo se balancea

1. **Original**: medir la vibración 1X (amplitud y fase con tacómetro) sin pesos.
2. **Prueba**: colocar un peso conocido T y medir R. El coeficiente de influencia es α = (R − O) / T.
3. **Corrección**: W = −O / α (en dos planos se resuelve el sistema [α]·W = −O con una corrida de prueba por plano).
4. **Verificación**: si la vibración supera la tolerancia, se calcula un ajuste con los mismos coeficientes.

Las posiciones angulares de los pesos se miden desde la marca de referencia con la misma convención en todas las corridas (por defecto, en sentido contrario al giro, que corresponde a fase en retraso).

## Cambios de la versión 2.0

- El cálculo de balanceo anterior usaba coeficientes inventados (`k = factor / radio`, acople `0,5 / distancia`), por lo que las correcciones no dependían de la máquina real. Ahora se usa el método de coeficientes de influencia con corridas de prueba.
- La fase se calculaba sobre un arreglo vacío y luego sobre una senoidal sin referencia, por lo que daba siempre ~270°. Ahora se obtiene por ajuste de mínimos cuadrados referido al pulso del tacómetro.
- "Aplicar corrección" ponía la vibración en un valor fijo (0,02). Ahora el simulador responde físicamente a los pesos y la verificación mide la vibración real.
- Los temas no se podían volver a cargar (System.Text.Json no serializa `Color`) y se guardaban en la carpeta de instalación; ahora se guardan en `%LOCALAPPDATA%`.
- El registro de depuración escribía en disco en cada repintado de los gráficos; ahora está apagado por defecto (`VA_DEBUG=1` para activarlo) y el log rota a los 5 MB.
- Se eliminó la dependencia de MathNet.Numerics (FFT propia con pruebas) y las referencias del proyecto a formularios inexistentes.

## Licencia

Ver [LICENSE](LICENSE).
