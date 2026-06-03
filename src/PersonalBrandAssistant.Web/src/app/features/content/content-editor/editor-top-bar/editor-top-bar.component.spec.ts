import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { EditorTopBarComponent } from './editor-top-bar.component';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';

describe('EditorTopBarComponent', () => {
  let fixture: ComponentFixture<EditorTopBarComponent>;
  let component: EditorTopBarComponent;
  let router: Router;

  function setup(inputs: Record<string, unknown> = {}) {
    TestBed.configureTestingModule({
      imports: [EditorTopBarComponent],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(EditorTopBarComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));

    const ref = fixture.componentRef;
    ref.setInput('status', inputs['status'] ?? ContentStatus.Draft);
    ref.setInput('contentType', inputs['contentType'] ?? ContentType.BlogPost);
    ref.setInput('primaryPlatform', inputs['primaryPlatform'] ?? Platform.Blog);
    ref.setInput('voiceScore', inputs['voiceScore'] ?? 85);
    ref.setInput('isSaving', inputs['isSaving'] ?? false);
    ref.setInput('isDirty', inputs['isDirty'] ?? false);
    ref.setInput('panelOpen', inputs['panelOpen'] ?? true);
    fixture.detectChanges();
  }

  function saveText(): string {
    return fixture.nativeElement.querySelector('[data-testid="save-indicator"]').textContent.trim();
  }

  it('navigates back to /content when the back control is clicked', () => {
    setup();
    const back = fixture.nativeElement.querySelector('[data-testid="back-to-studio"]') as HTMLElement;
    back.click();
    expect(router.navigate).toHaveBeenCalledWith(['/content']);
  });

  it('passes the current status to the stage tracker', () => {
    setup({ status: ContentStatus.Review });
    expect(fixture.nativeElement.querySelector('app-stage-tracker')).toBeTruthy();
    expect(component.status()).toBe(ContentStatus.Review);
  });

  it('shows Saving... when isSaving is true', () => {
    setup({ isSaving: true });
    expect(saveText()).toBe('Saving...');
  });

  it('shows Unsaved when dirty and not saving', () => {
    setup({ isSaving: false, isDirty: true });
    expect(saveText()).toBe('Unsaved');
  });

  it('shows Saved when neither saving nor dirty', () => {
    setup({ isSaving: false, isDirty: false });
    expect(saveText()).toBe('Saved');
  });

  it('renders the voice score ring with the voice score', () => {
    setup({ voiceScore: 72 });
    expect(fixture.nativeElement.querySelector('app-voice-score-ring')).toBeTruthy();
  });

  it('emits togglePanel when the Assistant toggle is clicked', () => {
    setup({ panelOpen: false });
    let emitted = false;
    component.togglePanel.subscribe(() => (emitted = true));
    const toggle = fixture.nativeElement.querySelector('[data-testid="assistant-toggle"]') as HTMLElement;
    toggle.click();
    expect(emitted).toBeTrue();
  });

  it('renders the type and platform meta text', () => {
    setup({ contentType: ContentType.BlogPost, primaryPlatform: Platform.LinkedIn });
    const meta = fixture.nativeElement.querySelector('[data-testid="type-platform-meta"]');
    expect(meta.textContent).toContain('Blog');
    expect(meta.textContent).toContain('LinkedIn');
  });
});
