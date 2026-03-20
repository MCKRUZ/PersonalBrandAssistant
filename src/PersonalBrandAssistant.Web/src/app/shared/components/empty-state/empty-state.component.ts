import { Component, input } from '@angular/core';

@Component({
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <div class="empty-state">
      <div class="empty-state__icon-ring">
        @if (icon()) {
          <i [class]="icon()"></i>
        }
      </div>
      <p class="empty-state__message">{{ message() }}</p>
      @if (hint()) {
        <p class="empty-state__hint">{{ hint() }}</p>
      }
    </div>
  `,
  styles: `
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 4rem 2rem;
      text-align: center;

      &__icon-ring {
        width: 72px;
        height: 72px;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        background: rgba(139, 92, 246, 0.06);
        border: 1px solid rgba(139, 92, 246, 0.15);
        margin-bottom: 1.25rem;

        i {
          font-size: 1.75rem;
          color: rgba(139, 92, 246, 0.5);
        }
      }

      &__message {
        font-size: 0.95rem;
        font-weight: 600;
        color: rgba(255, 255, 255, 0.5);
        margin: 0 0 0.35rem;
        letter-spacing: -0.01em;
      }

      &__hint {
        font-size: 0.8rem;
        color: rgba(255, 255, 255, 0.25);
        margin: 0;
      }
    }
  `,
})
export class EmptyStateComponent {
  message = input.required<string>();
  icon = input<string>('');
  hint = input<string>('');
}
