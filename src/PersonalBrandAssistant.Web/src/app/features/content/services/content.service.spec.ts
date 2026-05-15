import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ContentService } from './content.service';
import {
  Content,
  ContentDetail,
  ContentStatus,
  ContentType,
  Platform,
  VoiceCheckResult,
} from '../models/content.model';
import { PagedResult } from '../../../models/pagination.model';

describe('ContentService', () => {
  let service: ContentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [ContentService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ContentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('list() calls GET /api/content with correct query params', () => {
    const mockResult: PagedResult<Content> = {
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 20,
      totalPages: 0,
    };

    service
      .list({ status: ContentStatus.Draft, search: 'test' }, 2, 10)
      .subscribe((result) => {
        expect(result).toEqual(mockResult);
      });

    const req = httpMock.expectOne((r) => r.url === '/api/content');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    expect(req.request.params.get('status')).toBe('Draft');
    expect(req.request.params.get('search')).toBe('test');
    req.flush(mockResult);
  });

  it('list() omits null/undefined filter params', () => {
    service.list({}, 1, 20).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/content');
    expect(req.request.params.has('status')).toBeFalse();
    expect(req.request.params.has('platform')).toBeFalse();
    expect(req.request.params.has('contentType')).toBeFalse();
    expect(req.request.params.has('search')).toBeFalse();
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 });
  });

  it('get() calls GET /api/content/{id}', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const mockDetail: ContentDetail = {
      id,
      title: 'Test',
      contentType: ContentType.BlogPost,
      status: ContentStatus.Draft,
      primaryPlatform: Platform.Blog,
      voiceScore: null,
      tags: [],
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      scheduledAt: null,
      publishedAt: null,
      body: '',
      viralityPrediction: null,
      sourceIdeaId: null,
      parentContentId: null,
      platformPublishes: [],
      children: [],
    };

    service.get(id).subscribe((result) => {
      expect(result).toEqual(mockDetail);
    });

    const req = httpMock.expectOne(`/api/content/${id}`);
    expect(req.request.method).toBe('GET');
    req.flush(mockDetail);
  });

  it('create() calls POST /api/content with body', () => {
    const body = {
      title: 'New Post',
      contentType: ContentType.BlogPost,
      primaryPlatform: Platform.Blog,
      tags: ['ai'],
    };

    service.create(body).subscribe((result) => {
      expect(result).toBe('new-id');
    });

    const req = httpMock.expectOne('/api/content');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush('new-id');
  });

  it('update() calls PUT /api/content/{id} with lastUpdatedAt', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const body = { title: 'Updated', lastUpdatedAt: '2026-01-01T00:00:00Z' };

    service.update(id, body).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body.lastUpdatedAt).toBe('2026-01-01T00:00:00Z');
    req.flush(null);
  });

  it('delete() calls DELETE /api/content/{id}', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.delete(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('draft() calls POST /api/content/{id}/draft with action and instructions', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const body = { action: 'generate', instructions: 'Write about AI' };

    service.draft(id, body).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/draft`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush(null);
  });

  it('crossPost() calls POST /api/content/{id}/cross-post', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const body = { targetPlatform: Platform.LinkedIn };

    service.crossPost(id, body).subscribe((result) => {
      expect(result).toBe('cross-post-id');
    });

    const req = httpMock.expectOne(`/api/content/${id}/cross-post`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush('cross-post-id');
  });

  it('approve() calls PUT /api/content/{id}/approve', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.approve(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/approve`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('submitForReview() calls PUT /api/content/{id}/submit-review', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.submitForReview(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/submit-review`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('requestChanges() calls PUT /api/content/{id}/request-changes', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.requestChanges(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/request-changes`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('schedule() calls PUT /api/content/{id}/schedule with scheduledAt', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const body = { scheduledAt: '2026-06-01T12:00:00Z' };

    service.schedule(id, body).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/schedule`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
    req.flush(null);
  });

  it('unschedule() calls PUT /api/content/{id}/unschedule', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.unschedule(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/unschedule`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('publish() calls POST /api/content/{id}/publish', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.publish(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/publish`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('unpublish() calls PUT /api/content/{id}/unpublish', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.unpublish(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/unpublish`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('restore() calls PUT /api/content/{id}/restore', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.restore(id).subscribe();

    const req = httpMock.expectOne(`/api/content/${id}/restore`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('voiceCheck() calls GET /api/content/{id}/voice-check', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';
    const mockResult: VoiceCheckResult = { score: 0.85, feedback: 'Sounds like you' };

    service.voiceCheck(id).subscribe((result) => {
      expect(result).toEqual(mockResult);
    });

    const req = httpMock.expectOne(`/api/content/${id}/voice-check`);
    expect(req.request.method).toBe('GET');
    req.flush(mockResult);
  });
});
