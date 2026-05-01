import { Component, inject, viewChild } from '@angular/core';
import { Button } from 'primeng/button';
import { SelectButton } from 'primeng/selectbutton';
import { FormsModule } from '@angular/forms';
import { Skeleton } from 'primeng/skeleton';
import { PlatformType } from '../../shared/models';
import { CalendarSlot } from '../../shared/models';
import { CalendarStore } from './calendar.store';
import { CalendarApiService } from './calendar-api.service';
import { CalendarWeekGridComponent } from './components/calendar-week-grid.component';
import { CalendarMonthGridComponent } from './components/calendar-month-grid.component';
import { SlotDetailDialogComponent } from './components/slot-detail-dialog.component';
import { CreateSlotDialogComponent } from './components/create-slot-dialog.component';
import { CreateSeriesDialogComponent } from './components/create-series-dialog.component';

@Component({
  selector: 'app-calendar',
  standalone: true,
  imports: [
    FormsModule, Button, SelectButton, Skeleton,
    CalendarWeekGridComponent, CalendarMonthGridComponent,
    SlotDetailDialogComponent, CreateSlotDialogComponent, CreateSeriesDialogComponent,
  ],
  providers: [CalendarStore, CalendarApiService],
  templateUrl: './calendar.component.html',
  styleUrl: './calendar.component.scss',
})
export class CalendarComponent {
  protected readonly store = inject(CalendarStore);

  protected readonly slotDetailDialog = viewChild(SlotDetailDialogComponent);
  protected readonly createSlotDialog = viewChild(CreateSlotDialogComponent);
  protected readonly createSeriesDialog = viewChild(CreateSeriesDialogComponent);

  protected readonly viewModeOptions = [
    { label: 'Week', value: 'week' },
    { label: 'Month', value: 'month' },
  ];

  protected readonly platforms: PlatformType[] = [
    'TwitterX', 'LinkedIn', 'Instagram', 'YouTube', 'Reddit', 'PersonalBlog', 'Substack',
  ];

  protected onViewModeChange(mode: string): void {
    this.store.setViewMode(mode as 'week' | 'month');
    this.store.loadSlots(this.store.dateRange());
  }

  protected onNavigate(offset: number): void {
    this.store.navigate(offset);
    this.store.loadSlots(this.store.dateRange());
  }

  protected onSlotClicked(slot: CalendarSlot): void {
    this.slotDetailDialog()?.open(slot);
  }

  protected onEmptySlotClicked(date: Date): void {
    this.createSlotDialog()?.open(date);
  }

  protected onAutoFill(): void {
    this.store.autoFill();
  }

  protected onSlotCreated(): void {
    this.store.loadSlots(this.store.dateRange());
  }

  protected onContentAssigned(): void {
    this.store.loadSlots(this.store.dateRange());
  }

  protected onSeriesCreated(): void {
    this.store.loadSlots(this.store.dateRange());
  }

  protected isActiveFilter(platform: PlatformType | null): boolean {
    return this.store.platformFilter() === platform;
  }
}
