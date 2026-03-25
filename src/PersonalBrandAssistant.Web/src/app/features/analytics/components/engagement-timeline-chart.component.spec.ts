import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { EngagementTimelineChartComponent } from './engagement-timeline-chart.component';
import { DailyEngagement } from '../models/dashboard.model';

describe('EngagementTimelineChartComponent', () => {
  let component: EngagementTimelineChartComponent;
  let fixture: ComponentFixture<EngagementTimelineChartComponent>;

  const mockTimeline: DailyEngagement[] = [
    { date: '2026-03-22', platforms: [
      { platform: 'TwitterX', likes: 30, comments: 10, shares: 10, total: 50 },
      { platform: 'LinkedIn', likes: 60, comments: 20, shares: 20, total: 100 },
    ], total: 150 },
    { date: '2026-03-23', platforms: [
      { platform: 'TwitterX', likes: 40, comments: 15, shares: 5, total: 60 },
      { platform: 'LinkedIn', likes: 70, comments: 30, shares: 10, total: 110 },
    ], total: 170 },
    { date: '2026-03-24', platforms: [
      { platform: 'TwitterX', likes: 20, comments: 5, shares: 5, total: 30 },
      { platform: 'LinkedIn', likes: 80, comments: 25, shares: 15, total: 120 },
    ], total: 150 },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EngagementTimelineChartComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(EngagementTimelineChartComponent);
    component = fixture.componentInstance;
  });

  it('should render p-chart element when data provided', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('p-chart')).toBeTruthy();
  });

  it('should produce correct number of datasets (Total + platforms)', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const data = component.chartData();
    expect(data.datasets.length).toBe(3); // Total + TwitterX + LinkedIn
  });

  it('should set fill:true only for Total dataset', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const datasets = component.chartData().datasets;
    expect(datasets[0].fill).toBe(true);
    expect(datasets[1].fill).toBe(false);
    expect(datasets[2].fill).toBe(false);
  });

  it('should produce labels matching timeline dates', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const labels = component.chartData().labels;
    expect(labels.length).toBe(3);
    expect(labels[0]).toContain('Mar');
  });

  it('should have Total dataset data equal to day totals', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const totalData = component.chartData().datasets[0].data;
    expect(totalData).toEqual([150, 170, 150]);
  });

  it('should use PLATFORM_COLORS for platform datasets', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const twitterDataset = component.chartData().datasets.find(d => d.label === 'Twitter/X');
    expect(twitterDataset?.borderColor).toBe('#1DA1F2');
  });

  it('should produce empty data for empty timeline', () => {
    fixture.componentRef.setInput('timeline', []);
    fixture.detectChanges();
    const data = component.chartData();
    expect(data.labels.length).toBe(0);
    expect(data.datasets.length).toBe(0);
  });
});
