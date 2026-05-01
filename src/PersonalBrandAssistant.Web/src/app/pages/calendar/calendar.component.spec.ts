import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { CalendarComponent } from './calendar.component';

describe('CalendarComponent', () => {
  let component: CalendarComponent;
  let fixture: ComponentFixture<CalendarComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CalendarComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), MessageService],
    }).compileComponents();

    fixture = TestBed.createComponent(CalendarComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  function flushInitialLoad(): void {
    fixture.detectChanges();
    httpMock.match(r => r.url.includes('/api/calendar')).forEach(r => r.flush([]));
    fixture.detectChanges();
  }

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render calendar header with title', () => {
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1?.textContent).toContain('Calendar');
  });

  it('should render platform filter chips', () => {
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('.filter-chip');
    expect(chips.length).toBe(8);
    expect(chips[0].textContent).toContain('All');
  });

  it('should render week grid by default', () => {
    flushInitialLoad();
    const weekGrid = fixture.nativeElement.querySelector('app-calendar-week-grid');
    const monthGrid = fixture.nativeElement.querySelector('app-calendar-month-grid');
    expect(weekGrid).toBeTruthy();
    expect(monthGrid).toBeFalsy();
  });

  it('should display navigation controls', () => {
    fixture.detectChanges();
    const navButtons = fixture.nativeElement.querySelectorAll('.header-center p-button');
    expect(navButtons.length).toBe(2);
  });

  it('should display date label', () => {
    fixture.detectChanges();
    const label = fixture.nativeElement.querySelector('.date-label');
    expect(label?.textContent?.trim().length).toBeGreaterThan(0);
  });

  it('should render action buttons (New Series, New Slot, Auto-Fill)', () => {
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('.header-right p-button');
    expect(buttons.length).toBe(3);
  });

  it('should have All filter active by default', () => {
    fixture.detectChanges();
    const allChip = fixture.nativeElement.querySelector('.filter-chip.active');
    expect(allChip?.textContent).toContain('All');
  });
});
