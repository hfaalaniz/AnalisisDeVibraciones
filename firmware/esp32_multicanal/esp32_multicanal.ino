/*
  Sensor de vibraciones multicanal USB — "Análisis de Vibraciones y Balanceo"
  Autor: Fabian Alaniz – ElectroNet · www.developers-soft.com

  Hasta 4 acelerómetros por SPI + tacómetro óptico. Tres modelos (elegir con SENSOR_TYPE):
    · ADXL345  — económico, 13 bits, hasta 3200 Hz (ruido ≈ 290 µg/√Hz). Uso general y balanceo.
    · ADXL355  — bajo ruido (25 µg/√Hz), 20 bits, 4000 Hz. Baja frecuencia, máquinas lentas, tendencias.
    · IIS3DWB  — específico de vibraciones, 16 bits, 26,7 kHz, plano hasta 6 kHz. Rodamientos y engranajes.
  Cada sensor puede enviar un eje (Z, perpendicular a la base) o los tres (triaxial).

  Protocolo (USB serie; ver firmware/README.md):
    PC → sensor:  START | STOP | BIN | TEXT | INFO
    Texto  (v1): "#VA fs=3200 ch=2 unit=g tach=1"  y una línea por muestra "a1,a2,tach".
    Binario(v2): "#VA2 fs=26667 ch=4 unit=g tach=1 scale=… names=… sensor=…" y tramas
                 A5 5A | seq u16 | n u16 | ch u8 | n·ch × int16 | ⌈n/8⌉ bytes tacómetro | CRC-16/CCITT (LE).
  Por defecto elige texto si entra en 921600 baudios y binario si no (la app de Windows entiende ambos; la web, texto).

  Placa: ESP32 (ADXL345/ADXL355) o ESP32-S3 con USB nativo ("USB CDC On Boot" activado) para IIS3DWB a 26,7 kHz.
  Núcleo Arduino-ESP32 3.x.

  Red (opcional, USE_WIFI=1): el mismo protocolo por TCP (puerto 3333) en vez de USB, y con USE_MQTT=1 publica cada
  MQTT_PERIOD_S segundos los indicadores (aceleración RMS y pico, factor de cresta, velocidad RMS 10–1000 Hz, rpm) en
  «<MQTT_PREFIX>/<DEVICE_ID>/indicadores» como JSON, aunque nadie esté conectado (monitoreo en línea). MQTT usa PubSubClient.

  Ejemplo de referencia: compila en el CI, pero no fue probado en hardware. Verificar cableado, alimentación (3,3 V)
  y fijación del acelerómetro (espárrago o base magnética) antes de usar en planta.
*/
#include <SPI.h>

#ifndef USE_WIFI
#define USE_WIFI 0
#endif
#ifndef USE_MQTT
#define USE_MQTT 0
#endif
#if USE_MQTT && !USE_WIFI
#error "USE_MQTT necesita USE_WIFI=1"
#endif
#if USE_WIFI
#include <WiFi.h>
#endif
#if USE_MQTT
#include <PubSubClient.h>
#endif
#ifndef USE_MLX90614
#define USE_MLX90614 0                            // 1 = termómetro infrarrojo MLX90614 por I2C (temperatura de la carcasa)
#endif
#if USE_MLX90614
#include <Wire.h>
#if CONFIG_IDF_TARGET_ESP32S3
static const int PIN_SDA = 1, PIN_SCL = 2;
#else
static const int PIN_SDA = 21, PIN_SCL = 22;
#endif
/* Temperatura del objeto (°C) del MLX90614 (registro 0x07, 0,02 K por cuenta). NAN si no responde. */
static float readTemperatureC() {
  Wire.beginTransmission(0x5A);
  Wire.write(0x07);
  if (Wire.endTransmission(false) != 0) return NAN;
  if (Wire.requestFrom((uint16_t)0x5A, (size_t)3, true) != 3) return NAN;
  uint16_t lo = Wire.read();
  uint16_t hi = Wire.read();
  Wire.read();                                    // PEC
  uint16_t raw = (uint16_t)(lo | (hi << 8));
  if (raw & 0x8000) return NAN;                   // bandera de error
  return raw * 0.02f - 273.15f;
}
#endif

