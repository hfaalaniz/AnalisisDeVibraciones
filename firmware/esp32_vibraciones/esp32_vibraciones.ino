/*
  Sensor de vibraciones USB para "Análisis de Vibraciones y Balanceo"
  Autor: Fabian Alaniz – ElectroNet

  Hardware:
    - ESP32 DevKit (cualquier modelo con USB)
    - 1 o 2 acelerómetros ADXL345 por SPI (VSPI: SCK=18, MISO=19, MOSI=23; CS1=5, CS2=17)
    - Sensor óptico reflectivo (tacómetro) en GPIO 4, salida digital, un pulso por vuelta sobre cinta reflectiva

  Protocolo (texto, 921600 baudios):
    La PC envía "START" o "STOP".
    El sensor responde con la cabecera  "#VA fs=3200 ch=2 unit=g tach=1"
    y luego una línea por muestra:      "<sensor1_g>,<sensor2_g>,<tach 0|1>"

  Nota: ejemplo de referencia. Verificar el cableado y la fijación del acelerómetro (base magnética o espárrago):
  una fijación floja atenúa las frecuencias altas y altera la fase.
*/
#include <SPI.h>

static const int PIN_CS1 = 5;
static const int PIN_CS2 = 17;      // poner -1 si se usa un solo sensor
static const int PIN_TACH = 4;
static const uint32_t FS = 3200;     // Hz (frecuencia máxima del ADXL345)
static const uint32_t BAUD = 921600;

static const uint8_t REG_BW_RATE = 0x2C, REG_POWER_CTL = 0x2D, REG_DATA_FORMAT = 0x31, REG_DATAX0 = 0x32, REG_DEVID = 0x00;
static const float LSB_G = 0.0039f;  // modo resolución completa: 3,9 mg/LSB

SPISettings spiCfg(5000000, MSBFIRST, SPI_MODE3);
hw_timer_t *timer = nullptr;
volatile bool tick = false;
volatile bool tachPulse = false;
bool running = false;
int sensors = 1;

void IRAM_ATTR onTimer() { tick = true; }
void IRAM_ATTR onTach() { tachPulse = true; }

void writeReg(int cs, uint8_t reg, uint8_t val) {
  SPI.beginTransaction(spiCfg);
  digitalWrite(cs, LOW);
  SPI.transfer(reg & 0x3F);
  SPI.transfer(val);
  digitalWrite(cs, HIGH);
  SPI.endTransaction();
}

uint8_t readReg(int cs, uint8_t reg) {
  SPI.beginTransaction(spiCfg);
  digitalWrite(cs, LOW);
  SPI.transfer(0x80 | (reg & 0x3F));
  uint8_t v = SPI.transfer(0);
  digitalWrite(cs, HIGH);
  SPI.endTransaction();
  return v;
}

/* Lee el eje Z (perpendicular a la base de montaje) en g. */
float readZ(int cs) {
  uint8_t b[6];
  SPI.beginTransaction(spiCfg);
  digitalWrite(cs, LOW);
  SPI.transfer(0xC0 | REG_DATAX0);     // lectura múltiple
  for (int i = 0; i < 6; i++) b[i] = SPI.transfer(0);
  digitalWrite(cs, HIGH);
  SPI.endTransaction();
  int16_t z = (int16_t)((b[5] << 8) | b[4]);
  return z * LSB_G;
}

bool setupSensor(int cs) {
  if (cs < 0) return false;
  pinMode(cs, OUTPUT);
  digitalWrite(cs, HIGH);
  if (readReg(cs, REG_DEVID) != 0xE5) return false;   // ID del ADXL345
  writeReg(cs, REG_DATA_FORMAT, 0x0B);                  // resolución completa, ±16 g
  writeReg(cs, REG_BW_RATE, 0x0F);                      // 3200 Hz
  writeReg(cs, REG_POWER_CTL, 0x08);                    // medición
  return true;
}

void setup() {
  Serial.begin(BAUD);
  SPI.begin(18, 19, 23);
  bool s1 = setupSensor(PIN_CS1);
  bool s2 = setupSensor(PIN_CS2);
  sensors = s1 && s2 ? 2 : 1;
  if (!s1) Serial.println("# ERROR: no se encontró el ADXL345 del sensor 1");
  pinMode(PIN_TACH, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(PIN_TACH), onTach, FALLING);
  timer = timerBegin(1000000);                  // API de núcleo ESP32 3.x: 1 MHz
  timerAttachInterrupt(timer, &onTimer);
  timerAlarm(timer, 1000000 / FS, true, 0);
}

void loop() {
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    if (cmd == "START") {
      running = true;
      Serial.printf("#VA fs=%lu ch=%d unit=g tach=1\n", (unsigned long)FS, sensors);
    } else if (cmd == "STOP") {
      running = false;
    }
  }
  if (!running || !tick) return;
  tick = false;
  float a1 = readZ(PIN_CS1);
  float a2 = sensors == 2 ? readZ(PIN_CS2) : 0;
  int t = 0;
  if (tachPulse) { tachPulse = false; t = 1; }
  if (sensors == 2) Serial.printf("%.4f,%.4f,%d\n", a1, a2, t);
  else Serial.printf("%.4f,%d\n", a1, t);
}
