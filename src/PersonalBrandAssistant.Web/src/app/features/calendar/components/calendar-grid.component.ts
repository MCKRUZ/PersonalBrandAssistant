import { Component, input, output, computed } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { Tag } from 'primeng/tag';
import { PlatformChipComponent } from '../../../shared/components/platform-chip/platform-chip.component';
import { CalendarSlot } from '../../../shared/models';

interface DayCell {
  readonly date: Date;
  readonly dateKey: string;
  readonly isCurrentMonth: boolean;
  readonly isToday: boolean;
  readonly slots: readonly CalendarSlot[];
}

@Component({
  selector: 'app-calendar-grid',
  standalone: true,
  imports: [CommonModule, Tag, PlatformChipComponent, DatePipe],
  template: `
    <div class="calendar-grid">
      <div class="grid-header">
        @for (day of weekDays; track day) {
          <div class="day-header">{{ day }}</div>
        }
      </div>
      <div class="grid-body">
        @for (week of weeks(); track $index) {
          <div class="week-row">
            @for (day of week; track day.dateKey) {
              <div
                class="day-cell"
                [class.other-month]="!day.isCurrentMonth"
                [class.today]="day.isToday"
                (click)="dayClicked.emit(day.date)"
              >
                <div class="day-number">{{ day.date.getDate() }}</div>
                <div class="day-slots">
                  @for (slot of day.slots; track slot.id) {
                    <div class="slot-chip" (click)="slotClicked.emit(slot); $event.stopPropagation()">
                      <app-platform-chip [platform]="slot.platform" />
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: `
    .calendar-grid { border: 1px solid var(--p-surface-200); border-radius: 8px; overflow: hidden; }
    .grid-header { display: grid; grid-template-columns: repeat(7, 1fr); background: var(--p-surface-100); }
    .day-header { padding: 0.75rem; text-align: center; font-weight: 600; font-size: 0.85rem; }
    .week-row { display: grid; grid-template-columns: repeat(7, 1fr); }
    .day-cell {
      min-height: 100px; padding: 0.5rem; border: 1px solid var(--p-surface-100);
      cursor: pointer; transition: background 0.2s;
    }
    .day-cell:hover { background: var(--p-surface-50); }
    .day-cell.other-month { opacity: 0.4; }
    .day-cell.today { background: var(--p-primary-50); }
    .day-number { font-weight: 600; font-size: 0.85rem; margin-bottom: 0.25rem; }
    .day-slots { display: flex; flex-direction: column; gap: 2px; }
    .slot-chip { font-size: 0.75rem; }
  `,
})
export class CalendarGridComponent {
  dateRange = input.required<{ from: string; to: string }>();
  slotsByDate = input.required<Map<string, CalendarSlot[]>>();

  slotClicked = output<CalendarSlot>();
  dayClicked = output<Date>();

  readonly weekDays = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

  weeks = computed(() => {
    const range = this.dateRange();
    const start = new Date(range.from);
    const year = start.getFullYear();
    const month = start.getMonth();
    const firstDay = new Date(year, month, 1);
    const lastDay = new Date(year, month + 1, 0);
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    const weeks: DayCell[][] = [];
    let current = new Date(firstDay);
    current.setDate(current.getDate() - current.getDay());

    while (current <= lastDay || current.getDay() !== 0) {
      const week: DayCell[] = [];
      for (let i = 0; i < 7; i++) {
        const date = new Date(current);
        const dateKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
        week.push({
          date,
          dateKey,
          isCurrentMonth: date.getMonth() === month,
          isToday: date.getTime() === today.getTime(),
          slots: this.slotsByDate().get(dateKey) ?? [],
        });
        current.setDate(current.getDate() + 1);
      }
      weeks.push(week);
      if (current.getMonth() !== month && current.getDay() === 0) break;
    }
    return weeks;
  });
}
