import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ContentStore } from './content.store';
import { ContentService } from '../services/content.service';
import { ContentStatus, ContentType, Platform } from '../models/content.model';
import type { Content, ContentDetail } from '../models/content.model';
import type { PagedResult } from '../../../models/pagination.model';

function makeContent(over: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Hello world',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: null,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    ...over,
  };
}

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

describe('ContentStore (redesign surface)', () => {
  let store: InstanceType<typeof ContentStore>;
  let svc: jasmine.SpyObj<ContentService>;

  beforeEach(() => {
    svc = jasmine.createSpyObj('ContentService', [
      'list', 'delete', 'get', 'draft', 'approve', 'submitForReview',
      'requestChanges', 'schedule', 'unschedule', 'publish', 'unpublish', 'restore',
    ]);
    svc.list.and.returnValue(of(page([])));
    svc.delete.and.returnValue(of(void 0));
    ['draft', 'approve', 'submitForReview', 'requestChanges', 'unschedule', 'publish', 'unpublish', 'restore'].forEach(
      (m) => (svc as any)[m].and.returnValue(of(void 0))
    );

    TestBed.configureTestingModule({
      providers: [ContentStore, { provide: ContentService, useValue: svc }],
    });
    store = TestBed.inject(ContentStore);
  });

  function seed(items: Content[]): void {
    svc.list.and.returnValue(of(page(items)));
    store.loadAll();
  }

  it('loadAll populates allContents', () => {
    seed([makeContent()]);
    expect(store.allContents().length).toBe(1);
    expect(store.loading()).toBeFalse();
  });

  it('counts tallies per status', () => {
    seed([
      makeContent({ id: 'a', status: ContentStatus.Draft }),
      makeContent({ id: 'b', status: ContentStatus.Draft }),
      makeContent({ id: 'c', status: ContentStatus.Published }),
    ]);
    expect(store.counts()[ContentStatus.Draft]).toBe(2);
    expect(store.counts()[ContentStatus.Published]).toBe(1);
    expect(store.counts()[ContentStatus.Idea]).toBe(0);
  });

  it('filtered applies activeStatus + search (title and tags) together', () => {
    seed([
      makeContent({ id: 'a', title: 'Angular signals', status: ContentStatus.Draft, tags: [] }),
      makeContent({ id: 'b', title: 'Other', status: ContentStatus.Draft, tags: ['angular'] }),
      makeContent({ id: 'c', title: 'Angular', status: ContentStatus.Published, tags: [] }),
    ]);
    store.setActiveStatus(ContentStatus.Draft);
    store.setSearch('angular');
    const ids = store.filtered().map((c) => c.id);
    expect(ids).toContain('a'); // title match, Draft
    expect(ids).toContain('b'); // tag match, Draft
    expect(ids).not.toContain('c'); // matches search but wrong status
  });

  it('byStatus groups filtered into columns', () => {
    seed([
      makeContent({ id: 'a', status: ContentStatus.Idea }),
      makeContent({ id: 'b', status: ContentStatus.Idea }),
    ]);
    expect(store.byStatus()[ContentStatus.Idea].length).toBe(2);
    expect(store.byStatus()[ContentStatus.Draft].length).toBe(0);
  });

  it('setActiveStatus toggles and clears on re-select', () => {
    store.setActiveStatus(ContentStatus.Review);
    expect(store.activeStatus()).toBe(ContentStatus.Review);
    store.setActiveStatus(ContentStatus.Review);
    expect(store.activeStatus()).toBeNull();
  });

  describe('transition', () => {
    it('dispatches the correct endpoint per (current,target)', () => {
      svc.get.and.callFake((id: string) =>
        of(makeContent({ id, status: ContentStatus.Approved, updatedAt: 'x' }) as ContentDetail)
      );
      seed([makeContent({ id: 'c1', status: ContentStatus.Draft })]);

      store.transition('c1', ContentStatus.Approved);
      expect(svc.approve).toHaveBeenCalledWith('c1');

      seed([makeContent({ id: 'c2', status: ContentStatus.Draft })]);
      store.transition('c2', ContentStatus.Review);
      expect(svc.submitForReview).toHaveBeenCalledWith('c2');

      seed([makeContent({ id: 'c3', status: ContentStatus.Idea })]);
      store.transition('c3', ContentStatus.Draft);
      expect(svc.draft).toHaveBeenCalledWith('c3', { action: 'draft' });

      seed([makeContent({ id: 'c4', status: ContentStatus.Scheduled })]);
      store.transition('c4', ContentStatus.Approved);
      expect(svc.unschedule).toHaveBeenCalledWith('c4');

      seed([makeContent({ id: 'c5', status: ContentStatus.Archived })]);
      store.transition('c5', ContentStatus.Draft);
      expect(svc.restore).toHaveBeenCalledWith('c5');
    });

    it('illegal target is a no-op with a notice', () => {
      seed([makeContent({ id: 'c1', status: ContentStatus.Draft })]);
      store.transition('c1', ContentStatus.Published); // not legal from Draft
      expect(svc.publish).not.toHaveBeenCalled();
      expect(store.error()).toContain('Cannot move');
    });

    it('Approved -> Scheduled is a no-op (caller opens the schedule dialog)', () => {
      seed([makeContent({ id: 'c1', status: ContentStatus.Approved })]);
      store.transition('c1', ContentStatus.Scheduled);
      expect(svc.schedule).not.toHaveBeenCalled();
      expect(svc.approve).not.toHaveBeenCalled();
    });

    it('optimistically patches status, then reloads the record for the real updatedAt', () => {
      svc.get.and.returnValue(
        of(makeContent({ id: 'c1', status: ContentStatus.Approved, updatedAt: 'server-ts' }) as ContentDetail)
      );
      seed([makeContent({ id: 'c1', status: ContentStatus.Draft, updatedAt: 'old' })]);

      store.transition('c1', ContentStatus.Approved);

      const rec = store.allContents().find((c) => c.id === 'c1')!;
      expect(rec.status).toBe(ContentStatus.Approved);
      expect(rec.updatedAt).toBe('server-ts'); // from reload, not fabricated
    });

    it('rolls back the status on service error', () => {
      svc.approve.and.returnValue(throwError(() => new Error('boom')));
      seed([makeContent({ id: 'c1', status: ContentStatus.Draft })]);

      store.transition('c1', ContentStatus.Approved);

      expect(store.allContents().find((c) => c.id === 'c1')!.status).toBe(ContentStatus.Draft);
      expect(store.error()).toBe('boom');
    });
  });
});
