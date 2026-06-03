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
    pageSize: 1000,
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
    contentService = jasmine.createSpyObj('ContentService', ['list', 'delete']);
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
    expect(store.allContents()).toEqual([]);
    expect(store.activeStatus()).toBeNull();
    expect(store.search()).toBe('');
    expect(store.filters()).toEqual({});
    expect(store.viewMode()).toBe('board');
    expect(store.loading()).toBeFalse();
    expect(store.error()).toBeNull();
  });

  it('loadAll fetches the whole pipeline into allContents', () => {
    const page: PagedResult<Content> = {
      items: [mockContent],
      totalCount: 1,
      page: 1,
      pageSize: 1000,
      totalPages: 1,
    };
    contentService.list.and.returnValue(of(page));

    store.loadAll();

    expect(contentService.list).toHaveBeenCalled();
    expect(store.allContents()).toEqual([mockContent]);
    expect(store.loading()).toBeFalse();
  });

  it('setView widens to board/grid/table', () => {
    store.setView('grid');
    expect(store.viewMode()).toBe('grid');
    store.setView('table');
    expect(store.viewMode()).toBe('table');
    store.setView('board');
    expect(store.viewMode()).toBe('board');
  });

  it('setFilter merges a popover filter without refetching', () => {
    contentService.list.calls.reset();

    store.setFilter('platform', Platform.LinkedIn);

    expect(store.filters().platform).toBe(Platform.LinkedIn);
    expect(contentService.list).not.toHaveBeenCalled();
  });

  it('deleteContent calls service then reloads via loadAll', () => {
    store.deleteContent('content-1');

    expect(contentService.delete).toHaveBeenCalledWith('content-1');
    expect(contentService.list).toHaveBeenCalled();
  });

  it('handles loading errors', () => {
    contentService.list.and.returnValue(
      throwError(() => new Error('Network error'))
    );

    store.loadAll();

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Network error');
  });
});
