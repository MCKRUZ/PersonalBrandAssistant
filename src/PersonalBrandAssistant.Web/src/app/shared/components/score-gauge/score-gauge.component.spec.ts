import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { ScoreGaugeComponent } from './score-gauge.component';

@Component({
  standalone: true,
  imports: [ScoreGaugeComponent],
  template: `<app-score-gauge [score]="score" [size]="size" />`,
})
class TestHostComponent {
  score = 75;
  size = 120;
}

describe('ScoreGaugeComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render SVG circle elements', () => {
    const circles = fixture.debugElement.queryAll(By.css('circle'));
    expect(circles.length).toBe(2);
  });

  it('should render score as circular gauge with correct dashoffset', () => {
    host.score = 75;
    fixture.detectChanges();
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    const r = (120 - 8) / 2;
    const circumference = 2 * Math.PI * r;
    const expectedOffset = circumference * (1 - 0.75);
    expect(Number(arc.nativeElement.getAttribute('stroke-dashoffset'))).toBeCloseTo(expectedOffset, 0);
  });

  it('should show green color for score >= 80', () => {
    host.score = 90;
    fixture.detectChanges();
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    expect(arc.nativeElement.getAttribute('stroke')).toBe('#4ade80');
  });

  it('should show yellow color for score >= 60', () => {
    host.score = 65;
    fixture.detectChanges();
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    expect(arc.nativeElement.getAttribute('stroke')).toBe('#fbbf24');
  });

  it('should show red color for score < 60', () => {
    host.score = 30;
    fixture.detectChanges();
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    expect(arc.nativeElement.getAttribute('stroke')).toBe('#f87171');
  });

  it('should display numeric score in center', () => {
    host.score = 82;
    fixture.detectChanges();
    const text = fixture.debugElement.query(By.css('text'));
    expect(text.nativeElement.textContent.trim()).toBe('82');
  });

  it('should handle score of 0', () => {
    host.score = 0;
    fixture.detectChanges();
    const text = fixture.debugElement.query(By.css('text'));
    expect(text.nativeElement.textContent.trim()).toBe('0');
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    expect(arc).toBeTruthy();
  });

  it('should handle score of 100', () => {
    host.score = 100;
    fixture.detectChanges();
    const text = fixture.debugElement.query(By.css('text'));
    expect(text.nativeElement.textContent.trim()).toBe('100');
    const arc = fixture.debugElement.query(By.css('.gauge-arc'));
    const offset = Number(arc.nativeElement.getAttribute('stroke-dashoffset'));
    expect(offset).toBeCloseTo(0, 0);
  });

  it('should clamp scores outside 0-100 range', () => {
    host.score = 150;
    fixture.detectChanges();
    const text = fixture.debugElement.query(By.css('text'));
    expect(text.nativeElement.textContent.trim()).toBe('100');

    host.score = -10;
    fixture.detectChanges();
    const textNeg = fixture.debugElement.query(By.css('text'));
    expect(textNeg.nativeElement.textContent.trim()).toBe('0');
  });
});
