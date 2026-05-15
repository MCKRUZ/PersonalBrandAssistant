import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EditorToolbarComponent, DraftActionEvent } from './editor-toolbar.component';
import { ContentStatus } from '../../models/content.model';
import { ComponentRef } from '@angular/core';

describe('EditorToolbarComponent', () => {
  let fixture: ComponentFixture<EditorToolbarComponent>;
  let component: EditorToolbarComponent;
  let componentRef: ComponentRef<EditorToolbarComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EditorToolbarComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(EditorToolbarComponent);
    component = fixture.componentInstance;
    componentRef = fixture.componentRef;
  });

  it('should render AI action chips', () => {
    componentRef.setInput('status', ContentStatus.Draft);
    fixture.detectChanges();
    const toolbar = fixture.nativeElement.querySelector('[data-testid="editor-toolbar"]');
    expect(toolbar).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-draft"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-refine"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-shorten"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-expand"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-tone"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="chip-crosspost"]')).toBeTruthy();
  });

  it('should emit draftAction with correct action string on chip click', () => {
    componentRef.setInput('status', ContentStatus.Draft);
    componentRef.setInput('hasBody', true);
    fixture.detectChanges();

    let emitted: DraftActionEvent | undefined;
    component.draftAction.subscribe((e: DraftActionEvent) => (emitted = e));

    const refineBtn = fixture.nativeElement.querySelector('[data-testid="chip-refine"] button');
    refineBtn.click();

    expect(emitted).toEqual({ action: 'refine' });
  });

  it('should emit draftAction with changeTone action', () => {
    componentRef.setInput('status', ContentStatus.Draft);
    componentRef.setInput('hasBody', true);
    fixture.detectChanges();

    let emitted: DraftActionEvent | undefined;
    component.draftAction.subscribe((e: DraftActionEvent) => (emitted = e));

    const toneBtn = fixture.nativeElement.querySelector('[data-testid="chip-tone"] button');
    toneBtn.click();

    expect(emitted).toEqual({ action: 'changeTone' });
  });

  it('should show loading state when isLoading is true', () => {
    componentRef.setInput('isLoading', true);
    componentRef.setInput('status', ContentStatus.Draft);
    componentRef.setInput('hasBody', true);
    fixture.detectChanges();

    const refineHost = fixture.nativeElement.querySelector('[data-testid="chip-refine"]');
    expect(refineHost.querySelector('button').disabled).toBeTrue();
  });

  it('should disable chips when content status is Published', () => {
    componentRef.setInput('status', ContentStatus.Published);
    componentRef.setInput('hasBody', true);
    fixture.detectChanges();

    const refineHost = fixture.nativeElement.querySelector('[data-testid="chip-refine"]');
    expect(refineHost.querySelector('button').disabled).toBeTrue();
  });

  it('should emit crossPostAction on Cross-Post chip click', () => {
    componentRef.setInput('status', ContentStatus.Draft);
    componentRef.setInput('hasBody', true);
    fixture.detectChanges();

    let emitted = false;
    component.crossPostAction.subscribe(() => (emitted = true));

    const crossBtn = fixture.nativeElement.querySelector('[data-testid="chip-crosspost"] button');
    crossBtn.click();

    expect(emitted).toBeTrue();
  });
});
