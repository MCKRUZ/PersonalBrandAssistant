import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ContentStore } from './content.store';
import { ContentService } from '../services/content.service';
import { ContentStatus, ContentType, Platform } from '../models/content.model';
import type { Content } from '../models/content.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('ContentStore', () => {
  let store: InstanceType<typeof ContentStore>;
  let contentService: jasmine.SpyObj<ContentService>;

  const emptyPage: PagedResult<Content> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  const mockContent: Content = {
    id: 'content-1',
    title: 'Test Content',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: null,
    tags: ['test'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
  };

  beforeEach(() => {
    contentService = jasmine.createSpyObj('ContentService', [
      'list',
      'delete',
    ]);
    contentService.list.and.returnValue(of(emptyPage));
    contentService.delete.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      providers: [
        ContentStore,
        { provide: ContentService, useValue: contentService },
      ],
    });
    store = TestBed.inject(ContentStore);
  });

  it('has correct initial state', () => {
    expect(store.contents()).toEqual([]);
    expect(store.totalCount()).toBe(0);
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(20);
    expect(store.filters()).toEqual({});
    expect(store.viewMode()).toBe('list');
    expect(store.loading()).toBeFalse();
    expect(store.error()).toBeNull();
  });

  it('loadContents fetches from service', () => {
    const page: PagedResult<Content> = {
      items: [mockContent],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    contentService.list.and.returnValue(of(page));

    store.loadContents();

    expect(contentService.list).toHaveBeenCalled();
    expect(store.contents()).toEqual([mockContent]);
    expect(store.totalCount()).toBe(1);
    expect(store.loading()).toBeFalse();
  });

  it('setFilter updates filter and reloads', () => {
    store.setPage(3);
    contentService.list.calls.reset();

    store.setFilter('status', ContentStatus.Draft);

    expect(store.filters().status).toBe(ContentStatus.Draft);
    expect(store.page()).toBe(1);
    expect(contentService.list).toHaveBeenCalled();
  });

  it('setPage updates pagination and reloads', () => {
    store.setPage(3);

    expect(store.page()).toBe(3);
    expect(contentService.list).toHaveBeenCalled();
  });

  it('deleteContent calls service and reloads', () => {
    store.deleteContent('content-1');

    expect(contentService.delete).toHaveBeenCalledWith('content-1');
    expect(contentService.list).toHaveBeenCalled();
  });

  it('handles loading errors', () => {
    contentService.list.and.returnValue(
      throwError(() => new Error('Network error'))
    );

    store.loadContents();

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Network error');
  });

  it('computes totalPages correctly', () => {
    contentService.list.and.returnValue(
      of({
        items: [mockContent],
        totalCount: 45,
        page: 1,
        pageSize: 20,
        totalPages: 3,
      })
    );

    store.loadContents();

    expect(store.totalPages()).toBe(3);
  });

  it('toggleView switches list/grid', () => {
    expect(store.viewMode()).toBe('list');

    store.toggleView();
    expect(store.viewMode()).toBe('grid');

    store.toggleView();
    expect(store.viewMode()).toBe('list');
  });
});
