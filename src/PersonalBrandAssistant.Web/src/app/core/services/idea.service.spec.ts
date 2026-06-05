import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { IdeaService } from './idea.service';
import { IdeaStatus, IdeaSourceType } from '../../models/idea.model';
import type { Idea, IdeaDetail, IdeaConnection, IdeaSource } from '../../models/idea.model';
import type { PagedResult } from '../../models/pagination.model';

describe('IdeaService', () => {
  let service: IdeaService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [IdeaService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(IdeaService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('list() sends GET /api/ideas with query params', () => {
    const mockResult: PagedResult<Idea> = {
      items: [],
      totalCount: 0,
      page: 2,
      pageSize: 20,
      totalPages: 0,
    };

    service
      .list({ status: IdeaStatus.Saved, category: 'Tech' }, 2, 20, {
        field: 'detectedAt',
        direction: 'desc',
      })
      .subscribe((result) => {
        expect(result).toEqual(mockResult);
      });

    const req = httpMock.expectOne(
      (r) => r.url === '/api/ideas' && r.params.get('status') === 'Saved'
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    expect(req.request.params.get('category')).toBe('Tech');
    expect(req.request.params.get('sortBy')).toBe('detectedAt');
    expect(req.request.params.get('sortDirection')).toBe('desc');
    req.flush(mockResult);
  });

  it('list() sends tags as repeated query params', () => {
    service
      .list({ tags: ['AI', 'ML'] }, 1, 20, { field: 'detectedAt', direction: 'desc' })
      .subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/ideas');
    expect(req.request.params.getAll('tags')).toEqual(['AI', 'ML']);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 });
  });

  it('list() omits null filter values from query params', () => {
    service.list({}, 1, 20, { field: 'detectedAt', direction: 'desc' }).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/ideas');
    expect(req.request.params.has('status')).toBeFalse();
    expect(req.request.params.has('category')).toBeFalse();
    expect(req.request.params.has('searchText')).toBeFalse();
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 });
  });

  it('getById() sends GET /api/ideas/{id}', () => {
    const mockDetail: IdeaDetail = {
      id: 'abc-123',
      title: 'Test Idea',
      sourceName: 'Manual',
      category: null,
      summary: null,
      thumbnailUrl: null,
      status: IdeaStatus.New,
      tags: [],
      detectedAt: '2026-01-01T00:00:00Z',
      hasSavedDetails: false,
      description: null,
      url: null,
      score: null,
      scoreReason: null,
      isDuplicate: false,
      aiConnections: null,
      savedDetails: null,
      sourceInfo: null,
    };

    service.getById('abc-123').subscribe((result) => {
      expect(result).toEqual(mockDetail);
    });

    const req = httpMock.expectOne('/api/ideas/abc-123');
    expect(req.request.method).toBe('GET');
    req.flush(mockDetail);
  });

  it('create() sends POST /api/ideas with body', () => {
    service.create({ title: 'New Idea', category: 'Tech' }).subscribe((id) => {
      expect(id).toBe('new-id-123');
    });

    const req = httpMock.expectOne('/api/ideas');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ title: 'New Idea', category: 'Tech' });
    req.flush('new-id-123');
  });

  it('save() sends PUT /api/ideas/{id}/save with body', () => {
    service.save('abc-123', 'Test notes', ['tag1']).subscribe();

    const req = httpMock.expectOne('/api/ideas/abc-123/save');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ notes: 'Test notes', tags: ['tag1'] });
    req.flush(null);
  });

  it('dismiss() sends PUT /api/ideas/{id}/dismiss', () => {
    service.dismiss('abc-123').subscribe();

    const req = httpMock.expectOne('/api/ideas/abc-123/dismiss');
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('createContent() sends POST /api/ideas/{id}/create-content', () => {
    service.createContent('abc-123', 'BlogPost', 'LinkedIn').subscribe((id) => {
      expect(id).toBe('content-id-456');
    });

    const req = httpMock.expectOne('/api/ideas/abc-123/create-content');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      contentType: 'BlogPost',
      primaryPlatform: 'LinkedIn',
    });
    req.flush('content-id-456');
  });

  it('getConnections() sends GET /api/ideas/connections', () => {
    const mockConnections: IdeaConnection[] = [
      { theme: 'AI', relatedIdeaIds: ['1', '2'], suggestedAngle: 'Compare', confidence: 0.8 },
    ];

    service.getConnections().subscribe((result) => {
      expect(result).toEqual(mockConnections);
    });

    const req = httpMock.expectOne('/api/ideas/connections');
    expect(req.request.method).toBe('GET');
    req.flush(mockConnections);
  });

  it('listSources() sends GET /api/idea-sources', () => {
    const mockSources: IdeaSource[] = [];

    service.listSources().subscribe((result) => {
      expect(result).toEqual(mockSources);
    });

    const req = httpMock.expectOne('/api/idea-sources');
    expect(req.request.method).toBe('GET');
    req.flush(mockSources);
  });

  it('createSource() sends POST /api/idea-sources', () => {
    const body = {
      name: 'Test',
      type: IdeaSourceType.RSS,
      feedUrl: 'https://example.com/rss',
      category: 'Tech',
      pollIntervalMinutes: 30,
    };

    service.createSource(body).subscribe((id) => {
      expect(id).toBe('source-id');
    });

    const req = httpMock.expectOne('/api/idea-sources');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush('source-id');
  });

  it('updateSource() sends PUT /api/idea-sources/{id}', () => {
    service.updateSource('src-123', { name: 'Updated' }).subscribe();

    const req = httpMock.expectOne('/api/idea-sources/src-123');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Updated' });
    req.flush(null);
  });

  it('deleteSource() sends DELETE /api/idea-sources/{id}', () => {
    service.deleteSource('src-123').subscribe();

    const req = httpMock.expectOne('/api/idea-sources/src-123');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('refreshSources() sends POST /api/idea-sources/refresh', () => {
    service.refreshSources().subscribe((count) => {
      expect(count).toBe(5);
    });

    const req = httpMock.expectOne('/api/idea-sources/refresh');
    expect(req.request.method).toBe('POST');
    req.flush(5);
  });

  it('propagates HTTP errors', () => {
    service.getById('bad-id').subscribe({
      error: (err) => {
        expect(err.status).toBe(404);
      },
    });

    const req = httpMock.expectOne('/api/ideas/bad-id');
    req.flush('Not Found', { status: 404, statusText: 'Not Found' });
  });
});
