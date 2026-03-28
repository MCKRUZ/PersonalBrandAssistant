import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { DialogModule } from 'primeng/dialog';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { FormsModule } from '@angular/forms';
import { Tooltip } from 'primeng/tooltip';
import { SocialStore } from '../store/social.store';
import { SocialService } from '../services/social.service';
import { EngagementTask } from '../models/social.model';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { LoadingSpinnerComponent } from '../../../shared/components/loading-spinner/loading-spinner.component';
import { EngagementTaskFormComponent } from './engagement-task-form.component';
import { ExecutionHistoryComponent } from './execution-history.component';
import { KpiCardComponent } from '../../dashboard/components/kpi-card.component';

@Component({
  selector: 'app-engagement-task-list',
  standalone: true,
  imports: [
    CommonModule, TableModule, ButtonModule, TagModule, DialogModule,
    ToggleSwitchModule, FormsModule, Tooltip, EmptyStateComponent,
    LoadingSpinnerComponent, EngagementTaskFormComponent, ExecutionHistoryComponent,
    KpiCardComponent,
  ],
  template: `
    @if (store.loading()) {
      <app-loading-spinner />
    } @else if (!store.hasTasks()) {
      <app-empty-state
        icon="pi pi-users"
        title="No engagement tasks"
        message="Create your first engagement task to start automating community interactions."
      >
        <p-button
          label="Create Task"
          icon="pi pi-plus"
          (onClick)="showCreateDialog = true"
          pTooltip="Create a new automated engagement task"
        />
      </app-empty-state>
    } @else {
      @if (store.stats(); as stats) {
        <div class="kpi-row">
          <app-kpi-card label="Active Tasks" [value]="stats.activeTasks" icon="pi pi-bolt" />
          <app-kpi-card label="Total Executions" [value]="stats.totalExecutions" icon="pi pi-play" />
          <app-kpi-card label="Success Rate" [value]="stats.successRate + '%'" icon="pi pi-check-circle" />
          <app-kpi-card label="Total Actions" [value]="stats.totalActions" icon="pi pi-comments" />
        </div>
      }

      <div class="info-cards-row">
        <div class="info-card">
          <div class="info-card-header">
            <i class="pi pi-shield info-card-icon"></i>
            <span class="info-card-title">Safety Mode</span>
          </div>
          @if (store.safetyStatus(); as safety) {
            <p class="info-card-text">
              <strong>{{ safety.autonomyLevel }}</strong>
            </p>
            <p class="info-card-subtext">
              Auto-respond on {{ safety.autoRespondTaskCount }} of {{ safety.enabledTaskCount }} tasks
            </p>
          } @else {
            <p class="info-card-subtext">Loading safety status...</p>
          }
        </div>
        <div class="info-card">
          <div class="info-card-header">
            <i class="pi pi-compass info-card-icon"></i>
            <span class="info-card-title">Discovery</span>
          </div>
          @if (store.hasDiscovered()) {
            <p class="info-card-text">
              {{ store.opportunitySummary().count }} opportunities across {{ store.opportunitySummary().platformCount }} platforms
            </p>
            <p-button
              label="View Opportunities"
              [text]="true"
              size="small"
              (onClick)="store.setActiveTab('opportunities')"
            />
          } @else {
            <p class="info-card-subtext">Run discovery on the Opportunities tab to find engagement targets</p>
          }
        </div>
      </div>

      <div class="task-toolbar">
        <p-button
          label="Create Task"
          icon="pi pi-plus"
          (onClick)="showCreateDialog = true"
          pTooltip="Create a new automated engagement task"
        />
      </div>

      <p-table [value]="$any(store.tasks())" [rows]="10" [paginator]="store.tasks().length > 10" styleClass="p-datatable-sm">
        <ng-template #header>
          <tr>
            <th>Platform</th>
            <th>Type</th>
            <th>Schedule</th>
            <th>Mode</th>
            <th>Enabled</th>
            <th>Auto-Respond</th>
            <th>Last Run</th>
            <th>Next Run</th>
            <th>Actions</th>
          </tr>
        </ng-template>
        <ng-template #body let-task>
          <tr>
            <td>
              <p-tag [value]="task.platform" [severity]="getPlatformSeverity(task.platform)" />
            </td>
            <td>{{ task.taskType }}</td>
            <td><code>{{ task.cronExpression }}</code></td>
            <td>
              @if (task.schedulingMode === 'HumanLike') {
                <p-tag value="Human-like" severity="info" icon="pi pi-shield" />
              } @else {
                <p-tag value="Fixed" severity="secondary" />
              }
            </td>
            <td>
              <p-toggleswitch
                [ngModel]="task.isEnabled"
                (ngModelChange)="toggleEnabled(task, $event)"
                pTooltip="Enable for opportunity discovery and scheduling"
              />
            </td>
            <td>
              <p-toggleswitch
                [ngModel]="task.autoRespond"
                (ngModelChange)="toggleAutoRespond(task, $event)"
                pTooltip="Allow automatic posting without approval"
              />
            </td>
            <td>{{ task.lastExecutedAt ? (task.lastExecutedAt | date:'short') : 'Never' }}</td>
            <td>
              @if (task.nextExecutionAt) {
                {{ task.schedulingMode === 'HumanLike' ? '~' : '' }}{{ task.nextExecutionAt | date:'short' }}
              } @else {
                —
              }
            </td>
            <td>
              <div class="action-buttons">
                <p-button
                  icon="pi pi-play"
                  [text]="true"
                  severity="success"
                  pTooltip="Execute this task immediately"
                  [loading]="store.executing()"
                  (onClick)="runNow(task)"
                />
                <p-button
                  icon="pi pi-history"
                  [text]="true"
                  pTooltip="View past execution results"
                  (onClick)="showHistory(task)"
                />
                <p-button
                  icon="pi pi-trash"
                  [text]="true"
                  severity="danger"
                  pTooltip="Permanently delete this task"
                  (onClick)="deleteTask(task)"
                />
              </div>
            </td>
          </tr>
        </ng-template>
      </p-table>
    }

    <p-dialog
      header="Create Engagement Task"
      [(visible)]="showCreateDialog"
      [modal]="true"
      [style]="{width: '500px'}"
    >
      <app-engagement-task-form
        (saved)="onTaskCreated()"
        (cancelled)="showCreateDialog = false"
      />
    </p-dialog>

    <p-dialog
      header="Execution History"
      [(visible)]="showHistoryDialog"
      [modal]="true"
      [style]="{width: '700px'}"
    >
      <app-execution-history [executions]="store.selectedTaskHistory()" />
    </p-dialog>
  `,
  styles: [`
    .kpi-row {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 1rem;
      margin-bottom: 1rem;
    }
    .info-cards-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1rem;
      margin-bottom: 1rem;
    }
    .info-card {
      border: 1px solid var(--surface-200);
      border-radius: 8px;
      padding: 1rem;
    }
    .info-card-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
    }
    .info-card-icon {
      font-size: 1.25rem;
      color: var(--primary-color);
    }
    .info-card-title {
      font-weight: 600;
      font-size: 0.95rem;
    }
    .info-card-text {
      margin: 0 0 0.25rem 0;
      font-size: 0.9rem;
    }
    .info-card-subtext {
      margin: 0;
      font-size: 0.85rem;
      color: var(--text-color-secondary);
    }
    .task-toolbar {
      display: flex;
      justify-content: flex-end;
      margin-bottom: 1rem;
    }
    .action-buttons {
      display: flex;
      gap: 0.25rem;
    }
    code {
      font-size: 0.85rem;
      background: var(--surface-100);
      padding: 0.15rem 0.4rem;
      border-radius: 4px;
    }
  `],
})
export class EngagementTaskListComponent {
  readonly store = inject(SocialStore);
  private readonly service = inject(SocialService);

