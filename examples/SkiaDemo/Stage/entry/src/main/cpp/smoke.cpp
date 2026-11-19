#include <napi/native_api.h>
#include <cstdint>

extern "C" int32_t openharmony_skia_render(uint8_t* pixels, int32_t length);

static napi_value Render(napi_env env, napi_callback_info info)
{
    size_t argc = 1;
    napi_value argument;
    napi_get_cb_info(env, info, &argc, &argument, nullptr, nullptr);
    void* pixels = nullptr;
    size_t length = 0;
    if (argc != 1 || napi_get_arraybuffer_info(env, argument, &pixels, &length) != napi_ok ||
        length != 640 * 800 * 4) {
        napi_throw_type_error(env, nullptr, "render expects a 640x800 RGBA ArrayBuffer");
        return nullptr;
    }
    int32_t glyphs = openharmony_skia_render(static_cast<uint8_t*>(pixels), static_cast<int32_t>(length));
    if (glyphs <= 0) {
        napi_throw_error(env, nullptr, "Skia/HarfBuzz rendering failed; inspect SKIA_RENDER_FAILED in hilog");
        return nullptr;
    }
    // Validate background and actual rasterized geometry before displaying it.
    auto data = static_cast<uint8_t*>(pixels);
    const int center = (320 * 640 + 210) * 4;
    if (data[0] != 16 || data[1] != 24 || data[2] != 40 || data[3] != 255 ||
        data[center] < 150 || data[center + 3] != 255) {
        napi_throw_error(env, nullptr, "Skia pixel validation failed");
        return nullptr;
    }
    napi_value result;
    napi_create_int32(env, glyphs, &result);
    return result;
}

static napi_value Init(napi_env env, napi_value exports)
{
    napi_property_descriptor property = { "render", nullptr, Render, nullptr, nullptr, nullptr, napi_default, nullptr };
    napi_define_properties(env, exports, 1, &property);
    return exports;
}

static napi_module module = { 1, 0, nullptr, Init, "smoke", nullptr, {0} };
extern "C" __attribute__((constructor)) void RegisterSkiaDemo() { napi_module_register(&module); }
