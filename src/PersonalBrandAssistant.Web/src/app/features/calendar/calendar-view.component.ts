import { Component, inject, OnInit, ViewChild } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { ButtonModule } from 'primeng/button';
import { MessageService } from 'primeng/api';
import { PageHeaderComponent } from '../../shared/components/page-header/page-header.component';
import { LoadingSpinnerComponent } from '../../shared/components/loading-spinner/loading-spinner.component';
import { CalendarGridComponent } from './components/calendar-grid.component';
import { SlotDetailDialogComponent } from './components/slot-detail-dialog.component';
import { CreateSeriesDialogComponent } from './components/create-series-dialog.component';
import { CreateSlotDialogComponent } from './components/create-slot-dialog.component';
import { CalendarStore } from './store/calendar.store';
import { CalendarService } from './services/calendar.service';
import { CalendarSlot } from '../../shared/models';

@Component({
  selector: 'app-calendar-view',
  standalone: true,
  imports: [
    CommonModule, ButtonModule, PageHeaderComponent, LoadingSpinnerComponent,
    CalendarGridComponent, SlotDetailDialogComponent, CreateSeriesDialogComponent,
    CreateSlotDialogComponent, DatePipe,
  ],
  template: `
    <app-slot-detail-dialog #slotDialog (assigned)="reloadSlots()" />
    <app-create-series-dialog #seriesDialog (created)="reloadSlots()" />
    <app-create-slot-dialog #slotCreateDialog (created)="reloadSlots()" />

    <app-page-header title="Calendar" />

    <div class="flex align-items-center gap-3 mb-3">
      <p-button icon="pi pi-chevron-left" [text]="true" (onClick)="prevMonth()" />
      <h3 class="m-0">{{ monthLabel }}</h3>
      <p-button icon="pi pi-chevron-right" [text]="true" (onClick)="nextMonth()" />
      <div class="flex-1"></div>
      <p-button label="New Series" icon="pi pi-list" severity="secondary" (onClick)="seriesDialog.open()" />
      <p-button label="New Slot" icon="pi pi-plus" severity="secondary" (onClick)="slotCreateDialog.open()" />
      <p-button label="Auto-Fill" icon="pi pi-sparkles" (onClick)="autoFill()" />
    </div>

    @if (store.loading()) {
      <app-loading-spinner message="Loading calendar..." />
    } @else {
      <app-calendar-grid
        [dateRange]="store.dateRange()"
        [slotsByDate]="store.slotsByDate()"
        (slotClicked)="onSlotClick($event)"
        (dayClicked)="slotCreateDialog.open($event)"
      />
    }
  `,
})
export class CalendarViewComponent implements OnInit {
  readonly store = inject(CalendarStore);
  private readonly calendarService = inject(CalendarService);
  private readonly messageService = inject(MessageService);

  @ViewChild('slotDialog') slotDialog!: SlotDetailDialogComponent;

  monthLabel = '';

  ngOnInit() {
    this.updateMonthLabel();
    this.store.loadSlots(this.store.dateRange());
  }

  prevMonth() {
    const range = this.store.navigateMonth(-1);
    this.updateMonthLabel();
    this.store.loadSlots(range);
  }

  nextMonth() {
    const range = this.store.navigateMonth(1);
    this.updateMonthLabel();
    this.store.loadSlots(range);
  }

  onSlotClick(slot: CalendarSlot) {
    this.slotDialog.open(slot);
  }

  autoFill() {
    const range = this.store.dateRange();
    this.calendarService.autoFill(range.from, range.to).subscribe({
      next: () => {
        this.messageService.add({ severity: 'success', summary: 'Auto-filled', detail: 'Calendar auto-filled' });
        this.reloadSlots();
      },
    });
  }

  reloadSlots() {
    this.store.loadSlots(this.store.dateRange());
  }

  private updateMonthLabel() {
    const d = new Date(this.store.dateRange().from);
    this.monthLabel = d.toLocaleString('default', { month: 'long', year: 'numeric' });
  }
}
