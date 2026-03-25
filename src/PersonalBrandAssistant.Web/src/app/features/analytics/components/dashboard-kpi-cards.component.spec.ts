import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DashboardKpiCardsComponent } from './dashboard-kpi-cards.component';
import { DashboardSummary } from '../models/dashboard.model';

describe('DashboardKpiCardsComponent', () => {
  let component: DashboardKpiCardsComponent;
  let fixture: ComponentFixture<DashboardKpiCardsComponent>;

  const mockSummary: DashboardSummary = {
    totalEngagement: 12847,
    previousEngagement: 10878,
    totalImpressions: 284000,
    previousImpressions: 250000,
    engagementRate: 4.52,
    previousEngagementRate: 4.35,
    contentPublished: 12,
    previousContentPublished: 10,
    costPerEngagement: 0.03,
    previousCostPerEngagement: 0.04,
    websiteUsers: 1200,
    previousWebsiteUsers: 1000,
    generatedAt: '2026-03-25T00:00:00Z',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardKpiCardsComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardKpiCardsComponent);
    component = fixture.componentInstance;
  });

  it('should render all 6 KPI cards with correct values', () => {
    fixture.componentRef.setInput('summary', mockSummary);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('.kpi-card');
    expect(cards.length).toBe(6);

    const labels = Array.from(cards).map((c: any) => c.querySelector('.kpi-label')?.textContent?.trim());
    expect(labels).toContain('Total Engagement');
    expect(labels).toContain('Total Impressions');
    expect(labels).toContain('Engagement Rate');
    expect(labels).toContain('Content Published');
    expect(labels).toContain('Cost / Engagement');
    expect(labels).toContain('Website Users');
  });

  it('should show up trend indicator for positive change', () => {
    fixture.componentRef.setInput('summary', mockSummary);
    fixture.detectChanges();

    const kpiCards = component.kpiCards();
    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
    expect(engagement?.trend).toBe('up');
    expect(engagement?.changeText).toMatch(/^\+\d+\.\d+%$/);
  });

  it('should show down trend indicator for negative change', () => {
    const downSummary = { ...mockSummary, totalEngagement: 8000, previousEngagement: 10000 };
    fixture.componentRef.setInput('summary', downSummary);
    fixture.detectChanges();

    const kpiCards = component.kpiCards();
    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
    expect(engagement?.trend).toBe('down');
    expect(engagement?.changeText).toMatch(/^-\d+\.\d+%$/);
  });

  it('should show N/A when previous period value is 0', () => {
    const zeroSummary = { ...mockSummary, previousEngagement: 0 };
    fixture.componentRef.setInput('summary', zeroSummary);
    fixture.detectChanges();

    const kpiCards = component.kpiCards();
    const engagement = kpiCards.find(c => c.label === 'Total Engagement');
    expect(engagement?.changeText).toBe('N/A');
    expect(engagement?.trend).toBe('neutral');
  });

  it('should format large numbers with abbreviations', () => {
    fixture.componentRef.setInput('summary', mockSummary);
    fixture.detectChanges();

    const kpiCards = component.kpiCards();
    const impressions = kpiCards.find(c => c.label === 'Total Impressions');
    expect(impressions?.value).toBe('284K');
  });

  it('should format engagement rate as percentage', () => {
    fixture.componentRef.setInput('summary', mockSummary);
    fixture.detectChanges();

    const kpiCards = component.kpiCards();
    const rate = kpiCards.find(c => c.label === 'Engagement Rate');
    expect(rate?.value).toBe('4.52%');
  });
});