// ───────────── Red ─────────────
#define WIFI_SSID "mi-red"
#define WIFI_PASS "mi-clave"
#define DEVICE_ID "vt01"
static const uint16_t TCP_PORT = 3333;
#define MQTT_HOST "192.168.1.10"
static const uint16_t MQTT_PORT = 1883;
#define MQTT_PREFIX "vibraciones"
static const uint32_t MQTT_PERIOD_S = 10;

// ───────────── Configuración ─────────────
#define SENSOR_ADXL345 1
#define SENSOR_ADXL355 2
#define SENSOR_IIS3DWB 3
#ifndef SENSOR_TYPE
#define SENSOR_TYPE SENSOR_ADXL355
#endif

static const bool TRIAXIAL = false;              // true: X, Y y Z de cada sensor
#if CONFIG_IDF_TARGET_ESP32S3
static const int CS_PINS[4] = {10, 9, 8, -1};    // ESP32-S3 (no tiene GPIO 22–25)
static const int PIN_SCK = 12, PIN_MISO = 13, PIN_MOSI = 11;
static const int PIN_TACH = 4;
#else
static const int CS_PINS[4] = {5, 17, 16, -1};   // CS de cada sensor (−1 = no usado)
static const int PIN_SCK = 18, PIN_MISO = 19, PIN_MOSI = 23;
static const int PIN_TACH = 4;                   // sensor óptico (salida digital, pulso por vuelta)
#endif
static const uint32_t BAUD = 921600;
static const int RANGE_G = 4;                    // ±2, ±4, ±8 (ADXL355/IIS3DWB) o ±16 (ADXL345 siempre ±16 g)
static const int FRAME_SAMPLES = 64;             // muestras por trama binaria

// ───────────── Estado ─────────────
static Print *out = &Serial;                      // destino del flujo: USB o cliente TCP
#if USE_WIFI
static WiFiServer tcpServer(TCP_PORT);
static WiFiClient tcpClient;
#endif
#if USE_MQTT
static WiFiClient mqttNet;
static PubSubClient mqtt(mqttNet);
static uint32_t lastPublish = 0;
// Acumuladores del canal 1 para los indicadores
static double sumA2 = 0, sumV2 = 0, peakA = 0, hpPrevX = 0, hpPrevY = 0, vel = 0;
static uint32_t statN = 0, statPulses = 0;
#endif
static int sensorCount = 0;
static int csList[4];
static int channels = 0;
static bool running = false;
static bool binaryMode = false;
static bool forceMode = false;
static uint16_t seqNo = 0;
static float lsbG = 0;                            // g por cuenta del sensor
static float outScale = 0;                        // g por cuenta en la trama binaria
static uint32_t sampleRate = 0;
static const char *sensorName = "";

hw_timer_t *timer = nullptr;
volatile bool tick = false;
volatile uint32_t tachTimes[16];
volatile uint8_t tachHead = 0;
static uint8_t tachTail = 0;
volatile bool tachFlag = false;

void IRAM_ATTR onTimer() { tick = true; }
void IRAM_ATTR onTach() {
  tachTimes[tachHead & 15] = micros();
  tachHead++;
  tachFlag = true;
}

// ───────────── SPI ─────────────
SPISettings spiAdxl345(5000000, MSBFIRST, SPI_MODE3);
SPISettings spiAdxl355(8000000, MSBFIRST, SPI_MODE0);
SPISettings spiIis(10000000, MSBFIRST, SPI_MODE3);

static void spiWrite(SPISettings &cfg, int cs, uint8_t addr, uint8_t val) {
  SPI.beginTransaction(cfg);
  digitalWrite(cs, LOW);
  SPI.transfer(addr);
  SPI.transfer(val);
  digitalWrite(cs, HIGH);
  SPI.endTransaction();
}

static void spiRead(SPISettings &cfg, int cs, uint8_t addr, uint8_t *buf, int n) {
  SPI.beginTransaction(cfg);
  digitalWrite(cs, LOW);
  SPI.transfer(addr);
  for (int i = 0; i < n; i++) buf[i] = SPI.transfer(0);
  digitalWrite(cs, HIGH);
  SPI.endTransaction();
}

