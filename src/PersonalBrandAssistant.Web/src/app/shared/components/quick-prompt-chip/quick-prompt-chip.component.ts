import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-quick-prompt-chip',
  standalone: true,
  template: `
    <button
      class="prompt-chip"
      [disabled]="disabled()"
      (click)="onClick()"
    >{{ label() }}</button>
  `,
  styles: `
    .prompt-chip {
      display: inline-flex;
      align-items: center;
      padding: 6px 12px;
      border-radius: 16px;
      border: 1px solid var(--p-surface-300);
      background: var(--p-surface-50);
      color: var(--p-surface-700);
      font-size: 12px;
      font-family: 'DM Sans', sans-serif;
      cursor: pointer;
      transition: all 150ms ease;
    }
    .prompt-chip:hover:not(:disabled) {
      background: var(--p-surface-200);
      border-color: #c87156;
      color: var(--p-surface-900);
    }
    .prompt-chip:disabled {
      opacity: 0.4;
      cursor: not-allowed;
    }
  `,
})
export class QuickPromptChipComponent {
  label = input.required<string>();
  prompt = input.required<string>();
  disabled = input(false);
  clicked = output<string>();

  onClick(): void {
    if (!this.disabled()) {
      this.clicked.emit(this.prompt());
    }
  }
}
