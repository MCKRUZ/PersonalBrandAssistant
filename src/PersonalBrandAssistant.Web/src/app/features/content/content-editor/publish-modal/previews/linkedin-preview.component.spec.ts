import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LinkedinPreviewComponent } from './linkedin-preview.component';

describe('LinkedinPreviewComponent', () => {
  let fixture: ComponentFixture<LinkedinPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LinkedinPreviewComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(LinkedinPreviewComponent);
    fixture.componentRef.setInput('blocks', []);
  });

  function el(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('truncates a body over 210 chars and shows a …more affordance', () => {
    const long = 'a'.repeat(400);
    fixture.componentRef.setInput('body', long);
    fixture.detectChanges();

    const textEl = el().querySelector('.text');
    expect(textEl?.textContent).toContain('…more');
    expect(el().querySelector('.text')?.textContent).toContain('a'.repeat(210));
    // truncated body should not contain the full 400-char string
    expect((textEl?.textContent ?? '').includes('a'.repeat(211))).toBeFalse();
  });

  it('shows a warning when the body exceeds 3000 chars', () => {
    fixture.componentRef.setInput('body', 'b'.repeat(3001));
    fixture.detectChanges();

    expect(el().querySelector('.warn')?.textContent).toContain("Over LinkedIn's 3000-char limit");
  });

  it('shows the full short body with no …more affordance', () => {
    fixture.componentRef.setInput('body', 'A short post.');
    fixture.detectChanges();

    expect(el().querySelector('.text')?.textContent).toContain('A short post.');
    expect(el().querySelector('.more')).toBeNull();
    expect(el().querySelector('.warn')).toBeNull();
  });
});
