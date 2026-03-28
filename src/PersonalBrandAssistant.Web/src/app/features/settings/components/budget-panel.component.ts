import { Component, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Card } from 'primeng/card';
import { ProgressBar } from 'primeng/progressbar';
import { Tag } from 'primeng/tag';
import { AgentBudget } from '../../../shared/models';

@Component({
  selector: 'app-budget-panel',
  standalone: true,
  imports: [CommonModule, Card, ProgressBar, Tag],
  template: `
    <p-card header="Budget">
      @if (budget()) {
        <div class="flex flex-column gap-3">
          <div class="flex justify-content-between align-items-center">
            <span class="font-semibold">Remaining</span>
            <span class="text-2xl font-bold">\${{ budget()!.budgetRemaining | number:'1.2-2' }}</span>
          </div>
          <p-progressbar
            [value]="Math.max(0, budget()!.budgetRemaining)"
            [showValue]="false"
            [style]="{ height: '10px' }"
          />
          @if (budget()!.isOverBudget) {
            <p-tag value="Over Budget" severity="danger" />
          }
        </div>
      } @else {
        <div class="text-color-secondary">Budget data unavailable</div>
      }
    </p-card>
  `,
})
export class BudgetPanelComponent {
  budget = input<AgentBudget>();
  readonly Math = Math;
}
