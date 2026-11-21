# Manual de usuario — Análisis de Vibraciones y Balanceo

Versión 3.1 · Fabian Alaniz · ElectroNet · [www.developers-soft.com](https://www.developers-soft.com/)

Este manual cubre la **app de escritorio para Windows** (completa) y la **versión web** (en el navegador, sin instalar). Al final hay una tabla con lo que tiene cada una.

> Los diagnósticos automáticos son orientativos. Confirmalos midiendo en horizontal, vertical y axial, con la fase, y con el criterio de personal calificado.

---

## Índice

1. [Instalación](#1-instalación)
2. [Primeros pasos (5 minutos)](#2-primeros-pasos-5-minutos)
3. [Pestaña 1 · Máquina](#3-pestaña-1--máquina)
4. [Pestaña 2 · Adquisición](#4-pestaña-2--adquisición)
5. [Pestaña 3 · Diagnóstico](#5-pestaña-3--diagnóstico)
6. [Pestaña 4 · Análisis avanzado](#6-pestaña-4--análisis-avanzado)
7. [Pestaña 5 · Órdenes y transitorios](#7-pestaña-5--órdenes-y-transitorios)
8. [Pestaña 6 · Tendencias y alarmas](#8-pestaña-6--tendencias-y-alarmas)
9. [Pestaña 7 · Red y planta (MQTT y Modbus)](#9-pestaña-7--red-y-planta-mqtt-y-modbus)
10. [Pestaña 8 · Balanceo de campo](#10-pestaña-8--balanceo-de-campo)
11. [Pestaña 9 · Informe](#11-pestaña-9--informe)
12. [Sensores y equipos de adquisición](#12-sensores-y-equipos-de-adquisición)
13. [Versión web](#13-versión-web)
14. [Archivos y formatos](#14-archivos-y-formatos)
15. [Solución de problemas](#15-solución-de-problemas)
16. [Glosario](#16-glosario)

---

## 1. Instalación

**Requisitos:** Windows 10 u 11 de 64 bits. No hace falta instalar .NET: viene incluido.

**Con el instalador (recomendado)**

1. Descargá `AnalisisVibraciones-<versión>-instalador.exe` desde [Releases](https://github.com/hfaalaniz/AnalisisDeVibraciones/releases).
2. Ejecutalo y seguí los pasos. Opciones:
   - acceso directo en el escritorio;
   - asociar los archivos `.vaproj` (doble clic abre el proyecto);
   - abrir el puerto en el firewall (solo si vas a usar el servidor Modbus TCP o sensores por red).
3. Se instala en `C:\Program Files\Analisis de Vibraciones`. Se desinstala desde *Configuración → Aplicaciones*.

**Portable:** descomprimí `AnalisisVibraciones-win-x64.zip` en cualquier carpeta y ejecutá `AnalisisVibraciones.exe`.

**Datos del usuario:** configuración del informe, logos y registros (logs) se guardan en `%LOCALAPPDATA%\AnalisisVibraciones`. La base de tendencias va en la carpeta que elijas (pestaña 6).

---

## 2. Primeros pasos (5 minutos)

Sin sensor, con el **simulador**:

1. **Pestaña 1 · Máquina:** cargá el nombre del equipo, la velocidad (rpm) y el criterio de severidad.
2. **Pestaña 2 · Adquisición:** dejá *Fuente = Simulador* y tocá **Adquirir**. Vas a ver la forma de onda y el espectro.
3. **Pestaña 3 · Diagnóstico:** muestra la severidad ISO y las fallas probables.
4. **Pestaña 8 · Balanceo:** tocá **Nueva sesión** y seguí los pasos. El simulador coloca solo los pesos de prueba y la corrección.
5. **Pestaña 9 · Informe:** **Vista previa** y **Guardar PDF…**.

Para practicar diagnóstico, en el panel *Rotor virtual* agregá fallas (desalineación, holgura, rodamiento, engranaje…) y volvé a adquirir.

---

## 3. Pestaña 1 · Máquina

Los datos de la máquina definen los límites de severidad, las frecuencias de falla y los cálculos de balanceo.

| Grupo | Campo | Para qué se usa |
|---|---|---|
| Identificación | Equipo, Ubicación | Encabezado del informe y tendencias. |
| Operación | Velocidad (rpm) | 1X cuando no hay tacómetro. Con tacómetro se usa la medida. |
| | Criterio de severidad | Clase ISO 10816-1 (I–IV) o grupo ISO 10816-3. Define las zonas A/B/C/D. |
| | Frecuencia de red | 50 Hz (Argentina) o 60 Hz. Detecta problemas eléctricos (2× red) y MCSA. |
| Rotor | Tipo, material, diámetros, largo, masa | Masa del rotor (si ponés 0, se estima con la geometría). |
| | Álabes / posiciones fijas | Reparte la corrección entre dos posiciones fijas. 0 = cualquier ángulo. |
| | Polos del motor | Análisis de corriente (MCSA): velocidad de sincronismo y deslizamiento. |
| | Dientes del engranaje | Frecuencia de engrane (GMF) y sus bandas laterales. |
| Balanceo | Radio de corrección | Radio donde se colocan o perforan los pesos. |
| | Grado ISO 1940 (G) | G6,3 ventiladores, bombas y motores; G2,5 turbinas y máquinas herramienta. |
| Rodamiento (opcional) | Elementos, diámetro de bola, primitivo, ángulo | Frecuencias BPFO, BPFI, BSF y FTF. |

El panel de la derecha resume:
- la masa usada;
- los planos recomendados (1 o 2, según la relación largo/diámetro y la velocidad);
- el desbalance residual admisible (g·mm y gramos al radio de corrección);
- el peso de prueba sugerido;
- las frecuencias de rodamiento.

---

## 4. Pestaña 2 · Adquisición

**Fuente de datos**

| Fuente | Uso |
|---|---|
| Simulador | Rotor virtual con desbalance en dos planos, tacómetro y fallas configurables. Incluye arranque/parada con velocidad crítica. |
| Archivo CSV / WAV | Señales de otro colector o grabadas antes (ver [formatos](#14-archivos-y-formatos)). |
| Puerto serie (sensor) | Sensor USB con ESP32 (firmware incluido). Modo Auto, Texto o Binario. |
| Ingreso manual | Solo para balancear con lecturas de amplitud y fase de otro equipo. |
| Placa de sonido (IEPE) | Entrada de línea o micrófono con un acondicionador IEPE. |
| NI-DAQmx (IEPE) | Placas National Instruments (NI 9234, 9230, USB-4431…). Requiere el driver NI-DAQmx. |
| Sensor por red (WiFi/TCP) | ESP32 con `USE_WIFI=1`: IP y puerto (por defecto `192.168.4.1:3333`). |

**Controles**
- **Adquirir / Capturar:** toma una medición.
- **En vivo (1 s):** repite la medición cada segundo.
- **Conectar:** abre el puerto, la placa o el sensor por red.
- **Capturar (s):** duración del registro del sensor.
- **Configurar equipo…:** canales, tipo de sensor, sensibilidad, corriente IEPE y rango (placa de sonido y NI-DAQmx). Desde ahí también se calibra un canal.
- **Canal / Ver / Ventana:** canal mostrado, unidad (velocidad, aceleración o desplazamiento) y ventana de la FFT.
- **Exportar CSV:** guarda la señal.

La línea de estado muestra:
- con el sensor serie o de red: tramas recibidas, errores de CRC y tramas perdidas;
- con placa de sonido o DAQ: saturación, nivel bajo o falta de corriente IEPE.

**Gráficos:** rueda = zoom, arrastrar = desplazar, doble clic = ver todo.

---

## 5. Pestaña 3 · Diagnóstico

- **Severidad:** velocidad RMS 10–1000 Hz y zona ISO (A buena, B satisfactoria, C insatisfactoria, D inaceptable), con la escala de colores.
- **Fallas probables** con su confianza (%), la evidencia y la recomendación:
  - desbalance, desalineación, holgura mecánica, eje doblado;
  - rodamiento (BPFO/BPFI/BSF/FTF), remolino de aceite;
  - problema eléctrico (2× red), paso de álabes, engranaje (GMF y bandas laterales).
- **Picos principales** identificados (1X, 2X, BPFO…).

Para un diagnóstico firme, medí en las tres direcciones en cada apoyo y compará fases.

---

## 6. Pestaña 4 · Análisis avanzado

| Análisis | Qué muestra | Cuándo usarlo |
|---|---|---|
| Espectro promediado | Promedio lineal, exponencial o retención de picos por bloques (Welch). | Reduce el ruido; la retención de picos captura eventos intermitentes. |
| Densidad espectral (PSD) | unidades²/Hz, independiente de la resolución. | Vibración aleatoria, comparar equipos o normas. |
| Zoom FFT | Alta resolución en una banda (centro ± ancho). | Separar 1X de 2× red, bandas laterales cercanas, deslizamiento. |
| Envolvente (demodulación) | Espectro de la envolvente en una banda de alta frecuencia. | Rodamientos y engranajes en etapa temprana. |
| Cepstrum | Familias de armónicos y bandas laterales como un solo pico (quefrencia). | Engranajes, rodamientos con muchas bandas laterales. |
| Curtosis espectral | Qué bandas contienen impactos. | Elegir la banda de la envolvente (**Sugerir banda**). |
| Corriente del motor (MCSA) | Espectro de corriente, deslizamiento, barras rotas, excentricidad, THD. | Motores de inducción con pinza amperométrica. |
| Ultrasonido | Nivel en dBµV, factor de cresta, audio heterodino (**Escuchar**, **Guardar WAV**). | Lubricación, fugas, descargas eléctricas. Necesita 96 kHz o más. |

Parámetros comunes:
- bloque y solape (%);
- ventana: Hanning, Flat-top, Hamming, Blackman-Harris, Kaiser o Rectangular;
- **Hasta (Hz)**: frecuencia máxima a mostrar;
- **Escala dB**;
- **Frecuencias de la máquina**: dibuja 1X, 2X, BPFO… sobre el gráfico.

Los resultados se exportan a CSV o PNG.

Consejos:
- **Envolvente:** usá aceleración. La banda debe contener una resonancia excitada por los impactos (típicamente 1–5 kHz). Un pico en BPFO o BPFI con armónicos indica defecto en pista.
- **Zoom:** **Centrar en 1X** centra la banda en la velocidad medida.
- **MCSA:** con **Simular corriente del motor** practicás con un motor con barras rotas.

---

## 7. Pestaña 5 · Órdenes y transitorios

Todos (menos el espectrograma y la órbita) necesitan **tacómetro**.

| Análisis | Uso |
|---|---|
| Espectro de órdenes | Eje X en órdenes (1X, 2X…) con remuestreo angular: no se "embarra" si la velocidad varía. |
| Bode | Amplitud y fase de un orden contra rpm durante un arranque o parada. Detecta **velocidades críticas** (pico de amplitud y giro de fase ≈ 90°) y su factor Q. |
| Nyquist (polar) | La misma información en el plano complejo: una resonancia es un lazo. |
| Cascada (waterfall) | Espectros sucesivos apilados. |
| Espectrograma | Mapa de color tiempo/rpm vs frecuencia: las órdenes son líneas inclinadas y las resonancias, horizontales. |
| Órbita (X-Y) | Dos sensores a 90° en el mismo plano (opcionalmente filtrado en 1X). |

Para practicar: en el simulador, *Arranque / parada → Simular = Sí*, con 65 536 muestras o más.

Si la orden es fraccionaria (0,5X), las vueltas por bloque deben hacer que el producto sea entero (2, 4, 6…).

---

## 8. Pestaña 6 · Tendencias y alarmas

Organiza las mediciones en **Planta → Máquina → Punto** y guarda indicadores en cada medición.

**Configurar**
1. **Carpeta de la base…** (local o en red). Se guarda en JSON.
2. **+ Planta**, **+ Máquina**, **+ Punto** (nombre y dirección: horizontal, vertical, axial).
3. **Máquina ← pestaña 1** copia los datos de la máquina actual al árbol. **Máquina → pestaña 1** hace lo inverso.

**Medir**
1. Elegí el punto, adquirí en la pestaña 2 y tocá **Guardar medición actual**.
2. Opcional: temperatura (°C) y **Guardar también la señal**. Si guardaste la señal, doble clic en la medición la vuelve a abrir.
3. Se calculan:
   - velocidad RMS, aceleración RMS y pico, desplazamiento pico a pico;
   - factor de cresta, curtosis;
   - 1X (con fase), 2X, 3X, subsincrónico;
   - alta frecuencia, envolvente RMS, BPFO/BPFI;
   - GMF, temperatura y los indicadores de corriente o ultrasonido.

**Rutas:** **Nueva ruta…** define un recorrido de puntos; ◀ ▶ avanza al punto anterior o siguiente.

**Alarmas…** (por punto). Cada indicador tiene un tipo de límite:

| Tipo | Alerta / Peligro |
|---|---|
| Absoluto | Valores fijos en las unidades del indicador. |
| ISO | Límites B/C y C/D de la clase de la máquina. |
| Múltiplo de la base | Ej. 2 y 4 = el doble y el cuádruple del promedio de la línea base. |
| Estadístico | Media + n·σ de la línea base. |
| Base + incremento | Ej. +8 y +16 dB (ultrasonido). |

En los indicadores de MCSA en dB, **menos es peor** (alerta 42, peligro 36).

**Línea base:** **Marcar línea base** con las mediciones elegidas; si no hay, se usan las 3 primeras.

**Gráfico:** indicador elegido, límites, ajuste lineal o exponencial y **pronóstico** de la fecha en que cruzaría alerta o peligro.

**Demo (12 meses)** carga una historia de ejemplo con un rodamiento que se degrada.

---

## 9. Pestaña 7 · Red y planta (MQTT y Modbus)

**MQTT** (SCADA, Node-RED, plataformas IIoT)
- Broker, usuario, clave y prefijo.
- **Publicar cada medición guardada:** se publica en `<prefijo>/<planta>/<máquina>/<punto>/medicion` (JSON, retenido).
- **Recibir indicadores de sensores:** escucha `<prefijo>/+/indicadores`, lo que envía el firmware con `USE_MQTT=1`.
- **Asociar sensor → punto de Tendencias:** guarda automáticamente lo que llega de ese sensor.

**Servidor Modbus TCP** (el PLC lee los resultados)
- **Iniciar** en el puerto elegido (502 por defecto). **Exportar mapa CSV** genera la tabla completa.
- Mapa de registros de entrada (función 04, float32 palabra alta primero):

| Registro | Contenido |
|---|---|
| 30001… | Medición actual: velocidad RMS, aceleración RMS y pico, factor de cresta, curtosis, 1X, fase 1X, 2X, envolvente. |
| 30019 | Velocidad de giro (rpm). |
| 30021 / 30022 | Zona ISO (0 = A … 3 = D) / nivel de alarma (0, 1, 2). |
| 30023 / 30025 / 30026 | Fecha (Unix) / contador de mediciones / latido. |
| 30101 + 20·i | Punto *i* de la base: velocidad, aceleración, envolvente, 1X, nivel de alarma, cantidad y fecha. |
| 40001 (escritura) | 1 = adquirir y guardar en el punto elegido (la app lo vuelve a 0). |

**Cliente Modbus TCP / RTU** (leer de un PLC o variador)
- Dirección `IP:puerto` o `COMx`, baudios, unidad, función 03/04, registro, tipo (uint16, int16, float32 ABCD/CDAB) y escala.
- **Usar como velocidad de giro:** reemplaza las rpm nominales cuando no hay tacómetro.
- **Usar como temperatura:** se guarda con cada medición.
- **Leer cada 2 s.**

---

## 10. Pestaña 8 · Balanceo de campo

**Antes de empezar:** confirmá que el problema es desbalance (1X dominante, fase estable). Revisá que no haya holgura, desalineación ni rozamiento.

1. Elegí **1 plano** o **2 planos**, la tolerancia de vibración 1X y el sentido de los ángulos (contrarios o a favor del giro).
2. **Nueva sesión.**
3. **Medición original:** **Medir y registrar** (con la adquisición: 1X y fase con tacómetro), o ingresá amplitud y fase a mano.
4. **Peso de prueba 1:**
   - colocá la masa sugerida (≈ 10 % de la fuerza del peso del rotor) en el ángulo indicado;
   - cargá masa y ángulo y medí.
   - Si el peso cambia la vibración menos del 30 % en amplitud y menos de 30° en fase, la app avisa: aumentalo o cambiá su posición.
5. **Peso de prueba 2** (solo 2 planos): sacá el peso 1 (salvo que marques *Dejar los pesos de prueba*) y repetí en el plano 2.
6. **Corrección:** la app indica gramos y ángulo por plano.
   - **Posiciones fijas** (álabes): reparto entre dos posiciones.
   - **Perforación:** profundidad para quitar material.
   - **Vibración residual prevista** y **condición de la matriz** (2 planos; > 10 = planos poco independientes).
7. **Verificación:** medí de nuevo.
   - Si no alcanza la tolerancia, la app calcula un **ajuste (trim)** con los coeficientes ya conocidos.
   - También muestra el desbalance residual frente al admisible ISO 1940.

Con el simulador y *Colocar los pesos en el simulador*, los pesos se aplican solos para practicar.

El diagrama polar muestra cada lectura (flechas) y la corrección (círculo rojo).

---

## 11. Pestaña 9 · Informe

**Datos**
- técnico, empresa, cliente;
- N.º de informe: se numera solo por año, `VA-2026-001`, `VA-2026-002`…;
- orden de trabajo y logo (PNG/JPG);
- **Recomendaciones:** una por línea. Si queda vacío, se completan solas con el diagnóstico. **Sugerir desde el diagnóstico** las carga para editarlas.
- **Observaciones.**

Técnico, empresa, logo y numeración se recuerdan entre sesiones.

**Secciones** (se pueden desactivar):
- señal y espectro de la medición actual;
- gráfico actual del análisis avanzado (pestaña 4) y de órdenes (pestaña 5);
- indicadores y alarmas, tendencias del punto elegido;
- corriente del motor (MCSA), balanceo y firmas.

Siempre se incluyen:
- encabezado;
- **resumen** con el estado general (Aceptable / Alerta / Peligro, el peor entre severidad, alarmas, diagnóstico y MCSA) y recomendaciones;
- máquina, severidad y diagnóstico.

**Salidas**
- **Vista previa:** se abre en el navegador.
- **Guardar PDF…:** usa Microsoft Edge o Chrome en segundo plano. A4, con gráficos vectoriales.
- **Guardar HTML…:** un solo archivo con todo incluido.
- **Guardar proyecto…** (`.vaproj`) y **Exportar espectro (PNG)…**

También desde el menú **Archivo → Guardar informe PDF…** y **Vista previa del informe**.

---

## 12. Sensores y equipos de adquisición

**Tipos de sensor** (en *Configurar equipo…*)

| Tipo | Sensibilidad | Notas |
|---|---|---|
| Acelerómetro IEPE/ICP | mV/g (típico 100) | Corriente constante 2–20 mA. |
| Acelerómetro de tensión (MEMS) | mV/g | Sin corriente IEPE. |
| Sensor de velocidad | mV/(mm/s) (típico 4) | Sísmico o IEPE de velocidad. |
| Sonda de proximidad | mV/µm (típico 7,87) | Necesita entrada con acople DC. |
| Tacómetro / keyphasor | — | Pulso por vuelta: referencia de fase. |
| Pinza de corriente | mV/A | Para MCSA. |
| Ultrasonido | mV/V | Muestrear a 96 kHz o más. |

**Placa de sonido:**
- la entrada 0 es el canal izquierdo y la 1, el derecho;
- los acelerómetros IEPE necesitan un acondicionador (fuente de corriente);
- no mide por debajo de ≈ 10 Hz (acople AC);
- el fondo de escala es la tensión pico que satura la entrada: calibralo.

**NI-DAQmx:**
- excita los IEPE desde la placa;
- la sensibilidad está en el certificado del sensor.

**Sensor USB/WiFi con ESP32:** ver [`firmware/README.md`](../firmware/README.md). Hay dos firmwares:
- simple: ADXL345 + tacómetro, protocolo de texto;
- multicanal: hasta 4 sensores ADXL345 / ADXL355 / IIS3DWB, protocolo binario con CRC, WiFi y MQTT. También el MLX90614 para temperatura.

> Los firmwares compilan en el CI pero deben validarse con el hardware real (cableado, 3,3 V, fijación del sensor).

**Montaje:** espárrago o imán sobre superficie plana y limpia, cerca del rodamiento, en la zona de carga. El tacómetro óptico apunta a una cinta reflectante en el eje.

---

## 13. Versión web

En [analisisdevibraciones.pages.dev](https://analisisdevibraciones.pages.dev/) → *Abrir la app*. Funciona en Chrome, Edge, Firefox y Safari, también en el teléfono. El sensor USB requiere Chrome o Edge en una computadora (Web Serial).

| Pestaña | Contenido |
|---|---|
| Análisis | Fuente (simulador, CSV/WAV, sensor USB, manual), lectura de severidad, espectro, forma de onda, diagnóstico. |
| Avanzado | Espectro promediado, PSD, Zoom FFT, envolvente (con **Sugerir banda**), cepstrum, curtosis espectral. Exporta CSV. |
| Órdenes | Espectro de órdenes, Bode, Nyquist, espectrograma, órbita. **Simular arranque** genera una rampa de 600 a 3000 rpm con crítica en 1800 rpm. |
| Tendencias | Puntos, **Guardar medición actual**, alarmas con línea base, gráfico con límites y pronóstico, demo, exportar/importar la base (JSON) y CSV. |
| Balanceo | Igual que en el escritorio (1 y 2 planos, trim, posiciones fijas, perforación). |
| Máquina | Datos de la máquina y resumen técnico. |
| Informe | Mismo formato que el escritorio: logo, cliente, N.º, OT, recomendaciones, secciones, firmas. **Imprimir / PDF** y **Descargar HTML**. |

Los datos quedan **en el navegador** (almacenamiento local). Exportá el proyecto (`.vaproj`, compatible con el escritorio) y la base de tendencias para no perderlos o llevarlos a otra computadora.

---

## 14. Archivos y formatos

**Proyecto `.vaproj`** (JSON): máquina, sesión de balanceo y datos del informe. Lo abren las dos apps.

**CSV de señal:**
```
# fs=5120 unit=mm/s rpm=1500
t,Sensor 1,Sensor 2,tach
0.000000,0.123,-0.045,0
...
```
- Unidades: `g`, `m/s2`, `mm/s`, `um`, `A`.
- La columna `tach` es opcional: 1 = pulso, o la señal analógica del tacómetro.

**WAV:** al abrirlo se pide el valor físico para ±1,0 (según sensibilidad y ganancia), la unidad y el canal del tacómetro.

**Base de tendencias:** `base.json` + `mediciones/<punto>.jsonl` (y señales opcionales) en la carpeta elegida. En la web, un único JSON exportable.

**Protocolo del sensor (`#VA` texto, `#VA2` binario con CRC-16/CCITT):** ver [`firmware/README.md`](../firmware/README.md).

---

## 15. Solución de problemas

| Síntoma | Causa probable / solución |
|---|---|
| «Error externo» o sin señal en la placa de sonido | Probá 48000 Hz, 16 bits y un solo canal. Revisá que Windows permita el acceso al micrófono (*Privacidad → Micrófono*). |
| Saturación (aviso rojo) | Bajá la ganancia o subí el rango del canal. |
| «Falta corriente IEPE» | El acondicionador está apagado o el sensor está desconectado. |
| Sensor USB sin datos | Puerto y baudios correctos (921600). Probá *Modo = Texto*. **Info** consulta al firmware. En ESP32-S3, activá *USB CDC On Boot*. |
| Errores de CRC o tramas perdidas | Cable USB corto o de mejor calidad. Bajá la cantidad de canales o la frecuencia de muestreo. |
| No llegan mensajes MQTT | Broker y puerto correctos (1883). Revisá el prefijo. Probá con `mosquitto_sub -t "#" -v`. |
| El PLC no lee Modbus | Firewall (opción del instalador) y puerto. Los registros son de entrada (función 04), float32 palabra alta primero. |
| «Guardar PDF» falla | Necesita Microsoft Edge o Google Chrome instalados. Alternativa: **Guardar HTML** e imprimir desde el navegador. |
| Bode u órdenes sin resultado | Hace falta tacómetro con al menos 3 pulsos y un registro largo (arranque o parada). |
| Logs | Menú *Ayuda → Carpeta de registros (logs)*. |

---

## 16. Glosario

- **1X, 2X…:** múltiplos de la velocidad de giro.
- **BPFO / BPFI / BSF / FTF:** frecuencias de defecto en pista externa, pista interna, elemento rodante y jaula.
- **Envolvente:** demodulación que revela la cadencia de impactos repetitivos.
- **Cepstrum / quefrencia:** "espectro del espectro"; detecta familias de armónicos equiespaciados.
- **Curtosis:** mide la impulsividad de la señal (≈ 3 para ruido normal; más alta con impactos).
- **Factor de cresta:** pico / RMS.
- **PSD:** densidad espectral de potencia.
- **Velocidad crítica:** velocidad que coincide con una frecuencia natural del rotor.
- **ISO 10816 / 20816:** severidad de vibración por velocidad RMS.
- **ISO 1940 (G):** calidad de balanceo; define el desbalance residual admisible.
- **MCSA:** análisis de la firma de corriente del motor.

---

## Qué tiene cada versión

| Función | Escritorio | Web |
|---|:---:|:---:|
| Simulador (incluye arranque y velocidad crítica) | ✓ | ✓ |
| CSV / WAV | ✓ | ✓ |
| Sensor USB ESP32 (texto y binario con CRC) | ✓ | ✓ (Chrome/Edge) |
| Sensor por WiFi/TCP | ✓ | — |
| Placa de sonido IEPE, NI-DAQmx | ✓ | — |
| Severidad ISO y diagnóstico | ✓ | ✓ |
| Espectro promediado, PSD, zoom, envolvente, cepstrum, curtosis espectral | ✓ | ✓ |
| MCSA y ultrasonido | ✓ | — |
| Órdenes, Bode, Nyquist, espectrograma, órbita | ✓ | ✓ |
| Cascada (waterfall) | ✓ | — |
| Tendencias, alarmas, línea base y pronóstico | ✓ (planta/máquina/punto, rutas) | ✓ (puntos) |
| MQTT, servidor y cliente Modbus | ✓ | — |
| Balanceo 1 y 2 planos, trim | ✓ | ✓ |
| Informe profesional y PDF | ✓ | ✓ (imprimir a PDF) |

Las funciones de red, DAQ y placa de sonido dependen de acceso al hardware y a la red local que el navegador no permite.
