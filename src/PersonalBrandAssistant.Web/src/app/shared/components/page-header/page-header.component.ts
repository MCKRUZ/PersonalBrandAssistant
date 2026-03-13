import { Component, input } from '@angular/core';
import { ButtonModule } from 'primeng/button';

export interface PageAction {
  label: string;
  icon?: string;
  command: () => void;
}

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="page-header">
      <h1>{{ title() }}</h1>
      <div class="actions">
        @for (action of actions(); track action.label) {
          <p-button
            [label]="action.label"
            [icon]="action.icon ?? ''"
            (onClick)="action.command()"
          />
        }
      </div>
    </div>
  `,
  styles: `
    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 1.5rem;

      h1 {
        margin: 0;
        font-size: 1.5rem;
        font-weight: 600;
      }

      .actions {
        display: flex;
        gap: 0.5rem;
      }
    }
  `,
})
export class PageHeaderComponent {
  title = input.required<string>();
  actions = input<PageAction[]>([]);
}
