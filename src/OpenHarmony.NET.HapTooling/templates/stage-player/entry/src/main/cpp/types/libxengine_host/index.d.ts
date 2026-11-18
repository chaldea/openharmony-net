// Type declarations for the xengine_host NAPI module. The native source lives in
// entry/src/main/cpp/xengine/xengine_host_napi.cpp and registers three exports:
//   configurePlayer(contentRoot: string): boolean
//   setResumed(resumed: boolean): boolean
//   destroyPlayer(): boolean
// The XComponent surface/lifecycle/input callbacks are registered from C++ via
// OH_NativeXComponent_RegisterCallback and are NOT exposed to ArkTS directly;
// ArkTS only drives engine start/resume/destroy and owns the XComponent node.
export const configurePlayer: (contentRoot: string) => boolean;
export const setResumed: (resumed: boolean) => boolean;
export const destroyPlayer: () => boolean;
