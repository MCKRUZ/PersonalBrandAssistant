import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <div class="empty-state">
      <i [class]="icon()" class="empty-state__icon"></i>
      <p class="empty-state__message">{{ message() }}</p>
    </div>
  `,
  styles: [`
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 3rem 1rem;
      gap: 1rem;
      color: var(--p-text-muted-color, #8b949e);
    }
    .empty-state__icon {
      font-size: 2.5rem;
      opacity: 0.5;
    }
    .empty-state__message {
      font-size: 0.95rem;
      margin: 0;
      text-align: center;
    }
  `],
})
export class EmptyStateComponent {
  message = input('');
  icon = input('pi pi-info-circle');
}