// ───────────── Controladores ─────────────
#if SENSOR_TYPE == SENSOR_ADXL345
static bool sensorBegin(int cs) {
  uint8_t id;
  spiRead(spiAdxl345, cs, 0x80 | 0x00, &id, 1);
  if (id != 0xE5) return false;
  spiWrite(spiAdxl345, cs, 0x31, 0x0B);   // DATA_FORMAT: resolución completa, ±16 g
  spiWrite(spiAdxl345, cs, 0x2C, 0x0F);   // BW_RATE: 3200 Hz
  spiWrite(spiAdxl345, cs, 0x2D, 0x08);   // POWER_CTL: medición
  return true;
}
static void sensorRead(int cs, int32_t xyz[3]) {
  uint8_t b[6];
  spiRead(spiAdxl345, cs, 0xC0 | 0x32, b, 6);
  for (int a = 0; a < 3; a++) xyz[a] = (int16_t)((b[2 * a + 1] << 8) | b[2 * a]);
}
static void sensorConfig() { sensorName = "ADXL345"; sampleRate = 3200; lsbG = 0.0039f; }

#elif SENSOR_TYPE == SENSOR_ADXL355
static bool sensorBegin(int cs) {
  uint8_t id[3];
  spiRead(spiAdxl355, cs, (0x00 << 1) | 1, id, 3);   // DEVID_AD, DEVID_MST, PARTID
  if (id[0] != 0xAD || id[2] != 0xED) return false;
  spiWrite(spiAdxl355, cs, (0x2F << 1), 0x52);       // RESET
  delay(10);
  uint8_t range = RANGE_G <= 2 ? 0x01 : RANGE_G <= 4 ? 0x02 : 0x03;
  spiWrite(spiAdxl355, cs, (0x2C << 1), 0x80 | range); // RANGE (I2C alta velocidad desactivado, INT activo alto)
  spiWrite(spiAdxl355, cs, (0x28 << 1), 0x00);       // FILTER: ODR 4000 Hz, pasabajos 1000 Hz, sin pasaaltos
  spiWrite(spiAdxl355, cs, (0x2D << 1), 0x00);       // POWER_CTL: medición
  return true;
}
static void sensorRead(int cs, int32_t xyz[3]) {
  uint8_t b[9];
  spiRead(spiAdxl355, cs, (0x08 << 1) | 1, b, 9);    // XDATA3 … ZDATA1
  for (int a = 0; a < 3; a++) {
    int32_t v = ((int32_t)b[3 * a] << 12) | ((int32_t)b[3 * a + 1] << 4) | (b[3 * a + 2] >> 4);
    if (v & 0x80000) v -= 0x100000;                  // 20 bits con signo
    xyz[a] = v;
  }
}
static void sensorConfig() {
  sensorName = "ADXL355"; sampleRate = 4000;
  lsbG = RANGE_G <= 2 ? 3.9e-6f : RANGE_G <= 4 ? 7.8e-6f : 15.6e-6f;
}

#elif SENSOR_TYPE == SENSOR_IIS3DWB
static bool sensorBegin(int cs) {
  uint8_t id;
  spiRead(spiIis, cs, 0x80 | 0x0F, &id, 1);          // WHO_AM_I
  if (id != 0x7B) return false;
  spiWrite(spiIis, cs, 0x12, 0x01);                  // CTRL3_C: SW_RESET
  delay(10);
  spiWrite(spiIis, cs, 0x12, 0x44);                  // CTRL3_C: BDU + IF_INC
  uint8_t fs = RANGE_G <= 2 ? 0x00 : RANGE_G <= 4 ? 0x08 : RANGE_G <= 8 ? 0x0C : 0x04;
  spiWrite(spiIis, cs, 0x09, 0x0A);                  // FIFO_CTRL3: lote de aceleración a 26 667 Hz
  spiWrite(spiIis, cs, 0x0A, 0x06);                  // FIFO_CTRL4: modo continuo
  spiWrite(spiIis, cs, 0x10, 0xA0 | fs);             // CTRL1_XL: ODR 26,7 kHz, escala
  return true;
}
static int sensorFifoCount(int cs) {
  uint8_t s[2];
  spiRead(spiIis, cs, 0x80 | 0x3A, s, 2);            // FIFO_STATUS1/2
  return s[0] | ((s[1] & 0x03) << 8);
}
static void sensorReadFifo(int cs, int32_t xyz[3]) {
  uint8_t b[7];
  spiRead(spiIis, cs, 0x80 | 0x78, b, 7);            // TAG + X, Y, Z
  for (int a = 0; a < 3; a++) xyz[a] = (int16_t)((b[2 + 2 * a] << 8) | b[1 + 2 * a]);
}
static void sensorConfig() {
  sensorName = "IIS3DWB"; sampleRate = 26667;
  lsbG = RANGE_G <= 2 ? 0.061e-3f : RANGE_G <= 4 ? 0.122e-3f : RANGE_G <= 8 ? 0.244e-3f : 0.488e-3f;
}
#endif

