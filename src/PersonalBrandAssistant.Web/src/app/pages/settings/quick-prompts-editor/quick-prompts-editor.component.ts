import { Component, effect, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardModule } from 'primeng/card';
import { Select } from 'primeng/select';
import { InputTextModule } from 'primeng/inputtext';
import { ButtonModule } from 'primeng/button';

interface RouteOption {
  readonly label: string;
  readonly value: string;
}

const ROUTE_OPTIONS: RouteOption[] = [
  { label: 'Dashboard', value: 'dashboard' },
  { label: 'Content Editor', value: 'content-editor' },
  { label: 'Approval Queue', value: 'approval-queue' },
  { label: 'Calendar', value: 'calendar' },
  { label: 'Analytics', value: 'analytics' },
  { label: 'Settings', value: 'settings' },
];

@Component({
  selector: 'app-quick-prompts-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, CardModule, Select, InputTextModule, ButtonModule],
  template: `
    <p-card header="Quick Prompts">
      <div class="quick-prompts-editor">
        <p-select
          [(ngModel)]="selectedRoute"
          [options]="routeOptions"
          optionLabel="label"
          optionValue="value"
          placeholder="Select route context"
          (onChange)="onRouteChange()"
          styleClass="w-full"
        />

        @if (selectedRoute) {
          <div class="prompt-list">
            @for (prompt of currentPrompts(); track $index; let i = $index) {
              <div class="prompt-row">
                <input pInputText [ngModel]="prompt" (ngModelChange)="updatePrompt(i, $event)" class="flex-1" />
                <p-button icon="pi pi-times" [text]="true" severity="danger" (onClick)="removePrompt(i)" />
              </div>
            }
            <p-button label="Add Prompt" icon="pi pi-plus" [text]="true" (onClick)="addPrompt()" />
          </div>
        }

        <div class="actions">
          <p-button label="Save" icon="pi pi-check" (onClick)="save()" />
          <p-button label="Reset to Defaults" icon="pi pi-undo" [text]="true" severity="secondary" (onClick)="resetToDefaults()" />
        </div>
      </div>
    </p-card>
  `,
  styles: `
    .quick-prompts-editor { display: flex; flex-direction: column; gap: 1rem; }
    .prompt-list { display: flex; flex-direction: column; gap: 0.5rem; }
    .prompt-row { display: flex; gap: 0.5rem; align-items: center; }
    .actions { display: flex; gap: 0.5rem; padding-top: 0.5rem; }
    .flex-1 { flex: 1; }
  `,
})
export class QuickPromptsEditorComponent {
  readonly prompts = input<Record<string, string[]>>({});
  readonly promptsChange = output<Record<string, string[]>>();
  readonly promptsReset = output<void>();

  readonly routeOptions = ROUTE_OPTIONS;
  selectedRoute = '';
  readonly currentPrompts = signal<string[]>([]);
  private localPrompts: Record<string, string[]> = {};

  constructor() {
    effect(() => {
      const p = this.prompts();
      if (p && Object.keys(p).length > 0) {
        this.localPrompts = { ...p };
        if (this.selectedRoute) {
          this.currentPrompts.set([...(this.localPrompts[this.selectedRoute] ?? [])]);
        }
      }
    });
  }

  onRouteChange() {
    this.currentPrompts.set([...(this.localPrompts[this.selectedRoute] ?? [])]);
  }

  updatePrompt(index: number, value: string) {
    const updated = [...this.currentPrompts()];
    updated[index] = value;
    this.currentPrompts.set(updated);
    this.localPrompts[this.selectedRoute] = updated;
  }

  addPrompt() {
    this.currentPrompts.set([...this.currentPrompts(), '']);
    this.localPrompts[this.selectedRoute] = this.currentPrompts();
  }

  removePrompt(index: number) {
    const updated = this.currentPrompts().filter((_, i) => i !== index);
    this.currentPrompts.set(updated);
    this.localPrompts[this.selectedRoute] = updated;
  }

  save() {
    this.promptsChange.emit({ ...this.localPrompts });
  }

  resetToDefaults() {
    this.promptsReset.emit();
  }
}
