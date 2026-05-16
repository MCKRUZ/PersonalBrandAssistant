import { Component, computed, input, output } from '@angular/core';
import { FeedItem, FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import { RelativeTimePipe } from '../pipes/relative-time.pipe';

interface ActionButton {
  readonly label: string;
  readonly action: string;
}

interface PrimaryAction extends ActionButton {
  readonly severity: 'success' | 'info';
}

interface ActionConfig {
  readonly primary: PrimaryAction | null;
  readonly secondaries: readonly ActionButton[];
}

interface TypeConfig {
  readonly accentColor: string;
  readonly iconClass: string;
  readonly label: string;
}

@Component({
  selector: 'app-feed-card',
  standalone: true,
  imports: [RelativeTimePipe],
  template: `
    <div class="card"
         [class.is-read]="item().isRead"
         [style.border-left-color]="typeConfig().accentColor"
         data-testid="feed-card">
      <div class="card-header">
        <div class="card-header-left">
          <input type="checkbox"
                 class="card-checkbox"
                 [checked]="isSelected()"
                 (change)="onSelect()"
                 [attr.aria-label]="'Select ' + item().title"
                 data-testid="card-checkbox" />
          <i [class]="'pi ' + typeConfig().iconClass"
             [style.color]="typeConfig().accentColor"
             data-testid="type-icon"></i>
          <span class="type-label">{{ typeConfig().label }}</span>
          @if (showPriorityBadge()) {
            <span class="priority-badge"
                  [class.badge-high]="item().priority === priorityHigh"
                  [class.badge-urgent]="item().priority === priorityUrgent"
                  [class.pulse]="item().priority === priorityUrgent"
                  data-testid="priority-badge">
              {{ item().priority }}
            </span>
          }
        </div>
        <span class="timestamp">{{ item().createdAt | relativeTime }}</span>
      </div>

      <div class="card-body">
        <h3 class="card-title">{{ item().title }}</h3>
        <p class="card-summary">{{ item().summary }}</p>
      </div>

      <div class="card-actions">
        @if (actionConfig().primary; as primary) {
          <button type="button" class="btn btn-primary"
                  [class.btn-success]="primary.severity === 'success'"
                  [class.btn-info]="primary.severity === 'info'"
                  (click)="onAction(primary.action)"
                  data-testid="primary-action">
            {{ primary.label }}
          </button>
        }
        @for (sec of actionConfig().secondaries; track sec.action) {
          <button type="button" class="btn btn-secondary"
                  (click)="onAction(sec.action)"
                  [attr.data-testid]="'action-' + sec.action">
            {{ sec.label }}
          </button>
        }
        <button type="button" class="btn btn-dismiss"
                (click)="onAction('dismiss')"
                data-testid="action-dismiss">
          Dismiss
        </button>
      </div>
    </div>
  `,
  styles: [`
    .card {
      background: #161b22;
      border: 1px solid #30363d;
      border-left: 3px solid #30363d;
      border-radius: 8px;
      padding: 16px;
      transition: background 0.2s;
    }

    .card:hover { background: #1c2128; }
    .card.is-read { opacity: 0.7; }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 8px;
    }

    .card-header-left {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .card-checkbox {
      width: 16px;
      height: 16px;
      cursor: pointer;
      accent-color: #58a6ff;
    }

    .type-label {
      font-size: 12px;
      color: #8b949e;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .priority-badge {
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: 10px;
    }

    .badge-high {
      background: rgba(234, 179, 8, 0.15);
      color: #eab308;
    }

    .badge-urgent {
      background: rgba(248, 81, 73, 0.15);
      color: #f85149;
    }

    .timestamp {
      font-size: 12px;
      color: #484f58;
    }

    .card-body { margin-bottom: 12px; }

    .card-title {
      font-size: 15px;
      font-weight: 600;
      color: #f0f6fc;
      margin: 0 0 4px;
    }

    .card-summary {
      font-size: 13px;
      color: #8b949e;
      margin: 0;
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
    }

    .card-actions {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .btn {
      font-size: 12px;
      font-weight: 500;
      padding: 6px 12px;
      border-radius: 6px;
      border: 1px solid #30363d;
      cursor: pointer;
      transition: background 0.15s, border-color 0.15s;
      background: transparent;
      color: #c9d1d9;
    }

    .btn:hover { border-color: #8b949e; }

    .btn-success {
      background: rgba(35, 134, 54, 0.15);
      color: #3fb950;
      border-color: #238636;
    }
    .btn-success:hover { background: rgba(35, 134, 54, 0.3); }

    .btn-info {
      background: rgba(31, 111, 235, 0.15);
      color: #58a6ff;
      border-color: #1f6feb;
    }
    .btn-info:hover { background: rgba(31, 111, 235, 0.3); }

    .btn-dismiss {
      color: #484f58;
      margin-left: auto;
    }
    .btn-dismiss:hover { color: #f85149; border-color: #f85149; }

    @keyframes pulse {
      0%, 100% { opacity: 1; }
      50% { opacity: 0.5; }
    }

    .pulse { animation: pulse 2s ease-in-out infinite; }
  `]
})
export class FeedCardComponent {
  readonly item = input.required<FeedItem>();
  readonly selectedIds = input<string[]>([]);

  readonly action = output<{ id: string; action: string }>();
  readonly select = output<string>();

  protected readonly priorityHigh = FeedItemPriority.High;
  protected readonly priorityUrgent = FeedItemPriority.Urgent;

  protected readonly typeConfig = computed<TypeConfig>(() => FeedCardComponent.TYPE_MAP[this.item().type]);
  protected readonly actionConfig = computed<ActionConfig>(() => FeedCardComponent.ACTION_MAP[this.item().type]);
  protected readonly isSelected = computed(() => this.selectedIds().includes(this.item().id));
  protected readonly showPriorityBadge = computed(() => {
    const p = this.item().priority;
    return p === FeedItemPriority.High || p === FeedItemPriority.Urgent;
  });

  private static readonly TYPE_MAP: Readonly<Record<FeedItemType, TypeConfig>> = {
    [FeedItemType.AgentDraft]: { accentColor: '#3b82f6', iconClass: 'pi-bolt', label: 'Agent Draft' },
    [FeedItemType.TrendAlert]: { accentColor: '#f97316', iconClass: 'pi-chart-line', label: 'Trend Alert' },
    [FeedItemType.IdeaSuggestion]: { accentColor: '#a855f7', iconClass: 'pi-lightbulb', label: 'Idea Suggestion' },
    [FeedItemType.AnalyticsHighlight]: { accentColor: '#22c55e', iconClass: 'pi-chart-bar', label: 'Analytics' },
    [FeedItemType.ApprovalRequest]: { accentColor: '#eab308', iconClass: 'pi-check-circle', label: 'Approval' },
    [FeedItemType.SystemNotification]: { accentColor: '#6b7280', iconClass: 'pi-bell', label: 'System' },
  };

  private static readonly ACTION_MAP: Readonly<Record<FeedItemType, ActionConfig>> = {
    [FeedItemType.AgentDraft]: {
      primary: { label: 'Approve', action: 'approve', severity: 'success' },
      secondaries: [{ label: 'Edit', action: 'edit' }, { label: 'Schedule', action: 'schedule' }],
    },
    [FeedItemType.TrendAlert]: {
      primary: { label: 'View', action: 'view', severity: 'info' },
      secondaries: [],
    },
    [FeedItemType.IdeaSuggestion]: {
      primary: { label: 'Create Content', action: 'create-content', severity: 'info' },
      secondaries: [],
    },
    [FeedItemType.AnalyticsHighlight]: {
      primary: { label: 'View Report', action: 'view-report', severity: 'info' },
      secondaries: [],
    },
    [FeedItemType.ApprovalRequest]: {
      primary: { label: 'Approve', action: 'approve', severity: 'success' },
      secondaries: [{ label: 'Edit', action: 'edit' }],
    },
    [FeedItemType.SystemNotification]: {
      primary: null,
      secondaries: [],
    },
  };

  protected onAction(actionName: string): void {
    this.action.emit({ id: this.item().id, action: actionName });
  }

  protected onSelect(): void {
    this.select.emit(this.item().id);
  }
}
