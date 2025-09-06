#pragma once
#include <stdint.h>

#ifdef _WIN32
  #define RC_API extern "C" __declspec(dllexport)
#else
  #define RC_API extern "C"
#endif

#pragma pack(push, 1)
struct rc_open_params {
    int32_t sampleRate;   // 48000 / 44100 / etc.
    int32_t bits;         // 16/24/32
    int32_t channels;     // 2
    int32_t targetBufferMs; // requested buffer in ms
    int32_t preferRaw;      // 0/1
    int32_t preferExclusive;// 0/1
};

struct rc_status {
    int32_t requestedMs;   // requested
    int32_t quantizedMs;   // after Buf(...)
    int32_t effectiveMs;   // via BufferSize/SampleRate
    int32_t path;          // 0=shared,1=exclusive,2=exclusive-raw
    int32_t eventMode;     // 0=poll,1=event
    int32_t running;       // 0/1
};
#pragma pack(pop)

RC_API int  rc_open(const rc_open_params* p);
RC_API void rc_close();
RC_API void rc_get_status(rc_status* out);
RC_API int  rc_write(const void* data, int bytes);
