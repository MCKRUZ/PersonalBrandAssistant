import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { OverviewComponent } from './overview.component';
import { AnalyticsOverview } from '../models/channel-analytics.model';

describe('OverviewComponent', () => {
  let httpMock: HttpTestingController;

  const stub: AnalyticsOverview = {
    totalAudience: 45678,
    combinedKpis: [
      { key: 'audience', label: 'Total Audience', value: 45678, deltaPct: 3.2, rate: null },
    ],
    channels: [
      {
        platform: 'YouTube',
        status: 'Connected',
        followers: 12000,
        followerSparkline: [
          { date: '2026-07-01', value: 11800 },
          { date: '2026-07-02', value: 11900 },
          { date: '2026-07-03', value: 12000 },
        ],
      },
      {
        platform: 'Instagram',
        status: 'ReconnectRequired',
        followers: 33678,
        followerSparkline: [
          { date: '2026-07-01', value: 33000 },
          { date: '2026-07-02', value: 33678 },
        ],
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [OverviewComponent, HttpClientTestingModule],
    });
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders the total audience labeled approximate and one sparkline per channel', () => {
    const fixture = TestBed.createComponent(OverviewComponent);
    fixture.detectChanges();

    httpMock.expectOne('/api/analytics/overview?period=30d').flush(stub);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    const text = (el.textContent ?? '').toLowerCase();

    // Total audience value present…
    expect(text).toContain('45,678');
    // …and explicitly labeled approximate.
    expect(/approximate|approx|≈/.test(text)).toBeTrue();

    // One sparkline (p-chart) per channel.
    const charts = el.querySelectorAll('p-chart');
    expect(charts.length).toBe(stub.channels.length);
  });
});
