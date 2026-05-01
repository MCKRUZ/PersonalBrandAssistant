import { Component, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { AgentExecution } from '../../../core/models/agent.model';

@Component({
  selector: 'app-history-tab',
  standalone: true,
  imports: [DecimalPipe],
  template: `
    <div class="history-list">
      @if (executions().length === 0) {
        <div class="empty-history">
          <p>No agent interactions yet</p>
        </div>
      } @else {
        @for (exec of executions(); track exec.id) {
          <div class="history-item">
            <span class="agent-badge" [class]="'status-' + exec.status.toLowerCase()">
              {{ exec.agentType }}
            </span>
            <div class="exec-details">
              <span class="exec-summary">{{ exec.summary }}</span>
              <span class="exec-meta">
                {{ (exec.inputTokens + exec.outputTokens) | number }} tokens
                · {{ relativeDate(exec.startedAt) }}
              </span>
            </div>
            <span class="exec-status" [class]="'status-' + exec.status.toLowerCase()">
              {{ exec.status }}
            </span>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    @use '../../../../styles/variables' as *;

    .history-list {
      display: flex;
      flex-direction: column;
    }

    .history-item {
      display: flex;
      align-items: center;
      gap: $space-3;
      padding: $space-3;
      border-bottom: 1px solid $surface-border;
    }

    .agent-badge {
      font-size: 0.7rem;
      font-weight: 600;
      text-transform: uppercase;
      padding: $space-1 $space-2;
      border-radius: 4px;
      background: $surface-hover;
      color: $text-secondary;
      white-space: nowrap;
    }

    .exec-details {
      flex: 1;
      display: flex;
      flex-direction: column;
      gap: 2px;
      min-width: 0;
    }

    .exec-summary {
      font-size: 0.8125rem;
      color: $text-primary;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .exec-meta {
      font-size: 0.7rem;
      color: $text-muted;
    }

    .exec-status {
      font-size: 0.7rem;
      font-weight: 500;
      text-transform: uppercase;

      &.status-completed { color: $score-success; }
      &.status-running { color: $score-warning; }
      &.status-failed { color: $status-failed; }
    }

    .empty-history {
      text-align: center;
      padding: $space-6;
      color: $text-muted;
      font-size: 0.875rem;

      p { margin: 0; }
    }
  `],
})
export class HistoryTabComponent {
  readonly executions = input.required<readonly AgentExecution[]>();

  relativeDate(dateStr: string): string {
    const diffMs = Date.now() - new Date(dateStr).getTime();
    const diffMin = Math.floor(diffMs / 60000);
    if (diffMin < 60) return `${diffMin}m ago`;
    const diffHr = Math.floor(diffMin / 60);
    if (diffHr < 24) return `${diffHr}h ago`;
    return `${Math.floor(diffHr / 24)}d ago`;
  }
}
