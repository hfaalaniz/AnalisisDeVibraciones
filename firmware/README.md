# Sensores USB de vibraciones (ESP32)

Dos firmwares de referencia. Los dos funcionan con la app de Windows (puerto COM) y con la app web (Chrome/Edge, Web Serial).

| Carpeta | Para qué |
|---|---|
| `esp32_vibraciones/` | Versión simple: 1 o 2 ADXL345 + tacómetro, protocolo de texto. |
| `esp32_multicanal/` | Hasta 4 acelerómetros (o triaxiales), ADXL345 / ADXL355 / IIS3DWB, protocolo binario con CRC. |

> Ejemplos de referencia: compilan en el CI (ESP32 y ESP32-S3), pero no fueron probados en hardware. Verificar cableado,
> alimentación (3,3 V) y fijación del acelerómetro antes de usar en planta.

## Qué sensor elegir

| Sensor | Ruido | Ancho de banda útil | Bits | Uso recomendado |
|---|---|---|---|---|
| **ADXL345** | ≈ 290 µg/√Hz | hasta ≈ 1,5 kHz (muestreo 3200 Hz) | 13 | Balanceo y diagnóstico básico, bajo costo. |
| **ADXL355** | **25 µg/√Hz** | hasta 1 kHz (muestreo 4000 Hz) | 20 | Máquinas lentas (< 600 rpm), tendencias de baja amplitud, desbalance fino. |
| **IIS3DWB** | 75 µg/√Hz | **plano hasta 6 kHz** (muestreo 26,7 kHz) | 16 | Rodamientos, engranajes, envolvente y curtosis espectral. |

Con más de un canal a 26,7 kHz se necesita **ESP32-S3** con USB nativo (≈ 1,7 Mbit/s para 4 canales). Con ADXL345/ADXL355 alcanza un ESP32 común.

## Conexiones (`esp32_multicanal`)

| Señal | ESP32 | ESP32-S3 |
|---|---|---|
| SCK / MISO / MOSI | 18 / 19 / 23 | 12 / 13 / 11 |
| CS sensor 1 / 2 / 3 | 5 / 17 / 16 | 10 / 9 / 8 |
| Tacómetro óptico (salida digital) | 4 | 4 |

Configuración en las primeras líneas del sketch: `SENSOR_TYPE`, `CS_PINS`, `TRIAXIAL` (3 ejes por sensor) y `RANGE_G`.
En el IDE de Arduino, para ESP32-S3 activar **USB CDC On Boot**.

## Protocolo

```
PC → sensor:  START | STOP | BIN | TEXT | INFO
```

**Texto (v1)** — cabecera y una línea por muestra (la última columna es el tacómetro: 1 = pulso):
```
#VA fs=3200 ch=2 unit=g tach=1
0.0123,-0.0457,0
0.0131,-0.0442,1
```

**Binario (v2)** — cabecera de texto y tramas binarias (little-endian):
```
#VA2 fs=26667 ch=4 unit=g tach=1 scale=0.000122 names=S1Z,S2Z,S3Z,S4Z sensor=IIS3DWB
A5 5A | seq u16 | n u16 | ch u8 | n×ch int16 (muestra a muestra) | ⌈n/8⌉ bytes de tacómetro (bit i = muestra i) | CRC-16/CCITT u16
```
- Valor físico = int16 × `scale`. El CRC (polinomio 0x1021, inicial 0xFFFF) cubre desde `seq` hasta el tacómetro.
- `seq` permite contar tramas perdidas; la app muestra tramas, errores de CRC y pérdidas mientras mide.
- En modo automático el firmware usa texto si entra en 921600 baudios y binario si no. `TEXT` o `BIN` lo fuerzan.

Cualquier equipo que envíe este formato (otro microcontrolador, un DAQ, un script) funciona con las dos apps.

## Red: WiFi y MQTT (`esp32_multicanal`)

Con `USE_WIFI 1` (y `WIFI_SSID`, `WIFI_PASS`, `DEVICE_ID` al principio del sketch) el sensor atiende el **mismo protocolo por TCP en el puerto 3333**: en la app de Windows elegir *Sensor por red (WiFi/TCP)* en la pestaña 2 con la IP que el ESP32 informa por el USB (`# WiFi conectado: 192.168.1.57, puerto 3333`).

Con `USE_MQTT 1` además publica cada `MQTT_PERIOD_S` segundos, aunque nadie esté conectado, en `MQTT_PREFIX/DEVICE_ID/indicadores`:
```json
{"id":"vt01","sensor":"ADXL355","fs":4000,"acc_rms":0.08123,"acc_pico":0.31,"factor_cresta":3.8,"vel_rms":2.41,"rpm":1490.0,"n":40000}
```
Necesita la biblioteca **PubSubClient** (Administrador de bibliotecas). La app de Windows (pestaña 7) recibe estos mensajes, los muestra y, si se asocia el sensor a un punto, los guarda en la base de tendencias con sus alarmas.

## Temperatura (opcional)

Con `USE_MLX90614 1` se lee un termómetro infrarrojo **MLX90614** por I2C (SDA 21 / SCL 22; en ESP32-S3, 1 / 2) apuntado a la tapa del rodamiento. La temperatura sale en la respuesta a `INFO` y en el JSON de MQTT (`"temperatura": 56.25`), y la app la guarda como indicador con alarma (por defecto 80 °C alerta, 95 °C peligro).

## Corriente del motor y ultrasonido

No necesitan firmware: se conectan a la **placa de sonido o al DAQ** (pestaña 2 → *Configurar equipo…*):
- **Pinza de corriente** con salida de tensión (mV/A) en una o tres fases → análisis MCSA (barras del rotor, excentricidad, THD, desbalance). Registros de 10 s o más.
- **Sensor de ultrasonido** (40 kHz) → placa de sonido a 192 kHz o DAQ rápido: nivel en dBµV y audio heterodinado para escuchar.

## Montaje

- Fijar el acelerómetro con **espárrago o base magnética** sobre la tapa del cojinete. Con cinta o adhesivo blando se pierden las frecuencias altas.
- Dirección radial (horizontal) para balanceo; para rodamientos lo más cerca posible de la zona de carga.
- Dos planos: un sensor en cada cojinete. Órbita: dos sensores a 90° en el mismo cojinete (X horizontal, Y vertical).
- El tacómetro debe ver la cinta reflectiva una sola vez por vuelta (marca de 0°).

## Sincronismo y fase

- ADXL345/ADXL355: el temporizador del ESP32 marca el muestreo y el pulso del tacómetro se asigna a la muestra siguiente (error de fase ≤ 1 muestra: 2,2° a 1500 rpm con 4 kHz).
- IIS3DWB: se lee la FIFO de cada sensor y el pulso se ubica en la muestra correspondiente a su instante (error ≈ 1 muestra a 26,7 kHz).
- Los sensores tienen relojes independientes; para mediciones de fase entre canales de alta precisión conviene un solo IIS3DWB triaxial o el mismo modelo en todos los canales.
