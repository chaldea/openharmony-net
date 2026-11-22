import platform from 'libopenharmony_framework.so';
import { SurfaceView } from '../SurfaceView';

@Entry
@Component
struct Index {
  onPageShow(): void { platform.setActive(true); }
  onPageHide(): void { platform.setActive(false); }
  onBackPress(): boolean { return platform.back(); }

  build() {
    Column() {
      SurfaceView()
    }.width('100%').height('100%').backgroundColor('#ffffff')
  }
}
