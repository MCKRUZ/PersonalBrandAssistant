import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ManuscriptSurfaceComponent } from './manuscript-surface.component';
import { ContentDetail, ContentStatus, ContentType, Platform } from '../../models/content.model';

function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'm-1',
    title: 'My Title',
    body: '# My Title\n\nFirst real line of the body.',
    status: ContentStatus.Draft,
    contentType: ContentType.BlogPost,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 80,
    tags: ['angular', 'signals'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    children: [],
    ...overrides,
  };
}

describe('ManuscriptSurfaceComponent', () => {
  let fixture: ComponentFixture<ManuscriptSurfaceComponent>;
  let component: ManuscriptSurfaceComponent;

  function setup(content: ContentDetail, canEdit = true) {
    TestBed.configureTestingModule({
      imports: [ManuscriptSurfaceComponent],
      schemas: [NO_ERRORS_SCHEMA],
    });
    fixture = TestBed.createComponent(ManuscriptSurfaceComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('canEdit', canEdit);
    fixture.detectChanges();
  }

  it('emits titleChange when the title is edited', () => {
    setup(mockContent());
    let emitted: string | undefined;
    component.titleChange.subscribe((v) => (emitted = v));
    component.onTitleInput('A New Title');
    expect(emitted).toBe('A New Title');
  });

  it('emits bodyChange when the prose editor emits a new value', () => {
    setup(mockContent());
    let emitted: string | undefined;
    component.bodyChange.subscribe((v) => (emitted = v));
    component.onBodyChange('# Updated body');
    expect(emitted).toBe('# Updated body');
  });

  it('renders the prose editor for Draft status', () => {
    setup(mockContent({ status: ContentStatus.Draft }));
    expect(fixture.nativeElement.querySelector('app-prose-editor')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="idea-panel"]')).toBeFalsy();
  });

  it('renders the dashed idea panel + Start draft for Idea status (no prose editor)', () => {
    setup(mockContent({ status: ContentStatus.Idea }));
    const panel = fixture.nativeElement.querySelector('[data-testid="idea-panel"]');
    expect(panel).toBeTruthy();
    expect(panel.textContent.toLowerCase()).toContain('still just an idea');
    expect(fixture.nativeElement.querySelector('app-prose-editor')).toBeFalsy();
  });

  it('Start draft button emits startDraft', () => {
    setup(mockContent({ status: ContentStatus.Idea }));
    let fired = false;
    component.startDraft.subscribe(() => (fired = true));
    const btn = fixture.nativeElement.querySelector('[data-testid="start-draft-btn"]') as HTMLElement;
    btn.click();
    expect(fired).toBeTrue();
  });

  it('the derived subtitle is display-only and never emitted as a field change', () => {
    setup(mockContent());
    const titleSpy = jasmine.createSpy('titleChange');
    const bodySpy = jasmine.createSpy('bodyChange');
    component.titleChange.subscribe(titleSpy);
    component.bodyChange.subscribe(bodySpy);

    // The subtitle is a computed display string; touching it must not emit any change.
    const subtitle = component.subtitle();
    expect(typeof subtitle).toBe('string');
    expect(titleSpy).not.toHaveBeenCalled();
    expect(bodySpy).not.toHaveBeenCalled();

    const subtitleEl = fixture.nativeElement.querySelector('[data-testid="subtitle"]');
    expect(subtitleEl).toBeTruthy();
  });

  it('renders tag chips for the content tags', () => {
    setup(mockContent({ tags: ['alpha', 'beta'] }));
    const chips = fixture.nativeElement.querySelectorAll('[data-testid="tag-chip"]');
    const labels = Array.from(chips).map((c: any) => c.textContent);
    expect(labels.some((l) => l.includes('alpha'))).toBeTrue();
    expect(labels.some((l) => l.includes('beta'))).toBeTrue();
  });
});
