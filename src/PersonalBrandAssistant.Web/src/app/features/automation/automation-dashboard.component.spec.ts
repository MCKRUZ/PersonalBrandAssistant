import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { AutomationDashboardComponent } from './automation-dashboard.component';

const API = 'http://localhost:5000/api';

const mockConfig = {
  cronExpression: '30 9 * * 1-5',
  timeZone: 'America/New_York',
  autonomyLevel: 'Supervised',
  targetPlatforms: ['TwitterX', 'LinkedIn'],
  imageGeneration: { enabled: true, provider: 'ComfyUI' },
  maxPostsPerRun: 3,
};

const mockRuns = [
  {
    id: 'run-1', status: 'Completed', triggeredAt: '2026-01-01T10:00:00Z',
    completedAt: '2026-01-01T10:02:00Z', durationMs: 120000,
    platformVersionCount: 4, selectionReasoning: 'AI trend', errorDetails: null,
  },
];

describe('AutomationDashboardComponent', () => {
  let fixture: ComponentFixture<AutomationDashboardComponent>;
  let component: AutomationDashboardComponent;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AutomationDashboardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), MessageService],
    }).compileComponents();
    fixture = TestBed.createComponent(AutomationDashboardComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.match(() => true).forEach(req => {
      if (!req.cancelled) req.flush([]);
    });
    httpMock.verify();
  });

  function loadDashboard() {
    fixture.detectChanges();
    httpMock.expectOne(req => req.url.includes('/automation/runs')).flush(mockRuns);
    httpMock.expectOne(`${API}/automation/config`).flush(mockConfig);
    fixture.detectChanges();
  }

  it('should create', () => {
    loadDashboard();
    expect(component).toBeTruthy();
  });

  it('should render page header with breadcrumb', () => {
    loadDashboard();
    const el = fixture.nativeElement;
    expect(el.querySelector('.breadcrumb').textContent).toContain('SETTINGS / AUTOMATION');
    expect(el.querySelector('.auto-title').textContent).toContain('Automation');
    expect(el.querySelector('.auto-subtitle').textContent).toContain('Triggers');
  });

  it('should render config panel after loading', fakeAsync(() => {
    loadDashboard();
    tick();
    const el = fixture.nativeElement;
    expect(el.textContent).toContain('Schedule');
    expect(el.textContent).toContain('Mode');
  }));

  it('should render runs table', fakeAsync(() => {
    loadDashboard();
    tick();
    expect(fixture.nativeElement.querySelector('p-table')).toBeTruthy();
  }));

  it('should show loading spinner initially', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-loading-spinner')).toBeTruthy();
    httpMock.expectOne(req => req.url.includes('/automation/runs')).flush(mockRuns);
    httpMock.expectOne(`${API}/automation/config`).flush(mockConfig);
  });

  it('should open detail dialog when clicking a run', fakeAsync(() => {
    loadDashboard();
    tick();
    component.openDetail(mockRuns[0] as any);
    expect(component.showDialog).toBeTrue();
    expect(component.selectedRun).toBeTruthy();
  }));

  it('should convert cron to human-readable', () => {
    expect(component.cronToHuman('30 9 * * 1-5', 'America/New_York')).toContain('9:30 AM');
    expect(component.cronToHuman('30 9 * * 1-5', 'America/New_York')).toContain('ET');
  });
});
