import { Component, inject, OnInit } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MessageService, ConfirmationService } from 'primeng/api';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { Tag } from 'primeng/tag';
import { Card } from 'primeng/card';
import { Dialog } from 'primeng/dialog';
import { ConfirmDialog } from 'primeng/confirmdialog';
import { Tooltip } from 'primeng/tooltip';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { AutomationStore } from './store/automation.store';
import { AutomationRun, AutomationRunStatus } from '../../shared/models';

@Component({
  selector: 'app-automation-dashboard',
  standalone: true,
  imports: [
    CommonModule, DatePipe, TableModule, ButtonModule, Tag, Card, Dialog,
    ConfirmDialog, Tooltip, PageHeaderComponent, LoadingSpinnerComponent,
  ],
  providers: [ConfirmationService],
  template: `
    <app-page-header title="Content Automation" />
    <p-confirmDialog />

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
              <span [pTooltip]="config.cronExpression">{{ cronToHuman(config.cronExpression, config.timeZone) }}</span>
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
            <div class="ml-auto flex gap-2">
              <p-button
                label="Clear History"
                icon="pi pi-trash"
                [text]="true" size="small"
                severity="danger"
                [disabled]="store.runs().length === 0"
                (click)="onClearRuns($event)"
              />
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
            <th style="width: 3rem;"></th>
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
            <td>
              @if (run.status !== 'Running') {
                <p-button
                  icon="pi pi-times"
                  [text]="true" [rounded]="true" size="small"
                  severity="danger"
                  pTooltip="Delete run"
                  (click)="onDeleteRun($event, run)"
                />
              }
            </td>
          </tr>
        </ng-template>
        <ng-template #emptymessage>
          <tr><td colspan="6" class="text-center p-4 text-color-secondary">No automation runs yet. Click "Trigger Now" to start.</td></tr>
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
  private readonly confirmationService = inject(ConfirmationService);

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
    setTimeout(() => this.store.loadRuns(), 3000);
  }

  onDeleteRun(event: Event, run: AutomationRun) {
    event.stopPropagation();
    this.store.deleteRun(run.id);
  }

  onClearRuns(event: Event) {
    event.stopPropagation();
    this.confirmationService.confirm({
      message: 'Delete all completed and failed runs?',
      header: 'Clear Run History',
      icon: 'pi pi-trash',
      acceptButtonStyleClass: 'p-button-danger',
      accept: () => {
        this.store.clearRuns();
        this.messageService.add({
          severity: 'info',
          summary: 'History Cleared',
          detail: 'Old runs have been removed.',
        });
      },
    });
  }

  cronToHuman(cron: string, timeZone: string): string {
    const parts = cron.split(' ');
    if (parts.length !== 5) return `${cron} (${timeZone})`;

    const [minute, hour, dom, month, dow] = parts;
    const timePart = this.formatTime(hour, minute);
    const dayPart = this.formatDays(dow, dom, month);

    return `${dayPart} at ${timePart} (${this.formatTimeZone(timeZone)})`;
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

  private formatTime(hour: string, minute: string): string {
    if (hour === '*') return `every hour at :${minute.padStart(2, '0')}`;
    const h = parseInt(hour, 10);
    const m = minute.padStart(2, '0');
    const period = h >= 12 ? 'PM' : 'AM';
    const h12 = h === 0 ? 12 : h > 12 ? h - 12 : h;
    return `${h12}:${m} ${period}`;
  }

  private formatDays(dow: string, dom: string, month: string): string {
    if (dow === '*' && dom === '*') return 'Every day';
    if (dow === '*') return `Day ${dom}`;

    const dayMap: Record<string, string> = {
      '0': 'Sun', '1': 'Mon', '2': 'Tue', '3': 'Wed',
      '4': 'Thu', '5': 'Fri', '6': 'Sat', '7': 'Sun',
    };

    const rangeMap: Record<string, string> = {
      '1-5': 'Weekdays',
      '0,6': 'Weekends',
      '6,0': 'Weekends',
      '*': 'Every day',
    };

    if (rangeMap[dow]) return rangeMap[dow];

    // Handle ranges like "1-3"
    if (dow.includes('-')) {
      const [start, end] = dow.split('-');
      return `${dayMap[start] ?? start} - ${dayMap[end] ?? end}`;
    }

    // Handle lists like "1,3,5"
    if (dow.includes(',')) {
      return dow.split(',').map(d => dayMap[d.trim()] ?? d).join(', ');
    }

    return dayMap[dow] ?? dow;
  }

  private formatTimeZone(tz: string): string {
    // Shorten common IANA timezone names
    const short: Record<string, string> = {
      'America/New_York': 'ET',
      'America/Chicago': 'CT',
      'America/Denver': 'MT',
      'America/Los_Angeles': 'PT',
      'UTC': 'UTC',
    };
    return short[tz] ?? tz;
  }
}