  showCreateDialog = false;
  showHistoryDialog = false;

  getPlatformSeverity(platform: string): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' {
    const map: Record<string, 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast'> = {
      Reddit: 'danger',
      TwitterX: 'info',
      LinkedIn: 'success',
      Instagram: 'warn',
      YouTube: 'danger',
    };
    return map[platform] ?? 'secondary';
  }

  toggleEnabled(task: EngagementTask, enabled: boolean) {
    this.service.updateTask(task.id, { isEnabled: enabled }).subscribe(() => {
      this.store.loadTasks();
    });
  }

  toggleAutoRespond(task: EngagementTask, autoRespond: boolean) {
    this.service.updateTask(task.id, { autoRespond }).subscribe(() => {
      this.store.loadTasks();
    });
  }

  runNow(task: EngagementTask) {
    this.store.executeTask(task.id);
    setTimeout(() => this.store.loadTasks(), 2000);
  }

  showHistory(task: EngagementTask) {
    this.store.loadHistory(task.id);
    this.showHistoryDialog = true;
  }

  deleteTask(task: EngagementTask) {
    this.service.deleteTask(task.id).subscribe(() => {
      this.store.loadTasks();
    });
  }

  onTaskCreated() {
    this.showCreateDialog = false;
    this.store.loadTasks();
  }
}
