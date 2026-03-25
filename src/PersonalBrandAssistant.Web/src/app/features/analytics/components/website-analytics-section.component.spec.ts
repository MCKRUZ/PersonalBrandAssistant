import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WebsiteAnalyticsSectionComponent } from './website-analytics-section.component';
import { WebsiteAnalyticsResponse } from '../models/dashboard.model';

describe('WebsiteAnalyticsSectionComponent', () => {
  let component: WebsiteAnalyticsSectionComponent;
  let fixture: ComponentFixture<WebsiteAnalyticsSectionComponent>;

  const mockData: WebsiteAnalyticsResponse = {
    overview: {
      activeUsers: 1200,
      sessions: 3400,
      pageViews: 8900,
      avgSessionDuration: 142.5,
      bounceRate: 45.2,
      newUsers: 800,
    },
    topPages: [
      { pagePath: '/blog/ai-agents', views: 1200, users: 890 },
      { pagePath: '/about', views: 650, users: 520 },
      { pagePath: '/projects', views: 430, users: 350 },
      { pagePath: '/blog/claude-code', views: 380, users: 290 },
      { pagePath: '/contact', views: 210, users: 180 },
    ],
    trafficSources: [
      { channel: 'Organic Search', sessions: 1800, users: 1400 },
      { channel: 'Direct', sessions: 900, users: 700 },
      { channel: 'Social', sessions: 500, users: 400 },
      { channel: 'Referral', sessions: 200, users: 150 },
    ],
    searchQueries: [
      { query: 'matthew kruczek ai', clicks: 120, impressions: 1500, ctr: 0.08, position: 2.3 },
      { query: 'enterprise ai agents', clicks: 85, impressions: 3200, ctr: 0.027, position: 8.1 },
      { query: 'claude code tips', clicks: 45, impressions: 800, ctr: 0.056, position: 4.5 },
    ],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WebsiteAnalyticsSectionComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(WebsiteAnalyticsSectionComponent);
    component = fixture.componentInstance;
  });

  it('should render overview metric cards', () => {
    fixture.componentRef.setInput('data', mockData);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('1,200');
    expect(el.textContent).toContain('3,400');
    expect(el.textContent).toContain('8,900');
    expect(el.textContent).toContain('2m 23s');
    expect(el.textContent).toContain('45.2%');
    expect(el.textContent).toContain('800');
  });

  it('should render top pages table', () => {
    fixture.componentRef.setInput('data', mockData);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('.top-pages-table tbody tr, .top-pages-table .p-datatable-row-group');
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('/blog/ai-agents');
    expect(el.textContent).toContain('/about');
  });

  it('should render traffic sources table', () => {
    fixture.componentRef.setInput('data', mockData);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Organic Search');
    expect(el.textContent).toContain('Direct');
    expect(el.textContent).toContain('Social');
    expect(el.textContent).toContain('Referral');
  });

  it('should render search queries table with CTR as percentage', () => {
    fixture.componentRef.setInput('data', mockData);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('matthew kruczek ai');
    expect(el.textContent).toContain('8.0%');
    expect(el.textContent).toContain('2.3');
  });

  it('should show skeleton placeholders when data is null', () => {
    fixture.componentRef.setInput('data', null);
    fixture.detectChanges();

    const skeletons = fixture.nativeElement.querySelectorAll('p-skeleton');
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it('should handle empty arrays gracefully', () => {
    const emptyData: WebsiteAnalyticsResponse = {
      overview: mockData.overview,
      topPages: [],
      trafficSources: [],
      searchQueries: [],
    };
    fixture.componentRef.setInput('data', emptyData);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('No data');
  });
});
