import { Component, input, computed } from '@angular/core';
import { Chip } from 'primeng/chip';
import { PlatformType } from '../../models';
import { PLATFORM_ICONS, PLATFORM_LABELS } from '../../utils/platform-icons';

@Component({
  selector: 'app-platform-chip',
  standalone: true,
  imports: [Chip],
  template: `<p-chip [label]="label()" [icon]="iconClass()" />`,
  styles: `:host { display: inline-block; }`,
})
export class PlatformChipComponent {
  platform = input.required<PlatformType>();
  label = computed(() => PLATFORM_LABELS[this.platform()]);
  iconClass = computed(() => PLATFORM_ICONS[this.platform()]);
}
