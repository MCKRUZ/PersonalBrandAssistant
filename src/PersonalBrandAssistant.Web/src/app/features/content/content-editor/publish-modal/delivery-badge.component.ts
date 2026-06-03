import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { PlatformMeta, deliveryBadge } from '../../models/platform-metadata';

@Component({
  selector: 'app-delivery-badge',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="delivery-badge" [class]="'delivery-badge--' + badge().variant">{{ badge().text }}</span>`,
  styles: [`
    .delivery-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-family: var(--font-mono);
      font-size: 11px;
      padding: 2px 8px;
      border-radius: var(--r-pill);
    }
    .delivery-badge--auto { background: var(--delivery-auto-bg); color: var(--delivery-auto-fg); }
    .delivery-badge--manual { background: var(--delivery-manual-bg); color: var(--delivery-manual-fg); }
    .delivery-badge--warn { background: var(--delivery-warn-bg); color: var(--delivery-warn-fg); }
  `],
})
export class DeliveryBadgeComponent {
  readonly meta = input.required<PlatformMeta>();
  readonly isConnected = input(false);
  readonly badge = computed(() => deliveryBadge(this.meta(), this.isConnected()));
}
