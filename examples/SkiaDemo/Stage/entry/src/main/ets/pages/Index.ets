import skia from 'libsmoke.so';
import { image } from '@kit.ImageKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

@Entry
@Component
struct Index {
  @State bitmap: image.PixelMap | undefined = undefined;
  @State status: string = '正在渲染…';
  private glyphs: number = 0;

  async render(): Promise<void> {
    try {
      const pixels = new ArrayBuffer(640 * 800 * 4);
      this.glyphs = skia.render(pixels);
      this.bitmap = await image.createPixelMap(pixels, {
        size: { width: 640, height: 800 },
        srcPixelFormat: image.PixelMapFormat.RGBA_8888,
        pixelFormat: image.PixelMapFormat.RGBA_8888,
        alphaType: image.AlphaType.OPAQUE,
        editable: false
      });
      this.status = 'Skia 绘图 · HarfBuzz 文字整形';
    } catch (error) {
      this.status = '渲染失败，请查看日志';
      hilog.error(0x0001, 'SkiaDemo', 'SKIA_DEMO failed %{public}s', String(error));
    }
  }

  aboutToAppear(): void { this.render(); }
  aboutToDisappear(): void { this.bitmap?.release(); }

  build() {
    Column({ space: 24 }) {
      Text('HarmonyOS + SkiaSharp').fontSize(24).fontColor('#F9FAFB').fontWeight(FontWeight.Bold)
      if (this.bitmap) {
        Image(this.bitmap).width('100%').aspectRatio(0.8).objectFit(ImageFit.Contain)
          .onComplete(() => {
            hilog.info(0x0001, 'SkiaDemo',
              'SKIA_DEMO rendered=1 pixels=640x800 glyphs=%{public}d', this.glyphs);
          })
      }
      Text(this.status).fontSize(16).fontColor('#98A2B3')
    }.width('100%').height('100%').justifyContent(FlexAlign.Center).padding(24).backgroundColor('#101828')
  }
}
