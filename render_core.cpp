#include "render_core.h"
#define _WIN32_WINNT 0x0A00
#include <Audioclient.h>
#include <Mmdeviceapi.h>
#include <avrt.h>
#include <wrl.h>
#include <atomic>
#include <thread>
#include <vector>
#include <mutex>
#include <condition_variable>
#include <cstring>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "Mmdevapi.lib")

using Microsoft::WRL::ComPtr;

static std::atomic<bool> g_running{false};
static std::thread g_thread;
static HANDLE g_event = NULL;
static ComPtr<IAudioClient> g_client;
static ComPtr<IAudioRenderClient> g_render;
static WAVEFORMATEX* g_wfx = nullptr;
static UINT32 g_bufferFrames = 0;
static UINT32 g_sampleRate = 0;
static int g_requestedMs = 0;
static int g_quantizedMs = 0;
static int g_effectiveMs = 0;
static int g_path = 0; // 0 shared, 1 exclusive, 2 exclusive-raw
static int g_eventMode = 1;

static std::vector<uint8_t> g_ring;
static size_t g_ring_r = 0, g_ring_w = 0;
static std::mutex g_mtx;
static std::condition_variable g_cv;

static size_t ring_free() {
    if (g_ring_w >= g_ring_r) return g_ring.size() - (g_ring_w - g_ring_r) - 1;
    return (g_ring_r - g_ring_w) - 1;
}
static size_t ring_avail() {
    if (g_ring_w >= g_ring_r) return (g_ring_w - g_ring_r);
    return g_ring.size() - (g_ring_r - g_ring_w);
}

static int BufQuantizeMs(int wantMs, bool exclusive, double defMs, double minMs) {
    int ms = wantMs;
    if (exclusive) {
        int floor = (int)ceil(defMs * 3.0);
        if (ms < floor) ms = floor;
        if (minMs > 0) {
            double k = ceil(ms / minMs);
            ms = (int)ceil(k * minMs);
        }
    } else {
        int floor = (int)ceil(defMs * 2.0);
        if (ms < floor) ms = floor;
    }
    return ms;
}

static void render_thread() {
    // MMCSS
    DWORD taskIndex=0;
    HANDLE hTask = AvSetMmThreadCharacteristicsW(L"Pro Audio", &taskIndex);

    // pre-silence fill
    UINT32 padding = 0;
    g_client->GetCurrentPadding(&padding);
    UINT32 toWrite = g_bufferFrames - padding;
    if (toWrite > 0) {
        BYTE* pData = nullptr;
        if (SUCCEEDED(g_render->GetBuffer(toWrite, &pData))) {
            memset(pData, 0, toWrite * g_wfx->nBlockAlign);
            g_render->ReleaseBuffer(toWrite, 0);
        }
    }
    g_client->Start();

    while (g_running.load()) {
        DWORD wait = WaitForSingleObject(g_event, 10);
        if (wait == WAIT_TIMEOUT) continue;
        if (!g_running.load()) break;

        UINT32 padding2 = 0;
        if (FAILED(g_client->GetCurrentPadding(&padding2))) continue;
        UINT32 frames = g_bufferFrames - padding2;
        if (frames == 0) continue;

        BYTE* pData = nullptr;
        if (FAILED(g_render->GetBuffer(frames, &pData))) continue;

        size_t needBytes = (size_t)frames * g_wfx->nBlockAlign;
        size_t taken = 0;
        // pull from ring
        {
            std::unique_lock<std::mutex> lk(g_mtx);
            size_t avail = ring_avail();
            taken = (avail >= needBytes) ? needBytes : avail;
            size_t first = std::min(taken, g_ring.size() - g_ring_r);
            memcpy(pData, &g_ring[g_ring_r], first);
            if (taken > first) {
                memcpy(pData + first, &g_ring[0], taken - first);
            }
            g_ring_r = (g_ring_r + taken) % g_ring.size();
        }
        // zero-fill remainder
        if (taken < needBytes) {
            memset(pData + taken, 0, needBytes - taken);
        }

        g_render->ReleaseBuffer(frames, 0);
    }
    g_client->Stop();
    if (hTask) AvRevertMmThreadCharacteristics(hTask);
}

