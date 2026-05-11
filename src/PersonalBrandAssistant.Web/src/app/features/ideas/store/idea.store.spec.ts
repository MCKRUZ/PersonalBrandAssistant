import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { IdeaStore } from './idea.store';
import { IdeaService } from '../../../core/services/idea.service';
import { IdeaStatus } from '../../../models/idea.model';
import type { Idea } from '../../../models/idea.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('IdeaStore', () => {
  let store: InstanceType<typeof IdeaStore>;
  let ideaService: jasmine.SpyObj<IdeaService>;

  const emptyPage: PagedResult<Idea> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  const mockIdea: Idea = {
    id: 'idea-1',
    title: 'Test Idea',
    sourceName: 'Manual',
    category: 'Tech',
    summary: null,
    thumbnailUrl: null,
    status: IdeaStatus.New,
    tags: [],
    detectedAt: '2026-01-01T00:00:00Z',
    hasSavedDetails: false,
  };

  beforeEach(() => {
    ideaService = jasmine.createSpyObj('IdeaService', [
      'list',
      'save',
      'dismiss',
    ]);
    ideaService.list.and.returnValue(of(emptyPage));
    ideaService.save.and.returnValue(of(void 0));
    ideaService.dismiss.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      providers: [IdeaStore, { provide: IdeaService, useValue: ideaService }],
    });
    store = TestBed.inject(IdeaStore);
  });

  it('has correct initial state', () => {
    expect(store.ideas()).toEqual([]);
    expect(store.viewMode()).toBe('list');
    expect(store.selectedIdeaId()).toBeNull();
    expect(store.loading()).toBeFalse();
    expect(store.page()).toBe(1);
    expect(store.pageSize()).toBe(20);
  });

  it('loadIdeas fetches ideas from service', () => {
    const page: PagedResult<Idea> = {
      items: [mockIdea],
      totalCount: 1,
      page: 1,
      pageSize: 20,
      totalPages: 1,
    };
    ideaService.list.and.returnValue(of(page));

    store.loadIdeas();

    expect(ideaService.list).toHaveBeenCalled();
    expect(store.ideas()).toEqual([mockIdea]);
    expect(store.totalCount()).toBe(1);
    expect(store.loading()).toBeFalse();
  });

  it('setFilter resets page to 1 and reloads', () => {
    store.setFilter({ status: IdeaStatus.Saved });

    expect(store.filter().status).toBe(IdeaStatus.Saved);
    expect(store.page()).toBe(1);
    expect(ideaService.list).toHaveBeenCalled();
  });

  it('setSort resets page to 1 and reloads', () => {
    store.setSort({ field: 'title', direction: 'asc' });

    expect(store.sort().field).toBe('title');
    expect(store.sort().direction).toBe('asc');
    expect(store.page()).toBe(1);
    expect(ideaService.list).toHaveBeenCalled();
  });

  it('setPage updates page and reloads', () => {
    store.setPage(3);

    expect(store.page()).toBe(3);
    expect(ideaService.list).toHaveBeenCalled();
  });

  it('toggleView switches between list and grid', () => {
    expect(store.viewMode()).toBe('list');

    store.toggleView();
    expect(store.viewMode()).toBe('grid');

    store.toggleView();
    expect(store.viewMode()).toBe('list');
  });

  it('selectIdea updates selectedIdeaId', () => {
    store.selectIdea('idea-1');
    expect(store.selectedIdeaId()).toBe('idea-1');

    store.selectIdea(null);
    expect(store.selectedIdeaId()).toBeNull();
  });

  it('saveIdea calls service and reloads', () => {
    store.saveIdea('idea-1', 'notes', ['tag1']);

    expect(ideaService.save).toHaveBeenCalledWith('idea-1', 'notes', ['tag1']);
    expect(ideaService.list).toHaveBeenCalled();
  });

  it('dismissIdea calls service and reloads', () => {
    store.dismissIdea('idea-1');

    expect(ideaService.dismiss).toHaveBeenCalledWith('idea-1');
    expect(ideaService.list).toHaveBeenCalled();
  });

  it('handles loading errors', () => {
    ideaService.list.and.returnValue(throwError(() => new Error('Network error')));

    store.loadIdeas();

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Network error');
  });

  it('computes totalPages correctly', () => {
    ideaService.list.and.returnValue(
      of({ items: [mockIdea], totalCount: 45, page: 1, pageSize: 20, totalPages: 3 })
    );

    store.loadIdeas();

    expect(store.totalPages()).toBe(3);
  });
});
