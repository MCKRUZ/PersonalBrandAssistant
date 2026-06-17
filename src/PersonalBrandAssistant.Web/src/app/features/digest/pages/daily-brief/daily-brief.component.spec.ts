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
const msLatest: Digest = { id: 'm2', date: '2026-06-06', title: 'MS Today', intro: 'i', itemCount: 1,
  createdAt: '2026-06-06T07:00:00Z', items: [{ ideaId: 'x', rank: 1, score: 9, whyItMatters: 'w', title: 'Azure thing', url: null }] };

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

  const expectKind = (url: string, kind: string) =>
    http.expectOne(r => r.url === url && r.params.get('kind') === kind);

  function flushInit(ms: Digest | null = msLatest) {
    http.expectOne('/api/digests').flush(summaries);
    expectKind('/api/digests/latest', 'main').flush(latest);
    expectKind('/api/digests/by-date/2026-06-06', 'microsoft').flush(ms);
    fixture.detectChanges();
  }

  it('loads history, the latest brief, and the Microsoft brief side by side', () => {
    flushInit();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]').length).toBe(2);
    const heroes = fixture.nativeElement.querySelectorAll('[data-testid="brief-hero"]');
    expect(heroes.length).toBe(2); // main + microsoft columns
    expect(fixture.nativeElement.textContent).toContain('First');
    expect(fixture.nativeElement.textContent).toContain('Azure thing');
  });

  it('loads both briefs by date when a history entry is selected', () => {
    flushInit();
    fixture.componentInstance.onSelect('d1');
    http.expectOne('/api/digests/d1').flush({ ...latest, id: 'd1', title: 'Yesterday', date: '2026-06-05',
      items: [{ ideaId: 'b', rank: 1, score: 5, whyItMatters: 'w', title: 'Old', url: null }] });
    expectKind('/api/digests/by-date/2026-06-05', 'microsoft').flush(null); // no MS brief that day
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Old');
    expect(fixture.nativeElement.textContent).toContain('No Microsoft brief for this day.');
  });

  it('shows empty state when there are no briefs', () => {
    http.expectOne('/api/digests').flush([]);
    expectKind('/api/digests/latest', 'main').flush(null); // null date → no Microsoft request fired
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-empty"]')).toBeTruthy();
  });
});
