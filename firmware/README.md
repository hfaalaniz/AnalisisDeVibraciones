# Sensor USB de vibraciones (ESP32)

Firmware de ejemplo para medir en vivo con la app web (Chrome/Edge, Web Serial) o la app de Windows (puerto COM).

## Materiales

| Componente | Detalle |
|---|---|
| ESP32 DevKit | Núcleo Arduino-ESP32 3.x |
| ADXL345 ×1 o ×2 | SPI: SCK 18, MISO 19, MOSI 23, CS1 5, CS2 17 |
| Sensor óptico reflectivo | Salida digital a GPIO 4; cinta reflectiva en el eje (marca de 0°) |

## Protocolo

```
PC → sensor:  START   |  STOP
sensor → PC:  #VA fs=3200 ch=2 unit=g tach=1
              0.0123,-0.0457,0
              0.0131,-0.0442,1      ← 1 = pulso del tacómetro en esa muestra
```

- `fs`: frecuencia de muestreo en Hz. `ch`: canales de vibración. `unit`: `g`, `m/s2`, `mm/s` o `um`. `tach=1`: la última columna es el tacómetro.
- 921600 baudios, 8N1. Cualquier equipo que envíe este formato funciona con las dos apps.

## Montaje

- Fijar el acelerómetro con espárrago o base magnética en la tapa del cojinete, en dirección radial (horizontal).
- Para dos planos, un sensor en cada cojinete.
- El tacómetro debe ver la cinta una sola vez por vuelta.

> Ejemplo de referencia: no fue probado en hardware dentro de este repositorio. Verificar cableado y niveles antes de usar en planta.
