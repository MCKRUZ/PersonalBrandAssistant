import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { StreamingTextComponent } from './streaming-text.component';

@Component({
  standalone: true,
  imports: [StreamingTextComponent],
  template: `<app-streaming-text [text]="text" [streaming]="streaming" />`,
})
class TestHostComponent {
  text = 'Hello worl';
  streaming = true;
}

describe('StreamingTextComponent', () => {
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

  it('should render text with caret when streaming', () => {
    const textEl = fixture.debugElement.query(By.css('.streaming-text'));
    expect(textEl.nativeElement.textContent).toContain('Hello worl');
    const caret = fixture.debugElement.query(By.css('.caret'));
    expect(caret).toBeTruthy();
  });

  it('should render text without caret when not streaming', () => {
    host.text = 'Hello world';
    host.streaming = false;
    fixture.detectChanges();
    const textEl = fixture.debugElement.query(By.css('.streaming-text'));
    expect(textEl.nativeElement.textContent.trim()).toBe('Hello world');
    const caret = fixture.debugElement.query(By.css('.caret'));
    expect(caret).toBeNull();
  });

  it('should update text reactively', () => {
    host.text = 'H';
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.streaming-text')).nativeElement.textContent).toContain('H');

    host.text = 'He';
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.streaming-text')).nativeElement.textContent).toContain('He');

    host.text = 'Hel';
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.css('.streaming-text')).nativeElement.textContent).toContain('Hel');
  });
});
