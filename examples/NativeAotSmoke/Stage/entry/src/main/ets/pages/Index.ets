import smoke from 'libsmoke.so';
import { hilog } from '@kit.PerformanceAnalysisKit';

@Entry
@Component
struct Index {
  @State result: number = 0;
  @State version: number = 0;
  @State runs: number = 0;

  runCheck(): void {
    this.result = smoke.add(19, 23);
    this.version = smoke.runtimeMajor();
    this.runs++;
    hilog.info(0x0001, 'DotNetSmoke', 'DOTNET_SMOKE result=%{public}d runtime=%{public}d runs=%{public}d',
      this.result, this.version, this.runs);
  }

  aboutToAppear(): void { this.runCheck(); }

  build() {
    Column({ space: 24 }) {
      Text('OpenHarmony + .NET').fontSize(28).fontWeight(FontWeight.Bold)
      Text(`NativeAOT runtime: .NET ${this.version}`).fontSize(20)
      Text(`19 + 23 = ${this.result}`).fontSize(36)
      Text(this.result === 42 && this.version === 10 ? '验证通过' : '验证失败').fontSize(24)
      Button('再次运行 C#').onClick(() => this.runCheck())
      Text(`已运行 ${this.runs} 次`).fontSize(16)
    }.width('100%').height('100%').justifyContent(FlexAlign.Center).padding(24)
  }
}
