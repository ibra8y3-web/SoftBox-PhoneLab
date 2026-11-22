#pragma once
#include <stdint.h>
#ifdef _WIN32
#define SOFTBOX_PLUGIN_EXPORT extern "C" __declspec(dllexport)
#define SOFTBOX_PLUGIN_CALL __cdecl
#else
#define SOFTBOX_PLUGIN_EXPORT
#define SOFTBOX_PLUGIN_CALL
#endif
#define SOFTBOX_PLUGIN_API_VERSION 1
SOFTBOX_PLUGIN_EXPORT int SOFTBOX_PLUGIN_CALL SoftBoxPluginGetApiVersion(void);
/*
 * requestUtf8/requestLength: host-owned UTF-8 JSON bytes.
 * responseBuffer/responseCapacity: host-owned output buffer, max 1 MiB.
 * responseLength: output byte count. Return 0 for success.
 * Pointers are valid only during this function call. Do not retain them.
 */
SOFTBOX_PLUGIN_EXPORT int SOFTBOX_PLUGIN_CALL SoftBoxPluginExecute(
    const uint8_t* requestUtf8, int requestLength,
    uint8_t* responseBuffer, int responseCapacity, int* responseLength);
SOFTBOX_PLUGIN_EXPORT void SOFTBOX_PLUGIN_CALL SoftBoxPluginShutdown(void);
