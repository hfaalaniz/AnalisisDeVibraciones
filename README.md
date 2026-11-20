# Análisis de Vibraciones y Balanceo

Análisis de vibraciones mecánicas y **balanceo de campo en 1 y 2 planos** con el método de **coeficientes de influencia**, en dos versiones que comparten la misma matemática:

- **App web** (`web/`): funciona en el navegador, sin instalación. Se publica en Cloudflare Pages.
- **App de escritorio para Windows** (`src/VibrationAnalysis.UI`): WinForms (.NET 8), ejecutable único.

Autor: **Fabian Alaniz – ElectroNet** · +54 9 351 284-8802 · [www.developers-soft.com](https://www.developers-soft.com/)

## Funciones

| Área | Qué hace |
|---|---|
| Adquisición | Simulador de rotor (con fallas configurables), archivos CSV/WAV, sensor USB en vivo (ESP32 + ADXL345 + tacómetro) e ingreso manual de lecturas |
| Análisis | FFT (Hanning, flat-top, Hamming, rectangular), forma de onda, conversión aceleración ↔ velocidad ↔ desplazamiento, RMS, pico, factor de cresta, curtosis, lectura 1X (amplitud ∠ fase) referida al tacómetro |
| Severidad | Velocidad RMS 10–1000 Hz según ISO 10816-1 (clases I–IV) e ISO 10816-3 (grupos 1 y 2, base rígida o flexible), zonas A–D |
| Diagnóstico | Desbalance, desalineación, holgura mecánica, remolino de aceite, defectos de rodamiento (BPFO, BPFI, BSF), paso de álabes y problemas eléctricos (2× red) |
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
