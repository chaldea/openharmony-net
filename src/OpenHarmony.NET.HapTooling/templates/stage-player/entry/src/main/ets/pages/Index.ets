import { common } from '@kit.AbilityKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import xengine from 'libxengine_host.so';

const DOMAIN: number = 0xD003500;
const TAG: string = 'XEngine.Index';

// XEngine HarmonyOS Stage index page.
//
// Owns the XComponent whose native surface is rendered by libxengine_host.so.
// The native module (xengine_host, built from entry/src/main/cpp/xengine/
// xengine_host_napi.cpp) registers XComponent surface/lifecycle/input callbacks
// and a per-frame OnFrame callback from C++; it also exposes three ArkTS-callable
// exports: configurePlayer / setResumed / destroyPlayer. This page:
//   1. Resolves the engine content root (the bundled rawfile xengine assets).
//   2. On aboutToAppear, asks the native module to configure the player. The
//      player attaches its Vulkan surface when the XComponent's
//      OnSurfaceCreated fires (handled in C++).
//   3. Forwards onShow/onHide to setResumed so the engine pauses/resumes its
//      frame thread with the page visibility.
//   4. On aboutToDisappear, destroys the player.

@Entry
@Component
struct Index {
  // The XComponent id MUST match the second argument to
  // OH_NativeXComponent_GetXComponentId expectations; libxengine_host registers
  // its callbacks against the component surfaced through the NAPI export named
  // OH_NATIVE_XCOMPONENT_OBJ, which hvigor wires from libraryname + id.
  private xComponentId: string = 'xengine_surface';
  private contentRoot: string = '';
  private configured: boolean = false;

  aboutToAppear(): void {
    this.contentRoot = this.resolveContentRoot();
    try {
      let ok: boolean = xengine.configurePlayer(this.contentRoot);
      this.configured = ok;
      hilog.info(DOMAIN, TAG, 'configurePlayer(%{public}s) -> %{public}s', this.contentRoot, ok.toString());
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'configurePlayer threw: %{public}s', (e as Error)?.message ?? String(e));
    }
  }

  aboutToDisappear(): void {
    try {
      xengine.destroyPlayer();
      hilog.info(DOMAIN, TAG, '%{public}s', 'destroyPlayer returned');
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'destroyPlayer threw: %{public}s', (e as Error)?.message ?? String(e));
    }
    this.configured = false;
  }

  onPageShow(): void {
    if (!this.configured) return;
    try {
      xengine.setResumed(true);
      hilog.info(DOMAIN, TAG, '%{public}s', 'setResumed(true)');
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'setResumed(true) threw: %{public}s', (e as Error)?.message ?? String(e));
    }
  }

  onPageHide(): void {
    if (!this.configured) return;
    try {
      xengine.setResumed(false);
      hilog.info(DOMAIN, TAG, '%{public}s', 'setResumed(false)');
    } catch (e) {
      hilog.error(DOMAIN, TAG, 'setResumed(false) threw: %{public}s', (e as Error)?.message ?? String(e));
    }
  }

  // The engine reads assets from the HAP's resfile tree. OHOS unpacks the
  // entry/src/main/resources/resfile/ directory into the app sandbox at install
  // time and exposes its absolute on-device path through ctx.resourceDir. Unlike
  // rawfile (which is only reachable via the ResourceManager NDK API), resfile
  // contents are real files on disk that System.IO.File/Directory can read.
  private resolveContentRoot(): string {
    let ctx = getContext(this) as common.UIAbilityContext;
    return ctx.resourceDir;
  }

  build() {
    Column() {
      XComponent({
        id: this.xComponentId,
        type: XComponentType.SURFACE,
        libraryname: 'xengine_host'
      })
        .onLoad(() => {
          hilog.info(DOMAIN, TAG, '%{public}s', 'XComponent onLoad (native surface ready)');
        })
        .width('100%')
        .height('100%')
    }
    .width('100%')
    .height('100%')
  }
}
