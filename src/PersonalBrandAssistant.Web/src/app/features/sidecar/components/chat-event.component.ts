import { Component, ChangeDetectionStrategy, input, signal } from '@angular/core';
import { ChatEvent, ChatEventType } from '../../../shared/models';

const EVENT_CONFIG: Record<ChatEventType, { icon: string; label: string; colorClass: string }> = {
  thinking: { icon: 'pi pi-spinner', label: 'Thinking', colorClass: 'event-thinking' },
  'file-edit': { icon: 'pi pi-pencil', label: 'File Edit', colorClass: 'event-file-edit' },
  'file-read': { icon: 'pi pi-eye', label: 'File Read', colorClass: 'event-file-read' },
  'bash-command': { icon: 'pi pi-terminal', label: 'Command', colorClass: 'event-bash' },
  'bash-output': { icon: 'pi pi-terminal', label: 'Output', colorClass: 'event-bash' },
  'tool-use': { icon: 'pi pi-wrench', label: 'Tool', colorClass: 'event-tool' },
  'tool-result': { icon: 'pi pi-wrench', label: 'Result', colorClass: 'event-tool' },
  summary: { icon: 'pi pi-check-circle', label: 'Summary', colorClass: 'event-summary' },
  error: { icon: 'pi pi-exclamation-triangle', label: 'Error', colorClass: 'event-error' },
  status: { icon: 'pi pi-info-circle', label: 'Status', colorClass: 'event-thinking' },
};

const TRUNCATE_LENGTH = 300;

@Component({
  selector: 'app-chat-event',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let cfg = config();
    <div class="chat-event" [class]="cfg.colorClass">
      <div class="event-header">
        <i [class]="cfg.icon"></i>
        <span class="event-label">{{ cfg.label }}</span>
      </div>
      <div class="event-content" [class.monospace]="isCode()">
        {{ displayContent() }}
        @if (isTruncatable() && !expanded()) {
          <button class="show-more" (click)="expanded.set(true)">Show more</button>
        }
        @if (isTruncatable() && expanded()) {
          <button class="show-more" (click)="expanded.set(false)">Show less</button>
        }
      </div>
    </div>
  `,
  styleUrl: './chat-event.component.scss',
})
export class ChatEventComponent {
  readonly event = input.required<ChatEvent>();
  readonly expanded = signal(false);

  config() {
    return EVENT_CONFIG[this.event().type] ?? EVENT_CONFIG['status'];
  }

  isCode(): boolean {
    const t = this.event().type;
    return t === 'bash-command' || t === 'bash-output' || t === 'tool-result' || t === 'file-edit' || t === 'file-read';
  }

  isTruncatable(): boolean {
    const t = this.event().type;
    return (t === 'bash-output' || t === 'tool-result') && this.event().content.length > TRUNCATE_LENGTH;
  }

  displayContent(): string {
    if (this.isTruncatable() && !this.expanded()) {
      return this.event().content.slice(0, TRUNCATE_LENGTH) + '...';
    }
    return this.event().content;
  }
}