// ───────────── Salida ─────────────
static int16_t frameData[FRAME_SAMPLES * 12];
static uint8_t frameTach[(FRAME_SAMPLES + 7) / 8];
static int frameCount = 0;
static uint8_t txBuf[7 + FRAME_SAMPLES * 12 * 2 + (FRAME_SAMPLES + 7) / 8 + 2];

static uint16_t crc16(const uint8_t *d, int n) {
  uint16_t crc = 0xFFFF;
  for (int i = 0; i < n; i++) {
    crc ^= (uint16_t)d[i] << 8;
    for (int k = 0; k < 8; k++) crc = (crc & 0x8000) ? (uint16_t)((crc << 1) ^ 0x1021) : (uint16_t)(crc << 1);
  }
  return crc;
}

static void flushFrame() {
  if (frameCount == 0) return;
  int n = frameCount, tb = (n + 7) / 8, o = 0;
  txBuf[o++] = 0xA5; txBuf[o++] = 0x5A;
  txBuf[o++] = seqNo & 0xFF; txBuf[o++] = seqNo >> 8;
  txBuf[o++] = n & 0xFF; txBuf[o++] = n >> 8;
  txBuf[o++] = (uint8_t)channels;
  for (int i = 0; i < n * channels; i++) { txBuf[o++] = frameData[i] & 0xFF; txBuf[o++] = (uint16_t)frameData[i] >> 8; }
  for (int i = 0; i < tb; i++) txBuf[o++] = frameTach[i];
  uint16_t crc = crc16(txBuf + 2, o - 2);
  txBuf[o++] = crc & 0xFF; txBuf[o++] = crc >> 8;
  out->write(txBuf, o);
  seqNo++;
  frameCount = 0;
  memset(frameTach, 0, sizeof(frameTach));
}

/* Agrega una muestra (todos los canales, en cuentas del sensor) a la salida. */
static void emitSample(const int32_t *values, bool tach) {
  if (!binaryMode) {
    char line[160];
    int o = 0;
    for (int c = 0; c < channels; c++) o += snprintf(line + o, sizeof(line) - o, "%.5f,", values[c] * lsbG);
    snprintf(line + o, sizeof(line) - o, "%d\n", tach ? 1 : 0);
    out->print(line);
    return;
  }
  for (int c = 0; c < channels; c++) {
    float g = values[c] * lsbG;
    long q = lroundf(g / outScale);
    if (q > 32767) q = 32767;
    if (q < -32768) q = -32768;
    frameData[frameCount * channels + c] = (int16_t)q;
  }
  if (tach) frameTach[frameCount / 8] |= 1 << (frameCount % 8);
  if (++frameCount >= FRAME_SAMPLES) flushFrame();
}

static void sendHeader() {
  if (!forceMode) binaryMode = (uint32_t)channels * sampleRate * 9 * 10 > BAUD;   // ≈ 9 caracteres por valor
  String names;
  const char axes[3] = {'X', 'Y', 'Z'};
  for (int s = 0; s < sensorCount; s++)
    for (int a = TRIAXIAL ? 0 : 2; a < 3; a++) {
      if (names.length()) names += ',';
      names += String("S") + (s + 1) + axes[a];
    }
  if (binaryMode)
    out->printf("#VA2 fs=%lu ch=%d unit=g tach=1 scale=%.9g names=%s sensor=%s\n", (unsigned long)sampleRate, channels, outScale, names.c_str(), sensorName);
  else
    out->printf("#VA fs=%lu ch=%d unit=g tach=1\n", (unsigned long)sampleRate, channels);
}

