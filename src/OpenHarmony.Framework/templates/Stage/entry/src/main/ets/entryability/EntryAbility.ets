import { UIAbility, Want } from '@kit.AbilityKit';
import { window } from '@kit.ArkUI';
import platform from 'libopenharmony_framework.so';

export default class EntryAbility extends UIAbility {
  onCreate(want: Want): void { platform.configure('@ASSEMBLY_NAME@'); this.openRoute(want); }
  onNewWant(want: Want): void { this.openRoute(want); }
  onForeground(): void { platform.setActive(true); }
  onBackground(): void { platform.setActive(false); }

  private openRoute(want: Want): void {
    const route = want.parameters?.openharmonyRoute;
    if (typeof route === 'string' && route.startsWith('/') && !route.startsWith('//')) platform.navigate(route);
  }
  onWindowStageCreate(stage: window.WindowStage): void {
    stage.loadContent('pages/Index');
  }
}
