import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { DailyBriefComponent } from './daily-brief.component';
import { Digest, DigestSummary } from '../../models/digest.model';

const summaries: DigestSummary[] = [
  { id: 'd2', date: '2026-06-06', title: 'Today', itemCount: 1, createdAt: '2026-06-06T07:00:00Z' },
  { id: 'd1', date: '2026-06-05', title: 'Yesterday', itemCount: 1, createdAt: '2026-06-05T07:00:00Z' },
];
const latest: Digest = { id: 'd2', date: '2026-06-06', title: 'Today', intro: 'i', itemCount: 1,
  createdAt: '2026-06-06T07:00:00Z', items: [{ ideaId: 'a', rank: 1, score: 9, whyItMatters: 'w', title: 'First', url: null }] };

describe('DailyBriefComponent', () => {
  let fixture: ComponentFixture<DailyBriefComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyBriefComponent, HttpClientTestingModule],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(DailyBriefComponent);
    fixture.detectChanges(); // ngOnInit
  });

  afterEach(() => http.verify());

  function flushInit() {
    http.expectOne('/api/digests').flush(summaries);
    http.expectOne('/api/digests/latest').flush(latest);
    fixture.detectChanges();
  }

  it('loads history and the latest brief on init', () => {
    flushInit();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]').length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="brief-hero"]').textContent).toContain('First');
  });

  it('loads a brief by id when a history entry is selected', () => {
    flushInit();
    fixture.componentInstance.onSelect('d1');
    const req = http.expectOne('/api/digests/d1');
    req.flush({ ...latest, id: 'd1', title: 'Yesterday', items: [{ ideaId: 'b', rank: 1, score: 5, whyItMatters: 'w', title: 'Old', url: null }] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-hero"]').textContent).toContain('Old');
  });

  it('shows empty state when there are no briefs', () => {
    http.expectOne('/api/digests').flush([]);
    http.expectOne('/api/digests/latest').flush(null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-empty"]')).toBeTruthy();
  });
});
