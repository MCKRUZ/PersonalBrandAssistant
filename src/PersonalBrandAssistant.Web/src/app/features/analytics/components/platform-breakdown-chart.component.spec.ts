import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { PlatformBreakdownChartComponent } from './platform-breakdown-chart.component';
import { DailyEngagement } from '../models/dashboard.model';

describe('PlatformBreakdownChartComponent', () => {
  let component: PlatformBreakdownChartComponent;
  let fixture: ComponentFixture<PlatformBreakdownChartComponent>;

  const mockTimeline: DailyEngagement[] = [
    { date: '2026-03-22', platforms: [
      { platform: 'TwitterX', likes: 10, comments: 5, shares: 3, total: 18 },
      { platform: 'YouTube', likes: 20, comments: 10, shares: 5, total: 35 },
    ], total: 53 },
    { date: '2026-03-23', platforms: [
      { platform: 'TwitterX', likes: 20, comments: 8, shares: 2, total: 30 },
      { platform: 'YouTube', likes: 30, comments: 12, shares: 8, total: 50 },
    ], total: 80 },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlatformBreakdownChartComponent, NoopAnimationsModule],
    }).compileComponents();

    fixture = TestBed.createComponent(PlatformBreakdownChartComponent);
    component = fixture.componentInstance;
  });

  it('should render p-chart element with data', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('p-chart')).toBeTruthy();
  });

  it('should produce exactly 3 datasets: Likes, Comments, Shares', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const datasets = component.chartData().datasets;
    expect(datasets.length).toBe(3);
    expect(datasets.map(d => d.label)).toEqual(['Likes', 'Comments', 'Shares']);
  });

  it('should aggregate likes per platform across all days', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const data = component.chartData();
    // YouTube has more total (35+50=85) vs TwitterX (18+30=48), so YouTube comes first
    const youtubeIdx = data.labels.indexOf('YouTube');
    const likesDataset = data.datasets[0];
    expect(likesDataset.data[youtubeIdx]).toBe(50); // 20 + 30
  });

  it('should produce platform labels from input data', () => {
    fixture.componentRef.setInput('timeline', mockTimeline);
    fixture.detectChanges();
    const labels = component.chartData().labels;
    expect(labels).toContain('Twitter/X');
    expect(labels).toContain('YouTube');
  });

  it('should produce empty data for empty timeline', () => {
    fixture.componentRef.setInput('timeline', []);
    fixture.detectChanges();
    const data = component.chartData();
    expect(data.labels.length).toBe(0);
    expect(data.datasets.length).toBe(0);
  });

  it('should use horizontal bar layout (indexAxis y)', () => {
    expect(component.chartOptions.indexAxis).toBe('y');
  });

  it('should have stacked scales', () => {
    expect(component.chartOptions.scales.x.stacked).toBe(true);
    expect(component.chartOptions.scales.y.stacked).toBe(true);
  });
});
