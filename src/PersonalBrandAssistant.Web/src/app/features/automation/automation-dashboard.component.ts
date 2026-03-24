import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MessageService } from 'primeng/api';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { AutomationStore } from './store/automation.store';
import { AutomationRun, AutomationRunStatus } from '../../shared/models';

@Component({
  selector: 'app-automation-dashboard',
  standalone: true,
  imports: [
    CommonModule, DatePipe, TableModule, ButtonModule, Tag, Card, Dialog,
    PageHeaderComponent, LoadingSpinnerComponent,
  ],
  template: `
    <app-page-header title="Content Automation" />

    @if (store.loading()) {
      <app-loading-spinner message="Loading automation data..." />
    } @else {
      <!-- Config Panel -->
      @if (store.config(); as config) {
        <p-card styleClass="mb-4">
          <div class="flex flex-wrap gap-4 align-items-center">
            <div class="flex align-items-center gap-2">
              <i class="pi pi-clock text-primary"></i>
              <span class="font-semibold">Schedule:</span>
              <span>{{ config.cronExpression }} ({{ config.timeZone }})</span>
            </div>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-shield text-primary"></i>
              <span class="font-semibold">Mode:</span>
              <p-tag [value]="config.autonomyLevel" [severity]="config.autonomyLevel === 'Autonomous' ? 'success' : 'warn'" />
            </div>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-image text-primary"></i>
              <span class="font-semibold">Images:</span>
              <p-tag [value]="config.imageGeneration.enabled ? 'Enabled' : 'Disabled'"
                     [severity]="config.imageGeneration.enabled ? 'success' : 'danger'" />
            </div>
            <div class="flex align-items-center gap-2">
              <i class="pi pi-share-alt text-primary"></i>
              <span class="font-semibold">Platforms:</span>
              <span>{{ config.targetPlatforms.join(', ') }}</span>
            </div>
            <div class="ml-auto">
              <p-button
                label="Trigger Now"
                icon="pi pi-play"
                [loading]="store.triggering()"
                [disabled]="store.hasRunningPipeline()"
                (click)="onTrigger()"
                severity="success"
              />
            </div>
          </div>
        </p-card>
      }

      <!-- Error Banner -->
      @if (store.lastTriggerError(); as error) {
        <div class="p-message p-message-error mb-4 p-3 border-round">
          <i class="pi pi-exclamation-triangle mr-2"></i> {{ error }}
        </div>
      }

      <!-- Runs Table -->
      <p-table [value]="runsArray()" [paginator]="true" [rows]="10"
               styleClass="p-datatable-sm" [rowHover]="true"
               selectionMode="single">
        <ng-template #header>
          <tr>
            <th>Status</th>
            <th>Triggered</th>
            <th>Duration</th>
            <th>Platforms</th>
            <th>Error</th>
          </tr>
        </ng-template>
        <ng-template #body let-run>
          <tr (click)="openDetail(run)" class="cursor-pointer">
            <td>
              <p-tag [value]="run.status" [severity]="statusSeverity(run.status)" />
            </td>
            <td>{{ run.triggeredAt | date:'short' }}</td>
            <td>{{ run.durationMs > 0 ? (run.durationMs / 1000).toFixed(1) + 's' : '-' }}</td>
            <td>{{ run.platformVersionCount }}</td>
            <td class="text-overflow-ellipsis max-w-20rem">{{ run.errorDetails || '-' }}</td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="5" class="text-center p-4 text-color-secondary">No automation runs yet. Click "Trigger Now" to start.</td></tr>
        </ng-template>
      </p-table>

      <!-- Run Detail Dialog -->
      <p-dialog header="Run Details" [(visible)]="showDialog" [modal]="true"
                [style]="{ width: '600px' }" [dismissableMask]="true">
        @if (selectedRun) {
          <div class="flex flex-column gap-3">
            <div><span class="font-semibold">ID:</span> {{ selectedRun.id }}</div>
            <div><span class="font-semibold">Status:</span> <p-tag [value]="selectedRun.status" [severity]="statusSeverity(selectedRun.status)" class="ml-2" /></div>
            <div><span class="font-semibold">Triggered:</span> {{ selectedRun.triggeredAt | date:'medium' }}</div>
            <div><span class="font-semibold">Completed:</span> {{ selectedRun.completedAt ? (selectedRun.completedAt | date:'medium') : 'In progress' }}</div>
            <div><span class="font-semibold">Duration:</span> {{ selectedRun.durationMs > 0 ? (selectedRun.durationMs / 1000).toFixed(1) + 's' : '-' }}</div>
            <div><span class="font-semibold">Platforms:</span> {{ selectedRun.platformVersionCount }}</div>
            @if (selectedRun.selectionReasoning) {
              <div><span class="font-semibold">Topic Reasoning:</span><br/>{{ selectedRun.selectionReasoning }}</div>
            }
            @if (selectedRun.errorDetails) {
              <div class="p-3 border-round bg-red-50 text-red-700">
                <span class="font-semibold">Error:</span><br/>{{ selectedRun.errorDetails }}
              </div>
            }
          </div>
        }
      </p-dialog>
    }
  `,
})
export class AutomationDashboardComponent implements OnInit {
  readonly store = inject(AutomationStore);
  private readonly messageService = inject(MessageService);

  selectedRun: AutomationRun | null = null;
  showDialog = false;

  ngOnInit() {
    this.store.loadRuns();
    this.store.loadConfig();
  }

  runsArray(): AutomationRun[] {
    return [...this.store.runs()];
  }

  openDetail(run: AutomationRun) {
    this.selectedRun = run;
    this.showDialog = true;
  }

  onTrigger() {
    this.store.triggerRun();
    this.messageService.add({
      severity: 'info',
      summary: 'Pipeline Triggered',
      detail: 'Automation pipeline is running...',
    });
    // Reload runs after a delay to show the new run
    setTimeout(() => this.store.loadRuns(), 3000);
  }

  statusSeverity(status: AutomationRunStatus): 'success' | 'info' | 'warn' | 'danger' | 'secondary' {
    switch (status) {
      case 'Completed': return 'success';
      case 'Running': return 'info';
      case 'PartialFailure': return 'warn';
      case 'Failed': return 'danger';
      default: return 'secondary';
    }
  }
}
