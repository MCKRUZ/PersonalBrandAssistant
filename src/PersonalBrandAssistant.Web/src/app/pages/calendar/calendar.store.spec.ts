import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { CalendarStore, getWeekRange, getMonthRange } from './calendar.store';
import { CalendarApiService } from './calendar-api.service';
import { CalendarSlot } from '../../shared/models';

function makeSlot(overrides: Partial<CalendarSlot> = {}): CalendarSlot {
  return {
    id: 'slot-1',
    scheduledAt: '2026-05-01T10:00:00Z',
    platform: 'LinkedIn',
    status: 'Open',
    isOverride: false,
    createdAt: '2026-04-30T00:00:00Z',
    updatedAt: '2026-04-30T00:00:00Z',
    ...overrides,
  };
}

describe('CalendarStore', () => {
  let store: InstanceType<typeof CalendarStore>;
  let apiSpy: jasmine.SpyObj<CalendarApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('CalendarApiService', ['getSlots', 'createSlot', 'assignContent', 'autoFill']);
    apiSpy.getSlots.and.returnValue(of([]));

    TestBed.configureTestingModule({
      providers: [
        CalendarStore,
        { provide: CalendarApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(CalendarStore);
  });

  it('should have viewMode default to week', () => {
    expect(store.viewMode()).toBe('week');
  });

  it('should load slots from API with from/to params', () => {
    const slots = [makeSlot(), makeSlot({ id: 'slot-2' }), makeSlot({ id: 'slot-3' })];
    apiSpy.getSlots.and.returnValue(of(slots));

    store.loadSlots({ from: '2026-05-01', to: '2026-05-07' });

    expect(apiSpy.getSlots).toHaveBeenCalledWith('2026-05-01', '2026-05-07');
    expect(store.slots().length).toBe(3);
    expect(store.loading()).toBe(false);
  });

  it('should toggle viewMode between week and month', () => {
    store.setViewMode('month');
    expect(store.viewMode()).toBe('month');

    store.setViewMode('week');
    expect(store.viewMode()).toBe('week');
  });

  it('should group slots by date in slotsByDate computed', () => {
    const slots = [
      makeSlot({ id: '1', scheduledAt: '2026-05-01T10:00:00Z' }),
      makeSlot({ id: '2', scheduledAt: '2026-05-01T14:00:00Z' }),
      makeSlot({ id: '3', scheduledAt: '2026-05-02T09:00:00Z' }),
    ];
    apiSpy.getSlots.and.returnValue(of(slots));
    store.loadSlots({ from: '2026-05-01', to: '2026-05-07' });

    const map = store.slotsByDate();
    expect(map.size).toBe(2);
    expect(map.get('2026-05-01')?.length).toBe(2);
    expect(map.get('2026-05-02')?.length).toBe(1);
  });

  it('should filter slots by platform client-side', () => {
    const slots = [
      makeSlot({ id: '1', platform: 'LinkedIn' }),
      makeSlot({ id: '2', platform: 'TwitterX' }),
      makeSlot({ id: '3', platform: 'LinkedIn' }),
    ];
    apiSpy.getSlots.and.returnValue(of(slots));
    store.loadSlots({ from: '2026-05-01', to: '2026-05-07' });

    store.filterByPlatform('LinkedIn');
    expect(store.filteredSlots().length).toBe(2);
    expect(store.filteredSlots().every(s => s.platform === 'LinkedIn')).toBe(true);
  });

  it('should navigate forward by 7 days in week mode', () => {
    const initialFrom = new Date(store.dateRange().from);
    store.navigate(1);
    const newFrom = new Date(store.dateRange().from);
    const diff = (newFrom.getTime() - initialFrom.getTime()) / (1000 * 60 * 60 * 24);
    expect(diff).toBe(7);
  });

  it('should navigate forward by 1 month in month mode', () => {
    store.setViewMode('month');
    const initialMonth = new Date(store.dateRange().from).getMonth();
    store.navigate(1);
    const newMonth = new Date(store.dateRange().from).getMonth();
    expect((newMonth - initialMonth + 12) % 12).toBe(1);
  });
});

describe('getWeekRange', () => {
  it('should return Monday-to-Sunday range', () => {
    const range = getWeekRange(new Date('2026-05-01'));
    const from = new Date(range.from);
    const to = new Date(range.to);
    expect(from.getDay()).toBe(1);
    expect(to.getDay()).toBe(0);
  });
});

describe('getMonthRange', () => {
  it('should return first-to-last day of month', () => {
    const range = getMonthRange(new Date('2026-05-15'));
    const from = new Date(range.from);
    const to = new Date(range.to);
    expect(from.getDate()).toBe(1);
    expect(from.getMonth()).toBe(4);
    expect(to.getMonth()).toBe(4);
    expect(to.getDate()).toBe(31);
  });
});
