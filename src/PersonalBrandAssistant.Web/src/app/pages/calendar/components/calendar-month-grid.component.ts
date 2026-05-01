import { Component, input, output, computed } from '@angular/core';
import { SlotCardComponent } from './slot-card.component';
import { CalendarSlot } from '../../../shared/models';

interface DayCell {
  readonly date: Date;
  readonly dateKey: string;
  readonly isCurrentMonth: boolean;
  readonly isToday: boolean;
  readonly slots: readonly CalendarSlot[];
}

@Component({
  selector: 'app-calendar-month-grid',
  standalone: true,
  imports: [SlotCardComponent],
  template: `
    <div class="month-grid">
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
                    <app-slot-card [slot]="slot" (click)="slotClicked.emit(slot); $event.stopPropagation()" />
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
    .month-grid { border: 1px solid var(--p-surface-700); border-radius: 8px; overflow: hidden; }
    .grid-header {
      display: grid;
      grid-template-columns: repeat(7, 1fr);
      background: var(--p-surface-800);
    }
    .day-header {
      padding: 0.75rem;
      text-align: center;
      font-weight: 600;
      font-size: 0.85rem;
      font-family: 'DM Sans', sans-serif;
      color: var(--p-text-muted-color);
    }
    .week-row { display: grid; grid-template-columns: repeat(7, 1fr); }
    .day-cell {
      min-height: 100px;
      padding: 0.5rem;
      border: 1px solid var(--p-surface-700);
      background: var(--p-surface-900);
      cursor: pointer;
      transition: background 0.2s;
    }
    .day-cell:hover { background: var(--p-surface-800); }
    .day-cell.other-month { opacity: 0.3; }
    .day-cell.today { background: color-mix(in srgb, var(--p-primary-color) 10%, var(--p-surface-900)); }
    .day-number { font-weight: 600; font-size: 0.85rem; margin-bottom: 0.25rem; }
    .day-slots { display: flex; flex-direction: column; gap: 2px; }
  `,
})
export class CalendarMonthGridComponent {
  dateRange = input.required<{ from: string; to: string }>();
  slotsByDate = input.required<Map<string, CalendarSlot[]>>();

  slotClicked = output<CalendarSlot>();
  dayClicked = output<Date>();

  readonly weekDays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

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
    const current = new Date(firstDay);
    const dayOffset = current.getDay() === 0 ? 6 : current.getDay() - 1;
    current.setDate(current.getDate() - dayOffset);

    while (current <= lastDay || current.getDay() !== 1) {
      const week: DayCell[] = [];
      for (let i = 0; i < 7; i++) {
        const date = new Date(current);
        const dateKey = `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
        const d = new Date(date);
        d.setHours(0, 0, 0, 0);
        week.push({
          date,
          dateKey,
          isCurrentMonth: date.getMonth() === month,
          isToday: d.getTime() === today.getTime(),
          slots: this.slotsByDate().get(dateKey) ?? [],
        });
        current.setDate(current.getDate() + 1);
      }
      weeks.push(week);
      if (current.getMonth() !== month && (current.getDay() === 1 || current.getDay() === 0)) break;
    }
    return weeks;
  });
}
