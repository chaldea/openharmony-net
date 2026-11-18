#include <napi/native_api.h>
#include <cstdint>

extern "C" int32_t openharmony_dotnet_add(int32_t left, int32_t right);
extern "C" int32_t openharmony_dotnet_version();

static napi_value Add(napi_env env, napi_callback_info info)
{
    size_t argc = 2;
    napi_value args[2];
    int32_t left = 0, right = 0;
    napi_get_cb_info(env, info, &argc, args, nullptr, nullptr);
    if (argc != 2 || napi_get_value_int32(env, args[0], &left) != napi_ok ||
        napi_get_value_int32(env, args[1], &right) != napi_ok) {
        napi_throw_type_error(env, nullptr, "add expects two integers");
        return nullptr;
    }
    napi_value result;
    napi_create_int32(env, openharmony_dotnet_add(left, right), &result);
    return result;
}

static napi_value RuntimeMajor(napi_env env, napi_callback_info)
{
    napi_value result;
    napi_create_int32(env, openharmony_dotnet_version(), &result);
    return result;
}

static napi_value Init(napi_env env, napi_value exports)
{
    napi_property_descriptor properties[] = {
        { "add", nullptr, Add, nullptr, nullptr, nullptr, napi_default, nullptr },
        { "runtimeMajor", nullptr, RuntimeMajor, nullptr, nullptr, nullptr, napi_default, nullptr }
    };
    napi_define_properties(env, exports, 2, properties);
    return exports;
}

static napi_module module = { 1, 0, nullptr, Init, "smoke", nullptr, {0} };
extern "C" __attribute__((constructor)) void RegisterSmoke() { napi_module_register(&module); }
