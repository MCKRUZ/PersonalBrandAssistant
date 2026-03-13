import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <div class="empty-state">
      @if (icon()) {
        <i [class]="icon()"></i>
      }
      <p>{{ message() }}</p>
    </div>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 3rem;
      color: var(--text-color-secondary);

      i {
        font-size: 3rem;
        margin-bottom: 1rem;
      }

      p {
        font-size: 1.1rem;
      }
    }
  `,
})
export class EmptyStateComponent {
  message = input.required<string>();
  icon = input<string>('');
}
