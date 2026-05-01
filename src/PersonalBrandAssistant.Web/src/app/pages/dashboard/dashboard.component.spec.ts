import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { DashboardComponent } from './dashboard.component';
import { DashboardStore } from './dashboard.store';
import { DashboardApiService } from './dashboard-api.service';

describe('DashboardComponent', () => {
  let component: DashboardComponent;
  let fixture: ComponentFixture<DashboardComponent>;
  let store: InstanceType<typeof DashboardStore>;
  let router: Router;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(DashboardStore);
    router = TestBed.inject(Router);
    spyOn(store, 'load');
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call store.load on init', () => {
    component.ngOnInit();
    expect(store.load).toHaveBeenCalled();
  });

  it('should navigate to content edit page', () => {
    const spy = spyOn(router, 'navigate');
    component.navigateToContent('abc-123');
    expect(spy).toHaveBeenCalledWith(['/content', 'abc-123', 'edit']);
  });

  it('should navigate to content page without suggestion', () => {
    const spy = spyOn(router, 'navigate');
    component.navigateToNew();
    expect(spy).toHaveBeenCalledWith(['/content']);
  });

  it('should navigate to content page with suggestion', () => {
    const spy = spyOn(router, 'navigate');
    component.navigateToNew({ topic: 'AI', platform: 'LinkedIn', source: 'news' });
    expect(spy).toHaveBeenCalledWith(['/content'], {
      queryParams: { topic: 'AI', platform: 'LinkedIn' },
    });
  });

  it('should navigate to calendar', () => {
    const spy = spyOn(router, 'navigate');
    component.navigateToCalendar();
    expect(spy).toHaveBeenCalledWith(['/calendar']);
  });

  it('should format cost correctly', () => {
    expect(component.formatCost(1.5)).toBe('$1.50');
    expect(component.formatCost(0)).toBe('$0.00');
    expect(component.formatCost(12.345)).toBe('$12.35');
  });

  it('should format time from ISO string', () => {
    expect(component.formatTime('2026-04-30T09:00:00Z')).toMatch(/\d{1,2}:\d{2} (AM|PM)/);
    expect(component.formatTime('2026-04-30T14:30:00Z')).toMatch(/\d{1,2}:30 (AM|PM)/);
  });

  it('should return relative date strings', () => {
    const now = Date.now();
    expect(component.relativeDate(new Date(now - 30 * 60000).toISOString())).toBe('30m ago');
    expect(component.relativeDate(new Date(now - 3 * 3600000).toISOString())).toBe('3h ago');
    expect(component.relativeDate(new Date(now - 24 * 3600000).toISOString())).toBe('yesterday');
    expect(component.relativeDate(new Date(now - 5 * 86400000).toISOString())).toBe('5d ago');
  });

  it('should track slots by id', () => {
    expect(component.trackSlot(0, { id: 'slot-1', scheduledAt: '', platform: 'LinkedIn' } as any)).toBe('slot-1');
  });

  it('should track items by id', () => {
    expect(component.trackItem(0, { id: 'item-1' } as any)).toBe('item-1');
  });

  it('should track suggestions by topic', () => {
    expect(component.trackSuggestion(0, { topic: 'AI Trends', platform: 'LinkedIn', source: 'news' })).toBe('AI Trends::LinkedIn');
  });
});
