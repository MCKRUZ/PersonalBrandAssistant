import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { FeedService } from './feed.service';
import { FeedItemType, FeedItemPriority } from '../models/feed-item.model';
import type { FeedItem, FeedListParams } from '../models/feed-item.model';
import type { FeedSummary } from '../models/feed-summary.model';
import type { TrendingTopic } from '../models/trending-topic.model';
import type { PagedResult } from '../../../models/pagination.model';
import { mockFeedItem, mockFeedSummary, mockTrendingTopic } from '../testing/feed-test-utils';

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

  it('should list() with correct query params', () => {
    const params: FeedListParams = {
      page: 2,
      pageSize: 10,
      type: FeedItemType.TrendAlert,
      priority: FeedItemPriority.High,
      isRead: false,
      sortBy: 'CreatedAt',
      sortDirection: 'desc',
    };
    const mockResult: PagedResult<FeedItem> = {
      items: [mockFeedItem({ type: FeedItemType.TrendAlert })],
      totalCount: 1,
      page: 2,
      pageSize: 10,
      totalPages: 1,
    };

    service.list(params).subscribe((result) => {
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

  it('should list() and deserialize PagedResult correctly', () => {
    const items = [mockFeedItem(), mockFeedItem({ isRead: true })];
    const mockResult: PagedResult<FeedItem> = {
      items,
      totalCount: 42,
      page: 1,
      pageSize: 20,
      totalPages: 3,
    };

    service.list({}).subscribe((result) => {
      expect(result.items.length).toBe(2);
      expect(result.totalCount).toBe(42);
      expect(result.page).toBe(1);
      expect(result.pageSize).toBe(20);
      expect(result.totalPages).toBe(3);
    });

    const req = httpMock.expectOne((r) => r.url === '/api/feed');
    expect(req.request.params.keys().length).toBe(0);
    req.flush(mockResult);
  });

  it('should getSummary() via GET /api/feed/summary', () => {
    const summary = mockFeedSummary({ unreadCount: 7 });

    service.getSummary().subscribe((result) => {
      expect(result).toEqual(summary);
    });

    const req = httpMock.expectOne('/api/feed/summary');
    expect(req.request.method).toBe('GET');
    req.flush(summary);
  });

  it('should getTrending() via GET /api/feed/trending', () => {
    const topics = [
      mockTrendingTopic({ topic: 'AI' }),
      mockTrendingTopic({ topic: '.NET', count: 8 }),
    ];

    service.getTrending().subscribe((result) => {
      expect(result).toEqual(topics);
    });

    const req = httpMock.expectOne('/api/feed/trending');
    expect(req.request.method).toBe('GET');
    req.flush(topics);
  });

  it('should markRead() via PUT /api/feed/{id}/read', () => {
    const id = 'abc-123';

    service.markRead(id).subscribe();

    const req = httpMock.expectOne(`/api/feed/${id}/read`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({});
    req.flush(null);
  });

  it('should actOnItem() via PUT /api/feed/{id}/act with action body', () => {
    const id = 'abc-123';

    service.actOnItem(id, 'approve').subscribe((result) => {
      expect(result.success).toBeTrue();
    });

    const req = httpMock.expectOne(`/api/feed/${id}/act`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ action: 'approve' });
    req.flush({ success: true, navigationTarget: null, targetId: null });
  });

  it('should batchMarkRead() via PUT /api/feed/batch/read with filter body', () => {
    service.batchMarkRead(FeedItemType.TrendAlert, false).subscribe((result) => {
      expect(result.count).toBe(5);
    });

    const req = httpMock.expectOne('/api/feed/batch/read');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: FeedItemType.TrendAlert, isRead: false });
    req.flush({ count: 5 });
  });

  it('should batchDismiss() via PUT /api/feed/batch/dismiss with type body', () => {
    service.batchDismiss(FeedItemType.SystemNotification).subscribe((result) => {
      expect(result.count).toBe(3);
    });

    const req = httpMock.expectOne('/api/feed/batch/dismiss');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ type: FeedItemType.SystemNotification });
    req.flush({ count: 3 });
  });

  it('should batchMarkReadByIds() via PUT /api/feed/batch/read with ids body', () => {
    const ids = ['id-1', 'id-2'];

    service.batchMarkReadByIds(ids).subscribe((result) => {
      expect(result.count).toBe(2);
    });

    const req = httpMock.expectOne('/api/feed/batch/read');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ ids, isRead: true });
    req.flush({ count: 2 });
  });

  it('should batchAct() via PUT /api/feed/batch/act with ids and action', () => {
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
