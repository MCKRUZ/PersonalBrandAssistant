import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { FeedService } from './feed.service';
import { FeedItemType, FeedItemPriority, FeedItem } from '../models/feed-item.model';
import { PagedResult } from '../../../models/pagination.model';

describe('FeedService', () => {
  let service: FeedService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [FeedService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FeedService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });

  it('list() sends GET /api/feed with correct query params', () => {
    const mockResult: PagedResult<FeedItem> = {
      items: [],
      totalCount: 0,
      page: 2,
      pageSize: 10,
      totalPages: 0,
    };

    service
      .list({
        page: 2,
        pageSize: 10,
        type: FeedItemType.TrendAlert,
        priority: FeedItemPriority.High,
        isRead: false,
        sortBy: 'CreatedAt',
        sortDirection: 'desc',
      })
      .subscribe((result) => {
        expect(result).toEqual(mockResult);
      });

    const req = httpMock.expectOne((r) => r.url === '/api/feed');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('10');
    expect(req.request.params.get('type')).toBe('TrendAlert');
    expect(req.request.params.get('priority')).toBe('High');
    expect(req.request.params.get('isRead')).toBe('false');
    expect(req.request.params.get('sortBy')).toBe('CreatedAt');
    expect(req.request.params.get('sortDirection')).toBe('desc');
    req.flush(mockResult);
  });

  it('list() omits null/undefined filter params', () => {
    service.list({}).subscribe();

    const req = httpMock.expectOne((r) => r.url === '/api/feed');
    expect(req.request.params.keys().length).toBe(0);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20, totalPages: 0 });
  });

  it('getSummary() sends GET /api/feed/summary', () => {
    const mockSummary = { unreadCount: 5, pendingApprovals: 2, trendingCount: 3, engagementDelta: 12.5 };

    service.getSummary().subscribe((result) => {
      expect(result).toEqual(mockSummary);
    });

    const req = httpMock.expectOne('/api/feed/summary');
    expect(req.request.method).toBe('GET');
    req.flush(mockSummary);
  });

  it('getTrending() sends GET /api/feed/trending', () => {
    const mockTopics = [{ topic: 'AI', count: 10, latestAt: '2026-05-15T00:00:00Z' }];

    service.getTrending().subscribe((result) => {
      expect(result).toEqual(mockTopics);
    });

    const req = httpMock.expectOne('/api/feed/trending');
    expect(req.request.method).toBe('GET');
    req.flush(mockTopics);
  });

  it('markRead() sends PUT /api/feed/{id}/read', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.markRead(id).subscribe();

    const req = httpMock.expectOne(`/api/feed/${id}/read`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('actOnItem() sends PUT /api/feed/{id}/act with action body', () => {
    const id = '123e4567-e89b-12d3-a456-426614174000';

    service.actOnItem(id, 'approve').subscribe((result) => {
      expect(result.success).toBeTrue();
    });

    const req = httpMock.expectOne(`/api/feed/${id}/act`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ action: 'approve' });
    req.flush({ success: true, navigationTarget: null, targetId: null });
  });

  it('batchMarkRead() sends PUT /api/feed/batch/read with filter body', () => {
    service.batchMarkRead(FeedItemType.TrendAlert, false).subscribe((result) => {
      expect(result.count).toBe(5);
    });

    const req = httpMock.expectOne('/api/feed/batch/read');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: FeedItemType.TrendAlert, isRead: false });
    req.flush({ count: 5 });
  });

  it('batchDismiss() sends PUT /api/feed/batch/dismiss with type body', () => {
    service.batchDismiss(FeedItemType.SystemNotification).subscribe((result) => {
      expect(result.count).toBe(3);
    });

    const req = httpMock.expectOne('/api/feed/batch/dismiss');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: FeedItemType.SystemNotification });
    req.flush({ count: 3 });
  });

  it('batchAct() sends PUT /api/feed/batch/act with IDs and action', () => {
    const ids = ['id-1', 'id-2'];

    service.batchAct(ids, 'approve').subscribe((result) => {
      expect(result.successCount).toBe(2);
      expect(result.failures).toEqual([]);
    });

    const req = httpMock.expectOne('/api/feed/batch/act');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ ids, action: 'approve' });
    req.flush({ successCount: 2, failures: [] });
  });
});
