import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ChannelAnalyticsComponent } from './channel-analytics.component';
import { ChannelAnalytics, YouTubeDeepAnalytics } from '../models/channel-analytics.model';

describe('ChannelAnalyticsComponent', () => {
  let httpMock: HttpTestingController;

  const connected: ChannelAnalytics = {
    platform: 'YouTube',
    status: 'Connected',
    asOf: '2026-07-03',
    kpis: [
      { key: 'subscribers', label: 'Subscribers', value: 12000, deltaPct: 1.5, rate: null },
      { key: 'engagement', label: 'Engagement', value: 340, deltaPct: null, rate: 0.042 },
    ],
    trends: [
      { metric: 'Views', points: [{ date: '2026-07-01', value: 100 }, { date: '2026-07-02', value: 150 }] },
    ],
    recentPosts: [
      { videoId: 'vid1', title: 'How AI ships', metrics: { views: 900, likes: 42 } },
    ],
  };

  const deep: YouTubeDeepAnalytics = {
    daySeries: [{ metric: 'estimatedMinutesWatched', points: [{ day: '2026-07-01', value: 500 }] }],
    trafficSources: [{ metric: 'SUGGESTED', points: [{ day: '2026-07-01', value: 200 }] }],
    geography: [],
    demographics: [{ metric: 'age25-34', points: [{ day: '2026-07-01', value: 60 }] }],
  };

  function createFor(platform: 'youtube' | 'instagram' | 'tiktok') {
    const fixture = TestBed.createComponent(ChannelAnalyticsComponent);
    fixture.componentRef.setInput('platform', platform);
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ChannelAnalyticsComponent, HttpClientTestingModule],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders KPI row, trend charts and recent-posts table when Connected', () => {
    const fixture = createFor('instagram');

    httpMock.expectOne('/api/analytics/channel/instagram?period=30d').flush({ ...connected, platform: 'Instagram' });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain('Subscribers');
    expect(text).toContain('12,000');
    // one trend chart present
    expect(el.querySelectorAll('p-chart').length).toBeGreaterThanOrEqual(1);
    // recent-posts table row rendered
    expect(text).toContain('How AI ships');
    // delta badge (up arrow for +1.5%) and engagement rate (0.042 -> 4.20%) rendered
    expect(text).toContain('▲');
    expect(text).toContain('eng.');
    // postColumns() unions the metric keys across posts into table headers
    const headers = Array.from(el.querySelectorAll('.data-table thead th')).map(h => h.textContent?.trim());
    expect(headers).toContain('views');
    expect(headers).toContain('likes');
  });

  it('re-fetches with the new period when the period changes', () => {
    const fixture = createFor('instagram');
    httpMock.expectOne('/api/analytics/channel/instagram?period=30d')
      .flush({ ...connected, platform: 'Instagram' });
    fixture.detectChanges();

    fixture.componentInstance.changePeriod('7d');
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/channel/instagram?period=7d')
      .flush({ ...connected, platform: 'Instagram' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('last 7 days');
  });

  it('shows a Connect affordance linking to the OAuth authorize URL when NotConnected', () => {
    const fixture = createFor('instagram');

    httpMock.expectOne('/api/analytics/channel/instagram?period=30d')
      .flush({ platform: 'Instagram', status: 'NotConnected', asOf: null, kpis: [], trends: [], recentPosts: [] });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('a.connect-btn') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link!.textContent).toContain('Connect');
    expect(link!.getAttribute('href')).toBe('/api/auth/instagram/authorize?purpose=analytics');
  });

  it('shows a Reconnect affordance when ReconnectRequired', () => {
    const fixture = createFor('tiktok');

    httpMock.expectOne('/api/analytics/channel/tiktok?period=30d')
      .flush({ platform: 'TikTok', status: 'ReconnectRequired', asOf: null, kpis: [], trends: [], recentPosts: [] });
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const link = el.querySelector('a.connect-btn') as HTMLAnchorElement | null;
    expect(link).not.toBeNull();
    expect(link!.textContent).toContain('Reconnect');
    expect(link!.getAttribute('href')).toBe('/api/auth/tiktok/authorize?purpose=analytics');
  });

  it('renders the YouTube deep panel on success (channel + deep requests)', () => {
    const fixture = createFor('youtube');

    httpMock.expectOne('/api/analytics/channel/youtube?period=30d').flush(connected);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/youtube/deep?period=30d').flush(deep);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('YouTube Deep Analytics');
    expect(text).toContain('estimatedMinutesWatched');
    // breakdowns rendered (summed across range), not last-day sampled
    expect(text).toContain('Traffic Sources');
    expect(text).toContain('SUGGESTED');
    expect(text).toContain('Demographics');
    expect(text).toContain('age25-34');
  });

  it('adds a distinct error state (with retry) when the channel load fails', () => {
    const fixture = createFor('tiktok');

    httpMock.expectOne('/api/analytics/channel/tiktok?period=30d').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const text = el.textContent ?? '';
    expect(text).toContain("Couldn't load");
    const retry = el.querySelector('button.retry-btn') as HTMLButtonElement | null;
    expect(retry).not.toBeNull();

    // Retry re-issues the request.
    retry!.click();
    fixture.detectChanges();
    httpMock.expectOne('/api/analytics/channel/tiktok?period=30d')
      .flush({ platform: 'TikTok', status: 'Connected', asOf: null, kpis: [], trends: [], recentPosts: [] });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain("Couldn't load");
  });

  it('degrades gracefully when the deep call errors — rest of the tab still renders', () => {
    const fixture = createFor('youtube');

    httpMock.expectOne('/api/analytics/channel/youtube?period=30d').flush(connected);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/youtube/deep?period=30d').error(new ProgressEvent('error'));
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    // KPI/trend content still shows…
    expect(text).toContain('Subscribers');
    expect(text).toContain('How AI ships');
    // …but the deep panel is absent.
    expect(text).not.toContain('YouTube Deep Analytics');
  });
});
