import { Component, computed, inject, input, output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { Timeline } from 'primeng/timeline';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { StatusBadgeComponent } from '../../../shared/components/status-badge/status-badge.component';
import { RelativeTimePipe } from '../../../shared/pipes/relative-time.pipe';
import { ContentStatus, WorkflowTransitionLog } from '../../../shared/models';

@Component({
  selector: 'app-content-workflow-panel',
  standalone: true,
  imports: [CommonModule, ButtonModule, Timeline, Tag, Card, StatusBadgeComponent, RelativeTimePipe],
  template: `
    <p-card header="Workflow">
      <div class="mb-3">
        <span class="font-semibold mr-2">Status:</span>
        <app-status-badge [status]="currentStatus()" />
      </div>

      @if (allowedTransitions().length > 0) {
        <div class="flex flex-wrap gap-2 mb-3">
          @for (transition of allowedTransitions(); track transition) {
            <p-button
              [label]="transition"
              [severity]="getTransitionSeverity(transition)"
              size="small"
              (onClick)="onTransition.emit(transition)"
            />
          }
        </div>
      }

      @if (auditLog().length > 0) {
        <h4>History</h4>
        <p-timeline [value]="mutableAuditLog()" layout="vertical">
          <ng-template #content let-event>
            <div class="text-sm">
              <app-status-badge [status]="event.fromStatus" />
              <i class="pi pi-arrow-right mx-2"></i>
              <app-status-badge [status]="event.toStatus" />
              @if (event.reason) {
                <div class="text-color-secondary mt-1">{{ event.reason }}</div>
              }
            </div>
          </ng-template>
          <ng-template #opposite let-event>
            <small class="text-color-secondary">{{ event.timestamp | relativeTime }}</small>
          </ng-template>
        </p-timeline>
      }
    </p-card>
  `,
})
export class ContentWorkflowPanelComponent {
  currentStatus = input.required<ContentStatus>();
  allowedTransitions = input<readonly ContentStatus[]>([]);
  auditLog = input<readonly WorkflowTransitionLog[]>([]);
  mutableAuditLog = computed(() => [...this.auditLog()]);
  onTransition = output<ContentStatus>();

  getTransitionSeverity(status: ContentStatus): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'Approved': return 'success';
      case 'Review': return 'info';
      case 'Scheduled': return 'warn';
      case 'Failed': return 'danger';
      case 'Archived': return 'secondary';
      default: return 'info';
    }
  }
}
