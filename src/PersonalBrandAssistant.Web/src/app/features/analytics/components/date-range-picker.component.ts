import { Component, output, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { DatePicker } from 'primeng/datepicker';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-date-range-picker',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePicker, ButtonModule],
  template: `
    <div class="flex align-items-center gap-2">
      <p-datepicker [(ngModel)]="dateRange" selectionMode="range" [showIcon]="true" placeholder="Select date range" styleClass="w-18rem" />
      <p-button icon="pi pi-search" (onClick)="apply()" [disabled]="!dateRange || dateRange.length < 2" />
    </div>
  `,
})
export class DateRangePickerComponent implements OnInit {
  dateRange: Date[] = [];
  rangeChanged = output<{ from: string; to: string }>();

  ngOnInit() {
    const to = new Date();
    const from = new Date(to.getTime() - 30 * 86_400_000);
    this.dateRange = [from, to];
  }

  apply() {
    if (this.dateRange.length >= 2) {
      this.rangeChanged.emit({
        from: this.dateRange[0].toISOString(),
        to: this.dateRange[1].toISOString(),
      });
    }
  }
}
