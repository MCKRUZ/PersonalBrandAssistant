import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Card } from 'primeng/card';
import { DatePicker } from 'primeng/datepicker';
import { ButtonModule } from 'primeng/button';
import { SettingsStore } from '../store/settings.store';

@Component({
  selector: 'app-usage-panel',
  standalone: true,
  imports: [CommonModule, FormsModule, Card, DatePicker, ButtonModule],
  template: `
    <p-card header="LLM Usage">
      <div class="flex align-items-center gap-2 mb-3">
        <p-datepicker [(ngModel)]="dateRange" selectionMode="range" [showIcon]="true" placeholder="Date range" styleClass="w-18rem" />
        <p-button icon="pi pi-search" (onClick)="loadUsage()" [disabled]="!dateRange || dateRange.length < 2" />
      </div>
      @if (store.usage(); as usage) {
        <div class="flex justify-content-between align-items-center">
          <span class="font-semibold">Total Cost</span>
          <span class="text-2xl font-bold">\${{ usage.totalCost | number:'1.4-4' }}</span>
        </div>
      }
    </p-card>
  `,
})
export class UsagePanelComponent implements OnInit {
  readonly store = inject(SettingsStore);
  dateRange: Date[] = [];

  ngOnInit() {
    const to = new Date();
    const from = new Date(to.getTime() - 30 * 86_400_000);
    this.dateRange = [from, to];
    this.loadUsage();
  }

  loadUsage() {
    if (this.dateRange.length >= 2) {
      this.store.loadUsage({
        from: this.dateRange[0].toISOString(),
        to: this.dateRange[1].toISOString(),
      });
    }
  }
}