static void sendInfo() {
  out->printf("# sensor=%s sensores=%d canales=%d fs=%lu rango=±%dg modo=%s", sensorName, sensorCount, channels,
                (unsigned long)sampleRate, RANGE_G, binaryMode ? "binario" : "texto");
#if USE_MLX90614
  out->printf(" temperatura=%.1f", readTemperatureC());
#endif
  out->print("\n");
}

// ───────────── Programa ─────────────
void setup() {
  Serial.begin(BAUD);
  SPI.begin(PIN_SCK, PIN_MISO, PIN_MOSI);
  sensorConfig();
  for (int i = 0; i < 4; i++) {
    if (CS_PINS[i] < 0) continue;
    pinMode(CS_PINS[i], OUTPUT);
    digitalWrite(CS_PINS[i], HIGH);
  }
  delay(20);
  for (int i = 0; i < 4; i++)
    if (CS_PINS[i] >= 0 && sensorBegin(CS_PINS[i])) csList[sensorCount++] = CS_PINS[i];
    else if (CS_PINS[i] >= 0) Serial.printf("# AVISO: no responde el %s en CS=%d\n", sensorName, CS_PINS[i]);
  channels = sensorCount * (TRIAXIAL ? 3 : 1);
  // Escala de la trama binaria: rango completo del sensor en 16 bits
  outScale = (SENSOR_TYPE == SENSOR_ADXL345 ? 16.0f : (float)RANGE_G) / 32767.0f;
#if USE_MLX90614
  Wire.begin(PIN_SDA, PIN_SCL, 100000);
#endif
  pinMode(PIN_TACH, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_TACH), onTach, FALLING);
#if SENSOR_TYPE != SENSOR_IIS3DWB
  timer = timerBegin(1000000);                   // 1 MHz (API de núcleo 3.x)
  timerAttachInterrupt(timer, &onTimer);
  timerAlarm(timer, 1000000 / sampleRate, true, 0);
#endif
  if (sensorCount == 0) Serial.println("# ERROR: no se encontró ningún acelerómetro. Revisar cableado y SENSOR_TYPE.");
#if USE_WIFI
  WiFi.mode(WIFI_STA);
  WiFi.setHostname("vib-" DEVICE_ID);
  WiFi.begin(WIFI_SSID, WIFI_PASS);
  tcpServer.begin();
  tcpServer.setNoDelay(true);
#endif
#if USE_MQTT
  mqtt.setServer(MQTT_HOST, MQTT_PORT);
#endif
}

#if USE_WIFI
/* Acepta un cliente TCP (uno a la vez) y redirige el flujo a él; al desconectarse vuelve al USB. */
static void serviceNetwork() {
  static bool announced = false;
  if (WiFi.status() == WL_CONNECTED && !announced) {
    Serial.printf("# WiFi conectado: %s, puerto %u\n", WiFi.localIP().toString().c_str(), TCP_PORT);
    announced = true;
  }
  if (WiFi.status() != WL_CONNECTED) announced = false;
  WiFiClient c = tcpServer.accept();
  if (c) {
    if (tcpClient && tcpClient.connected()) tcpClient.stop();
    tcpClient = c;
    tcpClient.setNoDelay(true);
    out = &tcpClient;
    running = false;
  }
  if (out == &tcpClient && !tcpClient.connected()) { out = &Serial; running = false; }
}
#endif

#if USE_MQTT
/* Acumula los indicadores del canal 1 (en g): RMS, pico y velocidad 10–1000 Hz por integración con filtro pasaaltos. */
static void accumulate(float g, bool tach) {
  float fs = (float)sampleRate;
  float a = 1.0f / (1.0f + 2.0f * PI * 10.0f / fs);          // pasaaltos de 1.er orden a 10 Hz
  double y = a * (hpPrevY + g - hpPrevX);
  hpPrevX = g; hpPrevY = y;
  vel = vel * (1.0 - 2.0 * PI * 2.0 / fs) + y * 9806.65 / fs;  // integración con fuga (mm/s)
  sumA2 += (double)g * g; sumV2 += vel * vel;
  if (fabs(g) > peakA) peakA = fabs(g);
  statN++;
  if (tach) statPulses++;
}

