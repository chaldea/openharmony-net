#include <napi/native_api.h>
#include <cstdint>

extern "C" int32_t openharmony_main();
extern "C" int32_t openharmony_runtime_major();

static napi_value Run(napi_env env, napi_callback_info)
{
    napi_value result;
    napi_create_int32(env, openharmony_main(), &result);
    return result;
}

static napi_value RuntimeMajor(napi_env env, napi_callback_info)
{
    napi_value result;
    napi_create_int32(env, openharmony_runtime_major(), &result);
    return result;
}

static napi_value Init(napi_env env, napi_value exports)
{
    napi_property_descriptor properties[] = {
        { "run", nullptr, Run, nullptr, nullptr, nullptr, napi_default, nullptr },
        { "runtimeMajor", nullptr, RuntimeMajor, nullptr, nullptr, nullptr, napi_default, nullptr }
    };
    napi_define_properties(env, exports, 2, properties);
    return exports;
}

static napi_module module = { 1, 0, nullptr, Init, "dotnet", nullptr, {0} };
extern "C" __attribute__((constructor)) void RegisterDotNet() { napi_module_register(&module); }