RC_API int rc_open(const rc_open_params* p) {
    if (!p) return -1;

    rc_close(); // ensure clean

    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    // Allow multiple init; ignore failure if already initialized.

    ComPtr<IMMDeviceEnumerator> enumr;
    hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&enumr));
    if (FAILED(hr)) return -2;

    ComPtr<IMMDevice> dev;
    hr = enumr->GetDefaultAudioEndpoint(eRender, eConsole, &dev);
    if (FAILED(hr)) return -3;

    hr = dev->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, &g_client);
    if (FAILED(hr)) return -4;

    // prefer IAudioClient3 for low-latency in future
    ComPtr<IAudioClient2> ac2;
    g_client.As(&ac2);

    // build format
    WAVEFORMATEXTENSIBLE wfxex = {};
    wfxex.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
    wfxex.Format.nChannels = (WORD)p->channels;
    wfxex.Format.nSamplesPerSec = (DWORD)p->sampleRate;
    wfxex.Format.wBitsPerSample = (WORD)p->bits;
    wfxex.Format.nBlockAlign = (wfxex.Format.nChannels * wfxex.Format.wBitsPerSample) / 8;
    wfxex.Format.nAvgBytesPerSec = wfxex.Format.nBlockAlign * wfxex.Format.nSamplesPerSec;
    wfxex.Format.cbSize = 22;
    wfxex.Samples.wValidBitsPerSample = wfxex.Format.wBitsPerSample;
    wfxex.dwChannelMask = (wfxex.Format.nChannels==2)? (SPEAKER_FRONT_LEFT|SPEAKER_FRONT_RIGHT) : 0;
    wfxex.SubFormat = KSDATAFORMAT_SUBTYPE_PCM;
    g_wfx = (WAVEFORMATEX*)CoTaskMemAlloc(sizeof(WAVEFORMATEXTENSIBLE));
    memcpy(g_wfx, &wfxex, sizeof(WAVEFORMATEXTENSIBLE));

    // RAW preference via AudioClientProperties if available
    if (ac2 && p->preferRaw) {
        AudioClientProperties props = {};
        props.cbSize = sizeof(props);
        props.bIsOffload = FALSE;
        props.eCategory = AudioCategory_Media;
        props.Options = AUDCLNT_STREAMOPTIONS_RAW;
        ac2->SetClientProperties(&props); // ignore failure
        g_path = 2; // exclusive-raw preferred
    }

    // compute buffer duration
    REFERENCE_TIME hnsRequestedDuration = (REFERENCE_TIME)(p->targetBufferMs * 10000); // ms -> 100ns
    g_requestedMs = p->targetBufferMs;

    DWORD flags = AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_NOPERSIST;
    AUDCLNT_SHAREMODE share = p->preferExclusive ? AUDCLNT_SHAREMODE_EXCLUSIVE : AUDCLNT_SHAREMODE_SHARED;

    hr = g_client->Initialize(share, flags, hnsRequestedDuration, 0, g_wfx, nullptr);
    if (FAILED(hr)) {
        // fallback: shared
        share = AUDCLNT_SHAREMODE_SHARED;
        g_path = 0;
        hr = g_client->Initialize(share, flags, hnsRequestedDuration, 0, g_wfx, nullptr);
        if (FAILED(hr)) {
            CoTaskMemFree(g_wfx); g_wfx=nullptr;
            g_client.Reset();
            return -5;
        }
    } else {
        g_path = (p->preferRaw?2:1);
    }

    hr = g_client->GetBufferSize(&g_bufferFrames);
    if (FAILED(hr)) { rc_close(); return -6; }
    g_sampleRate = g_wfx->nSamplesPerSec;
    g_effectiveMs = (int)((g_bufferFrames * 1000ULL) / (g_sampleRate?g_sampleRate:1));

    // quantized ms (rough) — align to effective buffer as observed
    g_quantizedMs = g_effectiveMs;

    hr = g_client->GetService(IID_PPV_ARGS(&g_render));
    if (FAILED(hr)) { rc_close(); return -7; }

    if (!g_event) g_event = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    if (!g_event) { rc_close(); return -8; }
    g_client->SetEventHandle(g_event);

    // ring buffer: 2x buffer bytes
    size_t bytesPerFrame = g_wfx->nBlockAlign;
    size_t ringBytes = (size_t)g_bufferFrames * bytesPerFrame * 2;
    g_ring.assign(ringBytes, 0);
    g_ring_r = g_ring_w = 0;

    g_running.store(true);
    g_thread = std::thread(render_thread);
    return 0;
}

RC_API void rc_close() {
    if (g_running.exchange(false)) {
        if (g_event) SetEvent(g_event);
        if (g_thread.joinable()) g_thread.join();
    }
    if (g_client) g_client->Stop();
    g_render.Reset();
    g_client.Reset();
    if (g_wfx) { CoTaskMemFree(g_wfx); g_wfx=nullptr; }
    if (g_event) { CloseHandle(g_event); g_event=nullptr; }
    g_ring.clear();
    g_ring_r = g_ring_w = 0;
}

RC_API void rc_get_status(rc_status* out) {
    if (!out) return;
    out->requestedMs = g_requestedMs;
    out->quantizedMs = g_quantizedMs;
    out->effectiveMs = g_effectiveMs;
    out->path       = g_path;
    out->eventMode  = g_eventMode;
    out->running    = g_running.load()?1:0;
}

RC_API int rc_write(const void* data, int bytes) {
    if (!data || bytes<=0 || g_ring.empty()) return 0;
    std::unique_lock<std::mutex> lk(g_mtx);
    size_t freeb = ring_free();
    size_t tocopy = (size_t)bytes <= freeb ? (size_t)bytes : freeb;
    size_t first = std::min(tocopy, g_ring.size() - g_ring_w);
    memcpy(&g_ring[g_ring_w], data, first);
    if (tocopy > first) memcpy(&g_ring[0], (const uint8_t*)data + first, tocopy - first);
    g_ring_w = (g_ring_w + tocopy) % g_ring.size();
    return (int)tocopy;
}
