import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter, ActivatedRoute, Router } from '@angular/router';
import { of, EMPTY } from 'rxjs';
import { signal, NO_ERRORS_SCHEMA } from '@angular/core';
import { provideMarkdown } from 'ngx-markdown';
import { ContentEditorComponent } from './content-editor.component';
import { ContentEditorStore } from '../stores/content-editor.store';
import { ContentService } from '../services/content.service';
import { SignalRService } from '../services/signalr.service';
import { ContentDetail, ContentStatus, ContentType, Platform } from '../models/content.model';

function mockContent(overrides: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'abc-123',
    title: 'Test Post',
    body: '# Hello',
    status: ContentStatus.Draft,
    contentType: ContentType.BlogPost,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 85,
    tags: ['angular'],
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

  function setup(
    routeParams: Record<string, string> = { id: 'abc-123' },
    content?: ContentDetail,
    queryParams: Record<string, string> = {},
  ) {
    contentSignal.set(content ?? mockContent());
    loadingSignal.set(false);
    isDirtySignal.set(false);
    isSavingSignal.set(false);

    contentService = jasmine.createSpyObj('ContentService', [
      'create', 'get', 'update', 'delete', 'draft', 'crossPost',
      'approve', 'submitForReview', 'requestChanges', 'schedule',
      'unschedule', 'publish', 'unpublish', 'restore', 'voiceCheck',
      'getPlatforms', 'getPublishStatus', 'retryPlatform',
    ]);
    contentService.create.and.returnValue(of('new-id-1'));
    contentService.approve.and.returnValue(of(void 0));
    contentService.draft.and.returnValue(of(void 0));
    contentService.getPlatforms.and.returnValue(of([]));

    const mockSignalR = jasmine.createSpyObj('SignalRService',
      ['connect', 'disconnect', 'sendChatMessage'],
      { tokens$: EMPTY, generationComplete$: EMPTY, generationError$: EMPTY },
    );
    mockSignalR.connect.and.returnValue(Promise.resolve());
    mockSignalR.disconnect.and.returnValue(Promise.resolve());
    mockSignalR.sendChatMessage.and.returnValue(Promise.resolve());

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
              queryParamMap: {
                has: (key: string) => key in queryParams,
                get: (key: string) => queryParams[key] ?? null,
              },
            },
          },
        },
        { provide: ContentService, useValue: contentService },
        { provide: SignalRService, useValue: mockSignalR },
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

  it('loads content on init when route has id param (edit mode)', () => {
    setup({ id: 'abc-123' });
    expect(mockStore.loadContent).toHaveBeenCalledWith('abc-123');
  });

  it('has no p-splitter and no app-markdown-editor; app-manuscript-surface is present', () => {
    setup();
    expect(fixture.nativeElement.querySelector('p-splitter')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('app-markdown-editor')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('markdown')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('app-manuscript-surface')).toBeTruthy();
  });

  it('keeps the publish modal open after confirm (so its result view can show)', () => {
    setup();
    contentService.publish.and.returnValue(of(void 0));
    component.publishModalVisible.set(true);
    component.onPublishConfirm({ platforms: [], scheduledAt: undefined });
    expect(component.publishModalVisible()).toBeTrue();
  });

  it('seeds create() from topic/type/sourceIdeaId query params on /content/new', () => {
    setup(
      {},
      undefined,
      { topic: 'My great topic', type: 'LinkedInPost', sourceIdeaId: 'idea-9' },
    );
    expect(contentService.create).toHaveBeenCalledWith(jasmine.objectContaining({
      title: 'My great topic',
      contentType: 'LinkedInPost' as ContentType,
      sourceIdeaId: 'idea-9',
    }));
    expect(router.navigate).toHaveBeenCalledWith(['/content', 'new-id-1']);
  });

  it('falls back to Untitled/Blog when no query params are present on /content/new', () => {
    setup({}, undefined, {});
    expect(contentService.create).toHaveBeenCalledWith(jasmine.objectContaining({
      title: 'Untitled',
      contentType: ContentType.BlogPost,
      primaryPlatform: Platform.Blog,
    }));
    const arg = contentService.create.calls.mostRecent().args[0] as unknown as Record<string, unknown>;
    expect('sourceIdeaId' in arg).toBeFalse();
  });

  it('triggers autosave debounce on body change for Draft status', fakeAsync(() => {
    setup({}, mockContent({ status: ContentStatus.Draft }));
    component.onBodyChange('# Updated content');
    tick(3000);
    expect(mockStore.autoSave).toHaveBeenCalled();
  }));

  it('does NOT autosave for Approved status', fakeAsync(() => {
    setup({}, mockContent({ status: ContentStatus.Approved }));
    component.onBodyChange('# Updated content');
    tick(3000);
    expect(mockStore.autoSave).not.toHaveBeenCalled();
  }));

  it('does NOT autosave for Published status', fakeAsync(() => {
    setup({}, mockContent({ status: ContentStatus.Published }));
    component.onBodyChange('# Updated content');
    tick(3000);
    expect(mockStore.autoSave).not.toHaveBeenCalled();
  }));

  it('renders the action bar for Draft status with Save/Submit/Approve actions', () => {
    setup({}, mockContent({ status: ContentStatus.Draft }));
    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] .btn'),
    ).map((b: any) => b.textContent.trim());
    expect(labels).toContain('Save Draft');
    expect(labels).toContain('Approve');
    expect(labels).toContain('Submit for Review');
  });

  it('renders the action bar for Approved status with Schedule/Publish', () => {
    setup({}, mockContent({ status: ContentStatus.Approved }));
    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] .btn'),
    ).map((b: any) => b.textContent.trim());
    expect(labels).toContain('Schedule');
    expect(labels).toContain('Publish Now');
  });

  it('renders the Restore action for Archived status', () => {
    setup({}, mockContent({ status: ContentStatus.Archived }));
    const labels = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="action-bar"] .btn'),
    ).map((b: any) => b.textContent.trim());
    expect(labels).toContain('Restore');
  });

  it('Assistant toggle (panelOpen) shows and hides the side panel', () => {
    setup();
    expect(component.panelOpen()).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="side-panel"]')).toBeTruthy();

    component.panelOpen.set(false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="side-panel"]')).toBeFalsy();
  });
});
