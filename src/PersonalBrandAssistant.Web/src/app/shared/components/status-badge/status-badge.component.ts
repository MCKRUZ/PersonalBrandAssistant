import { Component, computed, input } from '@angular/core';
import { Tag } from 'primeng/tag';

const statusSeverityMap: Record<string, 'secondary' | 'info' | 'success' | 'warn' | 'danger'> = {
  Draft: 'secondary',
  Review: 'info',
  Approved: 'success',
  Scheduled: 'warn',
  Publishing: 'warn',
  Published: 'success',
  Failed: 'danger',
  Archived: 'secondary',
};

@Component({
  selector: 'app-status-badge',
  standalone: true,
  imports: [Tag],
  template: `<p-tag [value]="status()" [severity]="severity()" />`,
})
export class StatusBadgeComponent {
  status = input.required<string>();
  severity = computed(() => statusSeverityMap[this.status()] ?? 'secondary');
}
