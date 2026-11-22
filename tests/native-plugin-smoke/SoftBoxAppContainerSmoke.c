#include <stdint.h>
#include <string.h>
#define SOFTBOX_EXPORT __declspec(dllexport)
#define SOFTBOX_CDECL __cdecl
SOFTBOX_EXPORT int SOFTBOX_CDECL SoftBoxPluginGetApiVersion(void) { return 1; }
SOFTBOX_EXPORT int SOFTBOX_CDECL SoftBoxPluginExecute(const void* request_utf8, int request_length, void* response_buffer, int response_capacity, int* response_length)
{
    static const char result[] = "{\"marker\":\"softbox-appcontainer-smoke\",\"executed\":true}";
    if (request_utf8 == 0 || request_length <= 0 || response_buffer == 0 || response_length == 0) return 1;
    if (response_capacity < (int)(sizeof(result) - 1)) return 2;
    memcpy(response_buffer, result, sizeof(result) - 1);
    *response_length = (int)(sizeof(result) - 1);
    return 0;
}
SOFTBOX_EXPORT void SOFTBOX_CDECL SoftBoxPluginShutdown(void) {}
