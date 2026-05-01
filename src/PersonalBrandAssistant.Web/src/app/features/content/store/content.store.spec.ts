import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ContentStore } from './content.store';
import { ContentService } from '../services/content.service';
import { Content } from '../../../shared/models';

function makeContent(overrides: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Test Post',
    body: 'Hello',
    contentType: 'SocialPost',
    status: 'Draft',
    targetPlatforms: ['LinkedIn'],
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    version: 1,
    ...overrides,
  } as Content;
}

describe('ContentStore', () => {
  let store: InstanceType<typeof ContentStore>;
  let serviceSpy: jasmine.SpyObj<ContentService>;

  beforeEach(() => {
    serviceSpy = jasmine.createSpyObj('ContentService', ['getAll', 'getById', 'getAllowedTransitions', 'getBrandVoiceScore', 'getAuditLog']);
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));

    TestBed.configureTestingModule({
      providers: [
        ContentStore,
        { provide: ContentService, useValue: serviceSpy },
      ],
    });
    store = TestBed.inject(ContentStore);
  });

  it('should load items with pagination from API', () => {
    const items = [makeContent(), makeContent({ id: 'c2' }), makeContent({ id: 'c3' })];
    serviceSpy.getAll.and.returnValue(of({ items, cursor: 'abc', hasMore: true }));

    store.loadContent({});

    expect(store.items().length).toBe(3);
    expect(store.loading()).toBe(false);
    expect(store.hasMore()).toBe(true);
  });

  it('should filter by type and status', () => {
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));

    store.loadContent({ contentType: 'BlogPost', status: 'Draft' });

    expect(serviceSpy.getAll).toHaveBeenCalledWith(jasmine.objectContaining({ contentType: 'BlogPost', status: 'Draft' }));
  });

  it('should filter by platform and search', () => {
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));

    store.loadContent({ platform: 'LinkedIn', search: 'angular' });

    expect(serviceSpy.getAll).toHaveBeenCalledWith(jasmine.objectContaining({ platform: 'LinkedIn', search: 'angular' }));
  });

  it('should paginate via loadMore using cursor', () => {
    const first = [makeContent({ id: 'a' }), makeContent({ id: 'b' })];
    const second = [makeContent({ id: 'c' })];
    serviceSpy.getAll.and.returnValues(
      of({ items: first, cursor: 'abc', hasMore: true }),
      of({ items: second, cursor: undefined, hasMore: false }),
    );

    store.loadContent({});
    expect(store.items().length).toBe(2);

    store.loadMore(undefined);
    expect(store.items().length).toBe(3);
    expect(store.hasMore()).toBe(false);
  });

  it('should set loading true during fetch and false on completion', () => {
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));

    store.loadContent({});

    expect(store.loading()).toBe(false);
  });

  it('should handle API error gracefully', () => {
    serviceSpy.getAll.and.returnValue(throwError(() => new Error('fail')));

    store.loadContent({});

    expect(store.loading()).toBe(false);
    expect(store.items().length).toBe(0);
  });
});
