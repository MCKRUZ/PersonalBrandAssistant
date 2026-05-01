import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { AxisBarComponent } from './axis-bar.component';

@Component({
  standalone: true,
  imports: [AxisBarComponent],
  template: `<app-axis-bar [label]="label" [value]="value" />`,
})
class TestHostComponent {
  label = 'Authoritative';
  value = 85;
}

describe('AxisBarComponent', () => {
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

  it('should render label and value', () => {
    const label = fixture.debugElement.query(By.css('.axis-label'));
    expect(label.nativeElement.textContent.trim()).toBe('Authoritative');

    const value = fixture.debugElement.query(By.css('.axis-value'));
    expect(value.nativeElement.textContent.trim()).toBe('85');
  });

  it('should fill bar proportionally to value', () => {
    host.value = 50;
    fixture.detectChanges();
    const fill = fixture.debugElement.query(By.css('.axis-fill'));
    expect(fill.nativeElement.style.width).toBe('50%');

    host.value = 100;
    fixture.detectChanges();
    const fillFull = fixture.debugElement.query(By.css('.axis-fill'));
    expect(fillFull.nativeElement.style.width).toBe('100%');
  });

  it('should apply green color for value >= 80', () => {
    host.value = 90;
    fixture.detectChanges();
    const fill = fixture.debugElement.query(By.css('.axis-fill'));
    expect(fill.nativeElement.style.background).toBe('rgb(74, 222, 128)');
  });

  it('should apply yellow color for value >= 60 and < 80', () => {
    host.value = 65;
    fixture.detectChanges();
    const fill = fixture.debugElement.query(By.css('.axis-fill'));
    expect(fill.nativeElement.style.background).toBe('rgb(251, 191, 36)');
  });

  it('should apply red color for value < 60', () => {
    host.value = 40;
    fixture.detectChanges();
    const fill = fixture.debugElement.query(By.css('.axis-fill'));
    expect(fill.nativeElement.style.background).toBe('rgb(248, 113, 113)');
  });
});
