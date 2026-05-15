import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MarkdownEditorComponent } from './markdown-editor.component';
import { ComponentRef, NO_ERRORS_SCHEMA } from '@angular/core';

describe('MarkdownEditorComponent', () => {
  let fixture: ComponentFixture<MarkdownEditorComponent>;
  let component: MarkdownEditorComponent;
  let componentRef: ComponentRef<MarkdownEditorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MarkdownEditorComponent],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(MarkdownEditorComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  it('should create the component', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should accept value input', () => {
    componentRef.setInput('value', '# Hello');
    fixture.detectChanges();
    expect(component.value()).toBe('# Hello');
  });

  it('should emit valueChange on content change', () => {
    fixture.detectChanges();
    let emitted: string | undefined;
    component.valueChange.subscribe((v: string) => (emitted = v));

    component.onValueChange('# Updated');
    expect(emitted).toBe('# Updated');
  });

  it('should have readOnly default to false', () => {
    fixture.detectChanges();
    expect(component.readOnly()).toBeFalse();
  });
});
