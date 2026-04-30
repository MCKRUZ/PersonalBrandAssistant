import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { KpiCardComponent } from './kpi-card.component';

@Component({
  standalone: true,
  imports: [KpiCardComponent],
  template: `<app-kpi-card [value]="value" [label]="label" [trend]="trend" [sub]="sub" [flagged]="flagged" />`,
})
class TestHostComponent {
  value: number | string | undefined = 42;
  label = 'Pending Review';
  trend: 'up' | 'down' | 'flat' | undefined = 'up';
  sub: string | undefined;
  flagged = false;
}

describe('KpiCardComponent', () => {
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

  it('should render value, label, and trend indicator', () => {
    const value = fixture.debugElement.query(By.css('.kpi-value'));
    expect(value.nativeElement.textContent.trim()).toBe('42');

    const label = fixture.debugElement.query(By.css('.kpi-label'));
    expect(label.nativeElement.textContent.trim()).toBe('Pending Review');

    const trend = fixture.debugElement.query(By.css('.kpi-trend'));
    expect(trend).toBeTruthy();
  });

  it('should format large numbers with K suffix', () => {
    host.value = 1500;
    fixture.detectChanges();
    const value = fixture.debugElement.query(By.css('.kpi-value'));
    expect(value.nativeElement.textContent.trim()).toBe('1.5K');
  });

  it('should format large numbers with M suffix', () => {
    host.value = 2300000;
    fixture.detectChanges();
    const value = fixture.debugElement.query(By.css('.kpi-value'));
    expect(value.nativeElement.textContent.trim()).toBe('2.3M');
  });

  it('should render without trend indicator when trend is undefined', () => {
    host.trend = undefined;
    fixture.detectChanges();
    const trend = fixture.debugElement.query(By.css('.kpi-trend'));
    expect(trend).toBeNull();
  });

  it('should apply flagged styling when flagged input is true', () => {
    host.flagged = true;
    fixture.detectChanges();
    const card = fixture.debugElement.query(By.css('.kpi-card'));
    expect(card.nativeElement.classList).toContain('flagged');
  });

  it('should render -- when value is undefined', () => {
    host.value = undefined;
    fixture.detectChanges();
    const value = fixture.debugElement.query(By.css('.kpi-value'));
    expect(value.nativeElement.textContent.trim()).toBe('--');
  });

  it('should render string values directly', () => {
    host.value = '$4.20';
    fixture.detectChanges();
    const value = fixture.debugElement.query(By.css('.kpi-value'));
    expect(value.nativeElement.textContent.trim()).toBe('$4.20');
  });
});
