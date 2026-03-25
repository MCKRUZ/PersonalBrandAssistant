import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { DateRangeSelectorComponent } from './date-range-selector.component';
import { DashboardPeriod } from '../models/dashboard.model';

describe('DateRangeSelectorComponent', () => {
  let component: DateRangeSelectorComponent;
  let fixture: ComponentFixture<DateRangeSelectorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DateRangeSelectorComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(DateRangeSelectorComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should emit periodChanged on preset button click', () => {
    let emitted: DashboardPeriod | undefined;
    component.periodChanged.subscribe((p) => (emitted = p));

    component.selectPreset('7d');

    expect(emitted).toBe('7d');
  });

  it('should highlight active preset with filled style', () => {
    fixture.componentRef.setInput('activePeriod', '14d');
    fixture.detectChanges();

    expect(component.isActive('14d')).toBeTrue();
    expect(component.isActive('30d')).toBeFalse();
  });

  it('should emit custom date range from calendar', () => {
    let emitted: DashboardPeriod | undefined;
    component.periodChanged.subscribe((p) => (emitted = p));

    const from = new Date('2026-01-01');
    const to = new Date('2026-01-31');
    component.customRange = [from, to];
    component.onCustomSelect();

    expect(emitted).toBeTruthy();
    expect(typeof emitted).toBe('object');
    if (typeof emitted === 'object' && emitted !== null && 'from' in emitted) {
      expect(emitted.from).toBe(from.toISOString());
      expect(emitted.to).toBe(to.toISOString());
    }
  });

  it('should default to 30D preset on initialization', () => {
    expect(component.isActive('30d')).toBeTrue();
  });
});
