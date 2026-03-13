import { Component, input } from '@angular/core';
import { ProgressSpinner } from 'primeng/progressspinner';

@Component({
  selector: 'app-loading-spinner',
  standalone: true,
  imports: [ProgressSpinner],
  template: `
    <div class="spinner-container">
      <p-progressspinner />
      @if (message()) {
        <p>{{ message() }}</p>
      }
    </div>
  `,
  styles: `
    .spinner-container {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 2rem;

      p {
        margin-top: 1rem;
        color: var(--text-color-secondary);
      }
    }
  `,
})
export class LoadingSpinnerComponent {
  message = input<string>('');
}
