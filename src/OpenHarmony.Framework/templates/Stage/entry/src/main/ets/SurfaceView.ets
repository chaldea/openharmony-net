import platform from 'libopenharmony_framework.so';
import { inputMethod } from '@kit.IMEKit';
import { hilog } from '@kit.PerformanceAnalysisKit';

interface EditorState {
  Revision: number;
  Focused: boolean;
  Text: string;
  Cursor: number;
  InputType: number;
  ShowRequest: number;
}

@Component
export struct SurfaceView {
  private controller: inputMethod.InputMethodController = inputMethod.getController();
  private timer: number = -1;
  private attached: boolean = false;
  private busy: boolean = false;
  private alive: boolean = false;
  private revision: number = -1;
  private showRequest: number = -1;
  private inputType: number = -1;
  private insert = (text: string): void => { platform.text(text); };
  private deleteLeft = (length: number): void => { for (let i = 0; i < length; i++) platform.key(1); };
  private deleteRight = (length: number): void => { for (let i = 0; i < length; i++) platform.key(2); };
  private enter = (): void => { platform.key(7); };
  private move = (direction: inputMethod.Direction): void => {
    if (direction === inputMethod.Direction.CURSOR_LEFT) platform.key(3);
    if (direction === inputMethod.Direction.CURSOR_RIGHT) platform.key(4);
  };

  aboutToAppear(): void {
    this.alive = true;
    this.timer = setInterval(() => { this.syncEditor(); }, 50);
  }

  aboutToDisappear(): void {
    this.alive = false;
    clearInterval(this.timer);
    this.detach();
  }

  private async detach(): Promise<void> {
    if (!this.attached) return;
    this.attached = false;
    this.controller.off('insertText', this.insert);
    this.controller.off('deleteLeft', this.deleteLeft);
    this.controller.off('deleteRight', this.deleteRight);
    this.controller.off('sendFunctionKey', this.enter);
    this.controller.off('moveCursor', this.move);
    await this.controller.detach();
  }

  private async syncEditor(): Promise<void> {
    if (this.busy || !this.alive) return;
    this.busy = true;
    try {
      const json = platform.editor();
      if (!json) return;
      const state = JSON.parse(json) as EditorState;
      if (state.Revision === this.revision) return;
      if (!state.Focused) {
        await this.detach();
      } else {
        if (this.attached && this.inputType !== state.InputType) await this.detach();
        if (!this.attached) {
          let type = inputMethod.TextInputType.TEXT;
          if (state.InputType === 1) type = inputMethod.TextInputType.VISIBLE_PASSWORD;
          if (state.InputType === 2) type = inputMethod.TextInputType.NUMBER;
          if (state.InputType === 3) type = inputMethod.TextInputType.MULTILINE;
          await this.controller.attach(true, {
            inputAttribute: { textInputType: type, enterKeyType: inputMethod.EnterKeyType.NEWLINE }
          });
          this.attached = true;
          this.inputType = state.InputType;
          this.controller.on('insertText', this.insert);
          this.controller.on('deleteLeft', this.deleteLeft);
          this.controller.on('deleteRight', this.deleteRight);
          this.controller.on('sendFunctionKey', this.enter);
          this.controller.on('moveCursor', this.move);
          if (!this.alive) { await this.detach(); return; }
        } else if (this.showRequest !== state.ShowRequest) {
          await this.controller.showTextInput();
        }
        await this.controller.changeSelection(state.Text, state.Cursor, state.Cursor);
      }
      this.revision = state.Revision;
      this.showRequest = state.ShowRequest;
    } catch (error) {
      hilog.error(0x170, 'OpenHarmony', 'Input method: %{public}s', JSON.stringify(error));
    } finally { this.busy = false; }
  }

  build() {
    XComponent({ id: 'platform', type: XComponentType.SURFACE, libraryname: 'openharmony_framework' })
      .width('100%').height('100%')
      .onLoad(() => { platform.setDensity(this.getUIContext().vp2px(1)); })
      .onAreaChange(() => { platform.setDensity(this.getUIContext().vp2px(1)); })
      .focusable(true)
      .defaultFocus(true)
  }
}
