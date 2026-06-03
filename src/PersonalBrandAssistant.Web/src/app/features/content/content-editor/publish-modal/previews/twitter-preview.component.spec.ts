import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TwitterPreviewComponent } from './twitter-preview.component';
import { splitThread } from '../thread-splitter';

describe('TwitterPreviewComponent', () => {
  let fixture: ComponentFixture<TwitterPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TwitterPreviewComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(TwitterPreviewComponent);
    fixture.componentRef.setInput('blocks', []);
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('renders one numbered bubble per split tweet for a long body', () => {
    const body = 'This is a sentence with enough words to matter. '.repeat(20);
    const expected = splitThread(body, 280);

    fixture.componentRef.setInput('body', body);
    fixture.detectChanges();

    const bubbles = el().querySelectorAll('.tweet');
    expect(expected.length).toBeGreaterThan(1);
    expect(bubbles.length).toBe(expected.length);

    const firstText = el().querySelector('.tweet .text')?.textContent ?? '';
    expect(firstText).toContain('1/');
  });

  it('renders a single bubble for a short body', () => {
    fixture.componentRef.setInput('body', 'Just one tweet.');
    fixture.detectChanges();

    expect(el().querySelectorAll('.tweet').length).toBe(1);
  });
});
