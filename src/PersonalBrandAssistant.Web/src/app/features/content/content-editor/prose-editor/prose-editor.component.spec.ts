import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ComponentRef } from '@angular/core';
import { ProseEditorComponent } from './prose-editor.component';

/** Normalize markdown for round-trip comparison: collapse trailing whitespace and blank-line runs. */
function norm(md: string): string {
  return md
    .split('\n')
    .map((line) => line.replace(/\s+$/, ''))
    .join('\n')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

describe('ProseEditorComponent', () => {
  let fixture: ComponentFixture<ProseEditorComponent>;
  let component: ProseEditorComponent;
  let ref: ComponentRef<ProseEditorComponent>;

  function setup(value = '', readOnly = false) {
    TestBed.configureTestingModule({
      imports: [ProseEditorComponent],
    });
    fixture = TestBed.createComponent(ProseEditorComponent);
    component = fixture.componentInstance;
    ref = fixture.componentRef;
    ref.setInput('value', value);
    ref.setInput('readOnly', readOnly);
    fixture.detectChanges();
  }

  afterEach(() => {
    fixture?.destroy();
  });

  // GATES THE SECTION: markdown -> setContent -> serialize -> markdown must be stable.
  it('round-trips the supported mark set (h1-h3, bold, italic, links, lists, inline code)', () => {
    const md = [
      '# Heading one',
      '',
      '## Heading two',
      '',
      '### Heading three',
      '',
      'A paragraph with **bold** and *italic* and `inline code`.',
      '',
      'A [link](https://example.com) inline.',
      '',
      '- bullet one',
      '- bullet two',
      '',
      '1. ordered one',
      '2. ordered two',
    ].join('\n');

    setup(md);
    const out = component.serializeForTest();
    expect(norm(out)).toBe(norm(md));
  });

  it('emits the debounced serialized markdown on edit', fakeAsync(() => {
    setup('hello');
    let emitted: string | undefined;
    component.valueChange.subscribe((v) => (emitted = v));

    component.insertTextForTest(' world');
    // debounce window not yet elapsed
    expect(emitted).toBeUndefined();
    tick(350);
    expect(emitted).toContain('hello world');
  }));

  it('does NOT re-apply setContent when incoming value equals last serialized output', () => {
    setup('# Title');
    const spy = spyOn(component as any, 'applyExternalValue').and.callThrough();
    const current = component.serializeForTest();
    ref.setInput('value', current);
    fixture.detectChanges();
    expect(spy).not.toHaveBeenCalled();
  });

  it('skips setContent while the editor is focused', () => {
    setup('# Title');
    spyOnProperty(component as any, 'isFocusedForTest', 'get').and.returnValue(true);
    const spy = spyOn(component as any, 'applyExternalValue').and.callThrough();
    ref.setInput('value', '# Totally different external value');
    fixture.detectChanges();
    expect(spy).not.toHaveBeenCalled();
  });

  it('readOnly=true makes the editor non-editable', () => {
    setup('# Title', true);
    expect(component.isEditableForTest()).toBeFalse();
  });

  it('readOnly toggles editability when the input changes', () => {
    setup('# Title', false);
    expect(component.isEditableForTest()).toBeTrue();
    ref.setInput('readOnly', true);
    fixture.detectChanges();
    expect(component.isEditableForTest()).toBeFalse();
  });

  it('sanitizes pasted HTML: drops script/style, keeps allowlisted text', () => {
    setup('start');
    const cleaned = component.sanitizeHtmlForTest(
      '<p>Keep <strong>this</strong></p><script>alert(1)</script><style>x{}</style>',
    );
    expect(cleaned).not.toContain('<script');
    expect(cleaned).not.toContain('<style');
    expect(cleaned).toContain('this');
  });
});
