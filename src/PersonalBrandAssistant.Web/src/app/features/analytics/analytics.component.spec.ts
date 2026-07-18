import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { AnalyticsComponent } from './analytics.component';
import { AnalyticsOverview } from './models/channel-analytics.model';

describe('AnalyticsComponent (shell)', () => {
  let httpMock: HttpTestingController;

  const overviewStub: AnalyticsOverview = { totalAudience: 100, combinedKpis: [], channels: [] };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AnalyticsComponent, HttpClientTestingModule, NoopAnimationsModule],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders the five source tabs', () => {
    const fixture = TestBed.createComponent(AnalyticsComponent);
    fixture.detectChanges();

    // Overview mounts eagerly and fires its load; flush it so verify() stays clean.
    httpMock.expectOne('/api/analytics/overview?period=30d').flush(overviewStub);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    for (const label of ['Overview', 'Website', 'YouTube', 'Instagram', 'TikTok']) {
      expect(text).toContain(label);
    }
  });

  it('lazy-loads a channel tab: no channel request before activation, exactly one after', () => {
    const fixture = TestBed.createComponent(AnalyticsComponent);
    fixture.detectChanges();
    httpMock.expectOne('/api/analytics/overview?period=30d').flush(overviewStub);
    fixture.detectChanges();

    // Before activating YouTube: no channel request exists.
    expect(httpMock.match('/api/analytics/channel/youtube?period=30d').length).toBe(0);

    // Activate the YouTube tab through the rendered p-tabs DOM — this exercises the
    // (valueChange) wiring, not just the handler method.
    const el = fixture.nativeElement as HTMLElement;
    const tabs = Array.from(el.querySelectorAll('[role="tab"]')) as HTMLElement[];
    const youtubeTab = tabs.find(t => (t.textContent ?? '').trim() === 'YouTube');
    expect(youtubeTab).withContext('YouTube tab element should render').toBeTruthy();
    youtubeTab!.click();
    fixture.detectChanges();

    // Exactly one channel request now fires.
    const reqs = httpMock.match('/api/analytics/channel/youtube?period=30d');
    expect(reqs.length).toBe(1);
    reqs[0].flush({ platform: 'YouTube', status: 'NotConnected', asOf: null, kpis: [], trends: [], recentPosts: [] });
    fixture.detectChanges();
    // NotConnected => no deep call; nothing else outstanding.
  });
});
