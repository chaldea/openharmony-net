#pragma once
#include <cstdint>

// Keep field order/signatures identical to SurfaceApplication.NativeCallbacks.
struct SurfaceCallbacks {
    int32_t version;
    int32_t (*create)(void* (*getProc)(const char*), void (*log)(const char*));
    int32_t (*frame)(int32_t width, int32_t height, float density, double seconds);
    void (*destroy)();
    void (*touch)(int32_t action, int32_t id, float x, float y);
    int32_t (*back)();
    void (*active)(int32_t active);
    void (*text)(const char* utf8, int32_t length);
    void (*key)(int32_t key);
    int32_t (*editor)(char* utf8, int32_t capacity);
    void (*navigate)(const char* utf8, int32_t length);
};
