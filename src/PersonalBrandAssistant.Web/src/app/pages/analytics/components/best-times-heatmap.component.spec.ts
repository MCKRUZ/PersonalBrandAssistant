import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { BestTimesHeatmapComponent } from './best-times-heatmap.component';
import { BestTimesHeatmap } from '../heatmap.model';

@Component({
  standalone: true,
  imports: [BestTimesHeatmapComponent],
  template: `<app-best-times-heatmap [heatmap]="heatmap" />`,
})
class TestHostComponent {
  heatmap: BestTimesHeatmap | null = null;
}

describe('BestTimesHeatmapComponent', () => {
  let host: TestHostComponent;
  let fixture: ComponentFixture<TestHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-best-times-heatmap')).toBeTruthy();
  });

  it('should show empty state when no heatmap data', () => {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('No posting time data');
  });

  it('should render grid when heatmap has data', () => {
    host.heatmap = {
      cells: [{ day: 0, hour: 9, engagement: 10 }, { day: 1, hour: 14, engagement: 20 }],
      maxEngagement: 20,
    };
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('.cell');
    expect(cells.length).toBe(7 * 14);
  });

  it('should apply color based on engagement intensity', () => {
    host.heatmap = {
      cells: [{ day: 0, hour: 9, engagement: 10 }],
      maxEngagement: 10,
    };
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('.cell') as NodeListOf<HTMLElement>;
    const mondayNineCell = cells[3];
    expect(mondayNineCell.style.backgroundColor).toContain('200');
  });

  it('should show empty color for zero engagement cells', () => {
    host.heatmap = {
      cells: [{ day: 0, hour: 9, engagement: 5 }],
      maxEngagement: 5,
    };
    fixture.detectChanges();
    const cells = fixture.nativeElement.querySelectorAll('.cell') as NodeListOf<HTMLElement>;
    const emptyCell = cells[0];
    expect(emptyCell.style.backgroundColor).toContain('rgba');
  });

  it('should format hours correctly', () => {
    host.heatmap = { cells: [{ day: 0, hour: 6, engagement: 1 }], maxEngagement: 1 };
    fixture.detectChanges();
    const hourLabels = fixture.nativeElement.querySelectorAll('.hour-label');
    expect(hourLabels[0].textContent.trim()).toBe('6a');
    expect(hourLabels[6].textContent.trim()).toBe('12p');
    expect(hourLabels[7].textContent.trim()).toBe('1p');
  });

  it('should display all day labels', () => {
    host.heatmap = { cells: [{ day: 0, hour: 6, engagement: 1 }], maxEngagement: 1 };
    fixture.detectChanges();
    const dayLabels = fixture.nativeElement.querySelectorAll('.day-label');
    expect(dayLabels.length).toBe(7);
    expect(dayLabels[0].textContent.trim()).toBe('Mon');
    expect(dayLabels[6].textContent.trim()).toBe('Sun');
  });
});
