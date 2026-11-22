#include "surface_host.h"
#include <ace/xcomponent/native_interface_xcomponent.h>
#include <napi/native_api.h>
#include <hilog/log.h>
#include <EGL/egl.h>
#include <GLES2/gl2.h>
#include <cmath>
#include <string>
#include <vector>
#include <dlfcn.h>

namespace {
const SurfaceCallbacks* managed = nullptr;
const SurfaceCallbacks* (*initialize)() = nullptr;
void* managedLibrary = nullptr;
EGLDisplay display = EGL_NO_DISPLAY;
EGLSurface surface = EGL_NO_SURFACE;
EGLContext context = EGL_NO_CONTEXT;
float density = 1;
bool ready = false;
bool active = true;
bool firstFrame = true;
void* nativeWindow = nullptr;
std::string pendingRoute;

void Log(const char* text) { OH_LOG_Print(LOG_APP, LOG_INFO, 0x170, "OpenHarmony", "%{public}s", text); }
void* GetProc(const char* name) { return reinterpret_cast<void*>(eglGetProcAddress(name)); }

void Release()
{
    if (display != EGL_NO_DISPLAY && context != EGL_NO_CONTEXT) {
        eglMakeCurrent(display, surface, surface, context);
        if (managed) managed->destroy();
        eglMakeCurrent(display, EGL_NO_SURFACE, EGL_NO_SURFACE, EGL_NO_CONTEXT);
    }
    ready = false;
    if (display != EGL_NO_DISPLAY) {
        if (surface != EGL_NO_SURFACE) eglDestroySurface(display, surface);
        if (context != EGL_NO_CONTEXT) eglDestroyContext(display, context);
        eglTerminate(display);
    }
    surface = EGL_NO_SURFACE;
    context = EGL_NO_CONTEXT;
    display = EGL_NO_DISPLAY;
    nativeWindow = nullptr;
}

void OnFrame(OH_NativeXComponent* component, uint64_t timestamp, uint64_t)
{
    if (!ready || !active) return;
    uint64_t width = 0, height = 0;
    if (OH_NativeXComponent_GetXComponentSize(component, nativeWindow, &width, &height) != OH_NATIVEXCOMPONENT_RESULT_SUCCESS) return;
    if (width == 0 || height == 0 || width > 16384 || height > 16384) return;
    if (!eglMakeCurrent(display, surface, surface, context)) { Log("OH_EGL_ERROR make current"); ready = false; return; }
    using ViewportProc = void (*)(GLint, GLint, GLsizei, GLsizei);
    auto viewport = reinterpret_cast<ViewportProc>(eglGetProcAddress("glViewport"));
    if (!viewport) { Log("OH_GL_ERROR missing glViewport"); ready = false; return; }
    viewport(0, 0, static_cast<GLsizei>(width), static_cast<GLsizei>(height));
    int result = managed->frame(static_cast<int32_t>(width), static_cast<int32_t>(height), density, timestamp / 1e9);
    if (result < 0) { ready = false; return; }
    if (result == 1) {
        if (!eglSwapBuffers(display, surface)) { Log("OH_EGL_ERROR swap buffers"); ready = false; return; }
        if (firstFrame) { Log("OH_HARMONY first_frame=1"); firstFrame = false; }
    }
}

void Created(OH_NativeXComponent* component, void* window)
{
    Release();
    nativeWindow = window;
    display = eglGetDisplay(EGL_DEFAULT_DISPLAY);
    if (!eglInitialize(display, nullptr, nullptr)) { Log("OH_EGL_ERROR initialize"); Release(); return; }
    EGLint attributes[] = {EGL_SURFACE_TYPE, EGL_WINDOW_BIT, EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT,
        EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8, EGL_STENCIL_SIZE, 8, EGL_NONE};
    EGLConfig config = nullptr;
    EGLint count = 0;
    if (!eglChooseConfig(display, attributes, &config, 1, &count) || count == 0) { Log("OH_EGL_ERROR config"); Release(); return; }
    EGLint contextAttributes[] = {EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE};
    context = eglCreateContext(display, config, EGL_NO_CONTEXT, contextAttributes);
    surface = eglCreateWindowSurface(display, config, reinterpret_cast<EGLNativeWindowType>(window), nullptr);
    if (context == EGL_NO_CONTEXT || surface == EGL_NO_SURFACE || !eglMakeCurrent(display, surface, surface, context)) {
        Log("OH_EGL_ERROR surface"); Release(); return;
    }
    if (!initialize) { Log("OH_HOST_ERROR configure assembly before creating the surface"); Release(); return; }
    managed = initialize();
    if (!managed || managed->version != 1 || managed->create(GetProc, Log) != 1) { Release(); return; }
    ready = true;
    firstFrame = true;
    managed->active(active ? 1 : 0);
    if (!pendingRoute.empty()) {
        managed->navigate(pendingRoute.data(), static_cast<int32_t>(pendingRoute.size()));
        pendingRoute.clear();
    }
    if (OH_NativeXComponent_RegisterOnFrameCallback(component, OnFrame) != OH_NATIVEXCOMPONENT_RESULT_SUCCESS) {
        Log("OH_XCOMPONENT_ERROR register frame"); Release();
    }
}
void Changed(OH_NativeXComponent*, void*) { /* Size is queried on every vsync, before the idle check. */ }
void Destroyed(OH_NativeXComponent* component, void*) { OH_NativeXComponent_UnregisterOnFrameCallback(component); Release(); }
void Touch(OH_NativeXComponent* component, void* window)
{
    if (!ready || !active) return;
    OH_NativeXComponent_TouchEvent event{};
    if (OH_NativeXComponent_GetTouchEvent(component, window, &event) == OH_NATIVEXCOMPONENT_RESULT_SUCCESS) {
        managed->touch(static_cast<int32_t>(event.type), event.id, event.x, event.y);
    }
}
OH_NativeXComponent_Callback callbacks{Created, Changed, Destroyed, Touch};

napi_value Undefined(napi_env env) { napi_value value; napi_get_undefined(env, &value); return value; }
bool Arg(napi_env env, napi_callback_info info, napi_value& value)
{
    size_t count = 1;
    return napi_get_cb_info(env, info, &count, &value, nullptr, nullptr) == napi_ok && count == 1;
}
napi_value Configure(napi_env env, napi_callback_info info)
{
    if (initialize) return Undefined(env);
    napi_value arg;
    size_t length = 0;
    if (!Arg(env, info, arg) || napi_get_value_string_utf8(env, arg, nullptr, 0, &length) != napi_ok || length > 200)
        return Undefined(env);
    std::vector<char> name(length + 1);
    napi_get_value_string_utf8(env, arg, name.data(), name.size(), &length);
    std::string assembly(name.data(), length);
    if (assembly.empty() || assembly.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-") != std::string::npos)
        return Undefined(env);
    managedLibrary = dlopen((assembly + ".so").c_str(), RTLD_NOW | RTLD_GLOBAL);
    if (managedLibrary) initialize = reinterpret_cast<const SurfaceCallbacks* (*)()>(dlsym(managedLibrary, "openharmony_initialize"));
    if (!initialize) { Log(dlerror()); napi_throw_error(env, nullptr, "Cannot load the managed SurfaceView entry point."); }
    return Undefined(env);
}
napi_value SetDensity(napi_env env, napi_callback_info info)
{
    napi_value arg;
    double value;
    if (Arg(env, info, arg) && napi_get_value_double(env, arg, &value) == napi_ok && std::isfinite(value) && value > 0)
        density = static_cast<float>(value);
    return Undefined(env);
}
napi_value SetActive(napi_env env, napi_callback_info info)
{
    napi_value arg;
    bool value;
    if (Arg(env, info, arg) && napi_get_value_bool(env, arg, &value) == napi_ok) {
        active = value;
        if (ready) managed->active(active ? 1 : 0);
    }
    return Undefined(env);
}
napi_value Back(napi_env env, napi_callback_info)
{
    napi_value value;
    napi_get_boolean(env, ready && managed->back() == 1, &value);
    return value;
}
napi_value Text(napi_env env, napi_callback_info info)
{
    napi_value arg;
    size_t length = 0;
    if (ready && Arg(env, info, arg) && napi_get_value_string_utf8(env, arg, nullptr, 0, &length) == napi_ok && length < 1024 * 1024) {
        std::vector<char> text(length + 1);
        if (napi_get_value_string_utf8(env, arg, text.data(), text.size(), &length) == napi_ok)
            managed->text(text.data(), static_cast<int32_t>(length));
    }
    return Undefined(env);
}
napi_value Key(napi_env env, napi_callback_info info)
{
    napi_value arg;
    int32_t key;
    if (ready && Arg(env, info, arg) && napi_get_value_int32(env, arg, &key) == napi_ok) managed->key(key);
    return Undefined(env);
}
napi_value Editor(napi_env env, napi_callback_info)
{
    std::string text;
    if (ready) {
        int32_t length = managed->editor(nullptr, 0);
        if (length > 0 && length < 1024 * 1024) {
            text.resize(length);
            managed->editor(text.data(), length);
        }
    }
    napi_value value;
    napi_create_string_utf8(env, text.data(), text.size(), &value);
    return value;
}
napi_value Navigate(napi_env env, napi_callback_info info)
{
    napi_value arg;
    size_t length = 0;
    if (Arg(env, info, arg) && napi_get_value_string_utf8(env, arg, nullptr, 0, &length) == napi_ok && length <= 4096) {
        std::vector<char> route(length + 1);
        if (napi_get_value_string_utf8(env, arg, route.data(), route.size(), &length) == napi_ok) {
            if (ready) managed->navigate(route.data(), static_cast<int32_t>(length));
            else pendingRoute.assign(route.data(), length);
        }
    }
    return Undefined(env);
}
napi_value Init(napi_env env, napi_value exports)
{
    napi_property_descriptor methods[] = {
        {"configure", nullptr, Configure, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setDensity", nullptr, SetDensity, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"setActive", nullptr, SetActive, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"back", nullptr, Back, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"text", nullptr, Text, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"key", nullptr, Key, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"editor", nullptr, Editor, nullptr, nullptr, nullptr, napi_default, nullptr},
        {"navigate", nullptr, Navigate, nullptr, nullptr, nullptr, napi_default, nullptr}
    };
    napi_define_properties(env, exports, sizeof(methods) / sizeof(methods[0]), methods);
    napi_value value;
    OH_NativeXComponent* component = nullptr;
    if (napi_get_named_property(env, exports, OH_NATIVE_XCOMPONENT_OBJ, &value) == napi_ok &&
        napi_unwrap(env, value, reinterpret_cast<void**>(&component)) == napi_ok && component)
        OH_NativeXComponent_RegisterCallback(component, &callbacks);
    return exports;
}
napi_module module{1, 0, nullptr, Init, "openharmony_framework", nullptr, {nullptr}};
}
extern "C" __attribute__((constructor)) void RegisterOpenHarmony() { napi_module_register(&module); }
