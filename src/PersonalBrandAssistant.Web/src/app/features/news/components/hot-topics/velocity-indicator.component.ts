import { Component, input, computed } from '@angular/core';

@Component({
  selector: 'app-velocity-indicator',
  standalone: true,
  template: `
    <span class="velocity" [class]="velocity()">
      <i [class]="iconClass()"></i>
      {{ label() }}
    </span>
  `,
  styles: `
    .velocity {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      font-size: 0.8rem;
      font-weight: 600;

      &.rising {
        color: #22c55e;
      }

      &.stable {
        color: #6b7280;
      }

      &.falling {
        color: #ef4444;
      }
    }
  `,
})
export class VelocityIndicatorComponent {
  velocity = input.required<'rising' | 'stable' | 'falling'>();
  itemCount = input(0);

  readonly iconClass = computed(() => {
    switch (this.velocity()) {
      case 'rising': return 'pi pi-arrow-up';
      case 'falling': return 'pi pi-arrow-down';
      default: return 'pi pi-minus';
    }
  });

  readonly label = computed(() => {
    switch (this.velocity()) {
      case 'rising': return `+${this.itemCount()} items`;
      case 'falling': return 'Declining';
      default: return 'Stable';
    }
  });
}
