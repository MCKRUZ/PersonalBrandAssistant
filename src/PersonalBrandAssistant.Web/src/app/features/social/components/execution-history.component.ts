import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TagModule } from 'primeng/tag';
import { TimelineModule } from 'primeng/timeline';
import { EngagementExecution } from '../models/social.model';

@Component({
  selector: 'app-execution-history',
  standalone: true,
  imports: [CommonModule, TagModule, TimelineModule],
  template: `
    @if (executions().length === 0) {
      <p class="text-secondary">No execution history yet.</p>
    } @else {
      <div class="history-list">
        @for (exec of executions(); track exec.id) {
          <div class="execution-card">
            <div class="execution-header">
              <span class="execution-date">{{ exec.executedAt | date:'medium' }}</span>
              <p-tag
                [value]="exec.actionsSucceeded + '/' + exec.actionsAttempted + ' succeeded'"
                [severity]="exec.actionsSucceeded === exec.actionsAttempted ? 'success' : exec.actionsSucceeded > 0 ? 'warn' : 'danger'"
              />
            </div>
            @if (exec.errorMessage) {
              <p class="error-text">{{ exec.errorMessage }}</p>
            }
            @if (exec.actions.length > 0) {
              <div class="actions-list">
                @for (action of exec.actions; track action.id) {
                  <div class="action-item" [class.action-success]="action.succeeded" [class.action-failed]="!action.succeeded">
                    <div class="action-header">
                      <i [class]="action.succeeded ? 'pi pi-check-circle' : 'pi pi-times-circle'"></i>
                      <a [href]="action.targetUrl" target="_blank" rel="noopener">{{ action.targetUrl | slice:0:80 }}</a>
                    </div>
                    @if (action.generatedContent) {
                      <p class="generated-content">{{ action.generatedContent }}</p>
                    }
                    @if (action.errorMessage) {
                      <p class="error-text">{{ action.errorMessage }}</p>
                    }
                  </div>
                }
              </div>
            }
          </div>
        }
      </div>
    }
  `,
  styles: [`
    .history-list {
      display: flex;
      flex-direction: column;
      gap: 1rem;
    }
    .execution-card {
      border: 1px solid var(--surface-200);
      border-radius: 8px;
      padding: 1rem;
    }
    .execution-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 0.5rem;
    }
    .execution-date {
      font-weight: 500;
      font-size: 0.9rem;
    }
    .actions-list {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      margin-top: 0.75rem;
    }
    .action-item {
      padding: 0.5rem 0.75rem;
      border-radius: 6px;
      font-size: 0.85rem;
    }
    .action-success {
      background: var(--green-50);
      border-left: 3px solid var(--green-500);
    }
    .action-failed {
      background: var(--red-50);
      border-left: 3px solid var(--red-500);
    }
    .action-header {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .action-header a {
      color: var(--primary-color);
      text-decoration: none;
    }
    .generated-content {
      margin: 0.5rem 0 0;
      padding: 0.5rem;
      background: var(--surface-50);
      border-radius: 4px;
      font-style: italic;
      white-space: pre-wrap;
    }
    .error-text {
      color: var(--red-500);
      font-size: 0.85rem;
      margin: 0.25rem 0 0;
    }
    .text-secondary {
      color: var(--text-color-secondary);
      text-align: center;
      padding: 2rem;
    }
  `],
})
export class ExecutionHistoryComponent {
  executions = input.required<readonly EngagementExecution[]>();
}
