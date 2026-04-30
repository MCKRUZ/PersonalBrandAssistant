import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { QuickPromptChipComponent } from './quick-prompt-chip.component';

@Component({
  standalone: true,
  imports: [QuickPromptChipComponent],
  template: `<app-quick-prompt-chip
    [label]="label"
    [prompt]="prompt"
    [disabled]="disabled"
    (clicked)="lastEmitted = $event"
  />`,
})
class TestHostComponent {
  label = 'Tighten this';
  prompt = 'Make this more concise';
  disabled = false;
  lastEmitted: string | undefined;
}

describe('QuickPromptChipComponent', () => {
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

  it('should emit click event with prompt text', () => {
    const button = fixture.debugElement.query(By.css('.prompt-chip'));
    button.nativeElement.click();
    expect(host.lastEmitted).toBe('Make this more concise');
  });

  it('should render label text', () => {
    host.label = 'Draft a post';
    fixture.detectChanges();
    const button = fixture.debugElement.query(By.css('.prompt-chip'));
    expect(button.nativeElement.textContent.trim()).toBe('Draft a post');
  });

  it('should not emit when disabled', () => {
    host.disabled = true;
    fixture.detectChanges();
    const button = fixture.debugElement.query(By.css('.prompt-chip'));
    button.nativeElement.click();
    expect(host.lastEmitted).toBeUndefined();
  });

  it('should set disabled attribute on button', () => {
    host.disabled = true;
    fixture.detectChanges();
    const button = fixture.debugElement.query(By.css('.prompt-chip'));
    expect(button.nativeElement.disabled).toBe(true);
  });
});
