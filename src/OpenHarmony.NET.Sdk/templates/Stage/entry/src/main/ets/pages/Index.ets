import dotnet from 'libdotnet.so';
import { hilog } from '@kit.PerformanceAnalysisKit';

@Entry
@Component
struct Index {
  @State result: number = 0;
  @State version: number = 0;
  @State runs: number = 0;

  runApplication(): void {
    this.result = dotnet.run();
    this.version = dotnet.runtimeMajor();
    this.runs++;
    hilog.info(0x0001, 'DotNetOhos', 'DOTNET_OHOS result=%{public}d runtime=%{public}d runs=%{public}d',
      this.result, this.version, this.runs);
  }

  aboutToAppear(): void { this.runApplication(); }

  build() {
    Column({ space: 24 }) {
      Text('OpenHarmony + .NET').fontSize(28).fontWeight(FontWeight.Bold)
      Text(`.NET ${this.version}`).fontSize(20)
      Text(`Result: ${this.result}`).fontSize(36)
      Button('Run again').onClick(() => this.runApplication())
      Text(`Runs: ${this.runs}`).fontSize(16)
    }.width('100%').height('100%').justifyContent(FlexAlign.Center).padding(24)
  }
}