import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { signal, NO_ERRORS_SCHEMA } from '@angular/core';
import { provideMarkdown } from 'ngx-markdown';
import { ContentEditorComponent } from './content-editor.component';
import { ContentEditorStore } from '../stores/content-editor.store';
import { ContentService } from '../services/content.service';
import { ContentDetail, ContentStatus, ContentType, Platform } from '../models/content.model';

function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'abc-123',
    title: 'Test Post',
    body: '# Hello',
    status: ContentStatus.Draft,
    contentType: ContentType.BlogPost,
    primaryPlatform: Platform.Blog,
    voiceScore: 85,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    platformPublishes: [],
    children: [],
    ...overrides,
  };
}

describe('ContentEditorComponent', () => {
  let fixture: ComponentFixture<ContentEditorComponent>;
  let component: ContentEditorComponent;
  let contentService: jasmine.SpyObj<ContentService>;
  let router: Router;

  const contentSignal = signal<ContentDetail | null>(null);
  const loadingSignal = signal(false);
  const isDirtySignal = signal(false);
  const isSavingSignal = signal(false);
  const isStreamingSignal = signal(false);
  const errorSignal = signal<string | null>(null);

  const mockStore = {
    content: contentSignal,
    loading: loadingSignal,
    isDirty: isDirtySignal,
    isSaving: isSavingSignal,
    isStreaming: isStreamingSignal,
    error: errorSignal,
    hasContent: signal(false),
    canAutoSave: signal(false),
    statusActions: signal([] as string[]),
    chatMessages: signal([]),
    currentTokens: signal(''),
    loadContent: jasmine.createSpy('loadContent'),
    updateField: jasmine.createSpy('updateField'),
    autoSave: jasmine.createSpy('autoSave'),
    applyToEditor: jasmine.createSpy('applyToEditor'),
    reset: jasmine.createSpy('reset'),
    addChatMessage: jasmine.createSpy('addChatMessage'),
    appendToken: jasmine.createSpy('appendToken'),
    completeGeneration: jasmine.createSpy('completeGeneration'),
  };

  function setup(routeParams: Record<string, string> = { id: 'abc-123' }, content?: ContentDetail) {
    contentSignal.set(content ?? mockContent());
    loadingSignal.set(false);
    isDirtySignal.set(false);
    isSavingSignal.set(false);

    contentService = jasmine.createSpyObj('ContentService', [
      'create', 'get', 'update', 'delete', 'draft', 'crossPost',
      'approve', 'submitForReview', 'requestChanges', 'schedule',
      'unschedule', 'publish', 'unpublish', 'restore', 'voiceCheck',
    ]);
    contentService.create.and.returnValue(of('new-id-1'));
    contentService.approve.and.returnValue(of(void 0));
    contentService.draft.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [ContentEditorComponent],
      schemas: [NO_ERRORS_SCHEMA],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideMarkdown(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                has: (key: string) => key in routeParams,
                get: (key: string) => routeParams[key] ?? null,
              },
            },
          },
        },
        { provide: ContentService, useValue: contentService },
      ],
    });

    TestBed.overrideComponent(ContentEditorComponent, {
      add: {
        providers: [{ provide: ContentEditorStore, useValue: mockStore }],
      },
      remove: {
        providers: [ContentEditorStore],
      },
    });

    fixture = TestBed.createComponent(ContentEditorComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.returnValue(Promise.resolve(true));
    fixture.detectChanges();
  }

  afterEach(() => {
    mockStore.loadContent.calls.reset();
    mockStore.updateField.calls.reset();
    mockStore.autoSave.calls.reset();
    mockStore.reset.calls.reset();
  });

  it('should load content on init when route has id param (edit mode)', () => {
    setup({ id: 'abc-123' });
    expect(mockStore.loadContent).toHaveBeenCalledWith('abc-123');
  });

  it('should create content on init when route is /content/new (new mode)', () => {
    setup({});
    expect(contentService.create).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/content', 'new-id-1']);
  });

  it('should render platform selector dropdown', () => {
    setup();
    expect(fixture.nativeElement.querySelector('[data-testid="platform-selector"]')).toBeTruthy();
  });

  it('should render content type selector dropdown', () => {
    setup();
    expect(fixture.nativeElement.querySelector('[data-testid="type-selector"]')).toBeTruthy();
  });

  it('should render status badge reflecting current status', () => {
    setup();
    const badge = fixture.nativeElement.querySelector('[data-testid="status-badge"]');
    expect(badge).toBeTruthy();
    expect(badge.getAttribute('ng-reflect-value') ?? badge.textContent).toContain('Draft');
  });

  it('should render tags input', () => {
    setup();
    expect(fixture.nativeElement.querySelector('[data-testid="tags-input"]')).toBeTruthy();
  });

  it('should render voice score knob when score exists', () => {
    setup({}, mockContent({ voiceScore: 85 }));
    expect(fixture.nativeElement.querySelector('[data-testid="voice-knob"]')).toBeTruthy();
  });

  it('should render auto-save indicator showing Saved when not dirty', () => {
    setup();
    isDirtySignal.set(false);
    isSavingSignal.set(false);
    fixture.detectChanges();
    const indicator = fixture.nativeElement.querySelector('[data-testid="save-indicator"]');
    expect(indicator.textContent.trim()).toBe('Saved');
  });

  it('should render auto-save indicator showing Saving... when saving', () => {
    setup();
    isSavingSignal.set(true);
    fixture.detectChanges();
    const indicator = fixture.nativeElement.querySelector('[data-testid="save-indicator"]');
    expect(indicator.textContent.trim()).toBe('Saving...');
  });

  it('should render auto-save indicator showing Unsaved when dirty', () => {
    setup();
    isDirtySignal.set(true);
    isSavingSignal.set(false);
    fixture.detectChanges();
    const indicator = fixture.nativeElement.querySelector('[data-testid="save-indicator"]');
    expect(indicator.textContent.trim()).toBe('Unsaved changes');
  });

  it('should render bottom action bar with correct buttons for Draft status', () => {
    setup({}, mockContent({ status: ContentStatus.Draft }));
    const buttons = fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] p-button');
    const labels = Array.from(buttons).map((b: any) => b.getAttribute('ng-reflect-label') ?? b.getAttribute('label'));
    expect(labels).toContain('Save Draft');
    expect(labels).toContain('Approve');
    expect(labels).toContain('Submit for Review');
  });

  it('should render bottom action bar with correct buttons for Approved status', () => {
    setup({}, mockContent({ status: ContentStatus.Approved }));
    const buttons = fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] p-button');
    const labels = Array.from(buttons).map((b: any) => b.getAttribute('ng-reflect-label') ?? b.getAttribute('label'));
    expect(labels).toContain('Schedule');
    expect(labels).toContain('Publish Now');
  });

  it('should render bottom action bar with Restore button for Archived status', () => {
    setup({}, mockContent({ status: ContentStatus.Archived }));
    const buttons = fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] p-button');
    const labels = Array.from(buttons).map((b: any) => b.getAttribute('ng-reflect-label') ?? b.getAttribute('label'));
    expect(labels).toContain('Restore');
  });

  it('should call store.updateField when platform dropdown changes', () => {
    setup();
    component.onPlatformChange(Platform.LinkedIn);
    expect(mockStore.updateField).toHaveBeenCalledWith('primaryPlatform', Platform.LinkedIn);
  });

  it('should trigger auto-save debounce when editor content changes', fakeAsync(() => {
    setup({}, mockContent({ status: ContentStatus.Draft }));
    component.onBodyChange('# Updated content');
    tick(3000);
    expect(mockStore.autoSave).toHaveBeenCalled();
  }));

  it('should NOT auto-save when status is Published', fakeAsync(() => {
    setup({}, mockContent({ status: ContentStatus.Published }));
    component.onBodyChange('# Updated content');
    tick(3000);
    expect(mockStore.autoSave).not.toHaveBeenCalled();
  }));
});
