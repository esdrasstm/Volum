#pragma once

// Contrato do equalizador do Volum (versão 3). O layout é fixo: o C# (EqConfig.cs),
// o motor e o teste conferem os tamanhos. Mudou um campo? Suba a versão do arquivo.

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

enum VolumEqType {
    VOLUM_EQ_OFF = 0,
    VOLUM_EQ_BELL = 1,
    VOLUM_EQ_LOWSHELF = 2,
    VOLUM_EQ_HIGHSHELF = 3,
    VOLUM_EQ_HIGHPASS = 4,
    VOLUM_EQ_LOWPASS = 5,
    VOLUM_EQ_NOTCH = 6
};

#define VOLUM_EQ_TYPE_LAST 6
#define VOLUM_EQ_MAX_BANDS 32
#define VOLUM_EQ_MAX_SLOPE 4          // corte: 1..4 = 12, 24, 36, 48 dB/oitava
#define VOLUM_EQ_MAX_DEVICES 16
#define VOLUM_EQ_ID_CHARS 256

#define VOLUM_EQ_MIN_FREQ 10.0
#define VOLUM_EQ_MAX_FREQ 22000.0
#define VOLUM_EQ_MIN_Q 0.1
#define VOLUM_EQ_MAX_Q 24.0
#define VOLUM_EQ_MAX_GAIN 24.0
#define VOLUM_EQ_MIN_PREAMP -30.0
#define VOLUM_EQ_MAX_PREAMP 12.0

// 4*4 + 3*8 = 40 bytes
typedef struct VolumEqBand {
    int32_t type;
    int32_t enabled;
    int32_t slope;
    int32_t reserved;
    double freq_hz;
    double gain_db;
    double q;
} VolumEqBand;

// 4*4 + 8 + 32*40 = 1304 bytes
typedef struct VolumEqParams {
    int32_t bypass;
    int32_t band_count;
    int32_t flags;
    int32_t reserved;
    double preamp_db;
    VolumEqBand bands[VOLUM_EQ_MAX_BANDS];
} VolumEqParams;

#define VOLUM_EQ_FLAG_INSTALLED 1

// eq.bin: cabeçalho de 24 bytes e um registro por dispositivo.
// Registro: id do endpoint (UTF-16, 256 caracteres, terminado em zero) + VolumEqParams = 512 + 1304 = 1816.
#define VOLUM_EQ_FILE_MAGIC "VOLUMEQ3"
#define VOLUM_EQ_FILE_VERSION 3
#define VOLUM_EQ_HEADER_BYTES 24
#define VOLUM_EQ_RECORD_BYTES 1816

#ifdef __cplusplus
}
#endif
