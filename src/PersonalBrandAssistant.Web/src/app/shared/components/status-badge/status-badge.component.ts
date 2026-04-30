import { Component, computed, input } from '@angular/core';
import { LowerCasePipe } from '@angular/common';

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [LowerCasePipe],
  template: `<span [class]="cssClasses()">{{ status() | lowercase }}</span>`,
})
export class StatusBadgeComponent {
  status = input.required<string>();
  cssClasses = computed(() => {
    const s = this.status().toLowerCase();
    return `status-badge status-${s}`;
  });
}