static void serviceMqtt() {
  if (WiFi.status() != WL_CONNECTED) return;
  if (!mqtt.connected()) {
    static uint32_t lastTry = 0;
    if (millis() - lastTry < 5000) return;
    lastTry = millis();
    if (!mqtt.connect("vib-" DEVICE_ID)) return;
  }
  mqtt.loop();
  uint32_t now = millis();
  if (now - lastPublish < MQTT_PERIOD_S * 1000 || statN < 16) return;
  double secs = (now - lastPublish) / 1000.0;
  lastPublish = now;
  double accRms = sqrt(sumA2 / statN), velRms = sqrt(sumV2 / statN);
  char json[300];
  int len = snprintf(json, sizeof(json),
           "{\"id\":\"%s\",\"sensor\":\"%s\",\"fs\":%lu,\"acc_rms\":%.5f,\"acc_pico\":%.5f,\"factor_cresta\":%.3f,\"vel_rms\":%.4f,\"rpm\":%.1f,\"n\":%lu",
           DEVICE_ID, sensorName, (unsigned long)sampleRate, accRms, peakA, accRms > 0 ? peakA / accRms : 0, velRms,
           statPulses * 60.0 / secs, (unsigned long)statN);
#if USE_MLX90614
  float tc = readTemperatureC();
  if (!isnan(tc) && len > 0 && len < (int)sizeof(json) - 32) len += snprintf(json + len, sizeof(json) - len, ",\"temperatura\":%.2f", tc);
#endif
  if (len > 0 && len < (int)sizeof(json) - 2) { json[len] = '}'; json[len + 1] = 0; }
  mqtt.publish(MQTT_PREFIX "/" DEVICE_ID "/indicadores", json);
  sumA2 = sumV2 = peakA = 0; statN = statPulses = 0;
}
#endif

static void handleCommands() {
  Stream *in = &Serial;
#if USE_WIFI
  if (out == &tcpClient) in = &tcpClient;
#endif
  if (!in->available()) return;
  String cmd = in->readStringUntil('\n');
  cmd.trim();
  cmd.toUpperCase();
  if (cmd == "START" && sensorCount > 0) { running = true; seqNo = 0; frameCount = 0; tachTail = tachHead; sendHeader(); }
  else if (cmd == "STOP") { if (binaryMode) flushFrame(); running = false; }
  else if (cmd == "BIN") { binaryMode = true; forceMode = true; }
  else if (cmd == "TEXT") { binaryMode = false; forceMode = true; }
  else if (cmd == "INFO") sendInfo();
}

void loop() {
#if USE_WIFI
  serviceNetwork();
#endif
#if USE_MQTT
  serviceMqtt();
#endif
  handleCommands();
  if (!running && !USE_MQTT) return;          // con MQTT se muestrea siempre para calcular los indicadores
  int32_t xyz[3], values[12];

#if SENSOR_TYPE == SENSOR_IIS3DWB
  // FIFO: se leen las muestras disponibles en todos los sensores (la menor cantidad, para mantenerlos alineados)
  int avail = 512;
  for (int s = 0; s < sensorCount; s++) avail = min(avail, sensorFifoCount(csList[s]));
  if (avail <= 0) return;
  uint32_t now = micros();
  for (int i = 0; i < avail; i++) {
    int c = 0;
    for (int s = 0; s < sensorCount; s++) {
      sensorReadFifo(csList[s], xyz);
      for (int a = TRIAXIAL ? 0 : 2; a < 3; a++) values[c++] = xyz[a];
    }
    // El pulso se ubica en la muestra cuyo instante (estimado desde el momento de lectura) es el más cercano
    bool t = false;
    while (tachTail != tachHead) {
      uint32_t age = now - tachTimes[tachTail & 15];
      long idx = (long)avail - 1 - (long)((uint64_t)age * sampleRate / 1000000ULL);
      if (idx > i) break;
      t = true; tachTail++;
    }
#if USE_MQTT
    accumulate(values[0] * lsbG, t);
#endif
    if (running) emitSample(values, t);
  }
#else
  if (!tick) return;
  tick = false;
  int c = 0;
  for (int s = 0; s < sensorCount; s++) {
    sensorRead(csList[s], xyz);
    for (int a = TRIAXIAL ? 0 : 2; a < 3; a++) values[c++] = xyz[a];
  }
  bool t = tachFlag;
  if (t) { tachFlag = false; tachTail = tachHead; }
#if USE_MQTT
  accumulate(values[0] * lsbG, t);
#endif
  if (running) emitSample(values, t);
#endif
}
