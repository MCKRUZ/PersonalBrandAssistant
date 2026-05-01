import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ContentEditorStore } from './content-editor.store';
import { ContentEditorApiService } from './content-editor-api.service';
import { environment } from '../../environments/environment';

describe('ContentEditorStore', () => {
  let store: InstanceType<typeof ContentEditorStore>;
  let httpMock: HttpTestingController;

  const mockContent = {
    id: 'abc-123',
    title: 'Test Post',
    body: 'Hello world',
    type: 'SocialPost' as const,
    status: 'Draft' as const,
    platform: 'LinkedIn' as const,
    createdAt: '2026-04-30T08:00:00Z',
    updatedAt: '2026-04-30T08:00:00Z',
    version: 1,
    capturedAutonomyLevel: 'Manual' as const,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ContentEditorStore,
        ContentEditorApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(ContentEditorStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should have correct initial state', () => {
    expect(store.content()).toBeUndefined();
    expect(store.brandScore()).toBeUndefined();
    expect(store.isLoading()).toBe(false);
    expect(store.isSaving()).toBe(false);
    expect(store.isScoring()).toBe(false);
    expect(store.saveError()).toBeNull();
    expect(store.activeTab()).toBe('preview');
  });

  it('should load content by ID', () => {
    store.loadContent('abc-123');
    expect(store.isLoading()).toBe(true);

    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    expect(store.isLoading()).toBe(false);
    expect(store.content()?.id).toBe('abc-123');
    expect(store.content()?.title).toBe('Test Post');
  });

  it('should create content and load the result', () => {
    store.createContent({ title: 'New', body: 'text', type: 'SocialPost', platform: 'LinkedIn' });
    expect(store.isLoading()).toBe(true);

    httpMock.expectOne(`${environment.apiUrl}/content`).flush({ id: 'new-id' });
    httpMock.expectOne(`${environment.apiUrl}/content/new-id`).flush({ ...mockContent, id: 'new-id' });

    expect(store.isLoading()).toBe(false);
    expect(store.content()?.id).toBe('new-id');
  });

  it('should auto-save body changes with debounce', fakeAsync(() => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.updateField('body', 'updated text');
    expect(store.content()?.body).toBe('updated text');

    tick(500);
    httpMock.expectNone(`${environment.apiUrl}/content/abc-123`);

    tick(500);
    const req = httpMock.expectOne(`${environment.apiUrl}/content/abc-123`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.headers.get('If-Match')).toBe('"1"');
    req.flush(null);

    expect(store.isSaving()).toBe(false);
    expect(store.content()?.version).toBe(2);
  }));

  it('should not auto-save when content has no ID', fakeAsync(() => {
    store.updateField('body', 'some text');
    tick(2000);
    httpMock.expectNone(`${environment.apiUrl}/content`);
  }));

  it('should handle 409 conflict on save', fakeAsync(() => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.updateField('body', 'conflict text');
    tick(1000);

    const req = httpMock.expectOne(`${environment.apiUrl}/content/abc-123`);
    req.flush(null, { status: 409, statusText: 'Conflict' });

    expect(store.saveError()).toBe('conflict');
    expect(store.isSaving()).toBe(false);
  }));

  it('should score content', () => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.scoreContent();
    expect(store.isScoring()).toBe(true);

    const score = {
      overallScore: 85,
      authoritative: 90, pragmatic: 80, concise: 75, practitioner: 85,
      issues: ['Too casual'], ruleViolations: [],
    };
    httpMock.expectOne(`${environment.apiUrl}/brand-voice/score`).flush(score);

    expect(store.isScoring()).toBe(false);
    expect(store.brandScore()?.overallScore).toBe(85);
    expect(store.brandScore()?.authoritative).toBe(90);
  });

  it('should approve and publish', () => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.approveAndPublish();

    httpMock.expectOne(`${environment.apiUrl}/approval/abc-123/approve`).flush(null);
    httpMock.expectOne(`${environment.apiUrl}/content-pipeline/abc-123/publish`).flush(null);

    expect(store.content()?.status).toBe('Published');
  });

  it('should schedule content', () => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.scheduleContent('2026-05-15T10:00:00Z');
    httpMock.expectOne(`${environment.apiUrl}/scheduling/abc-123/schedule`).flush(null);

    expect(store.content()?.status).toBe('Scheduled');
    expect(store.content()?.scheduledAt).toBe('2026-05-15T10:00:00Z');
  });

  it('should set active tab', () => {
    store.setActiveTab('history');
    expect(store.activeTab()).toBe('history');
  });

  it('should apply draft from sidecar', fakeAsync(() => {
    store.loadContent('abc-123');
    httpMock.expectOne(`${environment.apiUrl}/content/abc-123`).flush(mockContent);

    store.applyDraft('new draft text');
    expect(store.content()?.body).toBe('new draft text');

    tick(1000);
    const req = httpMock.expectOne(`${environment.apiUrl}/content/abc-123`);
    expect(req.request.body).toEqual({ body: 'new draft text' });
    req.flush(null);
  }));
});
