import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { IdeaSourceStore } from './idea-source.store';
import { IdeaService } from '../../../core/services/idea.service';
import { IdeaSourceType } from '../../../models/idea.model';
import type { IdeaSource } from '../../../models/idea.model';

describe('IdeaSourceStore', () => {
  let store: InstanceType<typeof IdeaSourceStore>;
  let ideaService: jasmine.SpyObj<IdeaService>;

  const mockSource: IdeaSource = {
    id: 'src-1',
    name: 'Tech Blog',
    type: IdeaSourceType.RSS,
    feedUrl: 'https://example.com/rss',
    apiUrl: null,
    category: 'Tech',
    pollIntervalMinutes: 30,
    isEnabled: true,
    lastPolledAt: null,
    lastSuccessAt: null,
    lastError: null,
    consecutiveFailures: 0,
    ideaCount: 5,
    isHealthy: true,
  };

  beforeEach(() => {
    ideaService = jasmine.createSpyObj('IdeaService', [
      'listSources',
      'createSource',
      'updateSource',
      'deleteSource',
      'refreshSources',
    ]);
    ideaService.listSources.and.returnValue(of([]));
    ideaService.createSource.and.returnValue(of('new-id'));
    ideaService.updateSource.and.returnValue(of(void 0));
    ideaService.deleteSource.and.returnValue(of(void 0));
    ideaService.refreshSources.and.returnValue(of(0));

    TestBed.configureTestingModule({
      providers: [IdeaSourceStore, { provide: IdeaService, useValue: ideaService }],
    });
    store = TestBed.inject(IdeaSourceStore);
  });

  it('has correct initial state', () => {
    expect(store.sources()).toEqual([]);
    expect(store.loading()).toBeFalse();
    expect(store.error()).toBeNull();
    expect(store.lastRefreshCount()).toBeNull();
  });

  it('loadAll populates sources', () => {
    ideaService.listSources.and.returnValue(of([mockSource]));

    store.loadAll();

    expect(store.sources()).toEqual([mockSource]);
    expect(store.loading()).toBeFalse();
  });

  it('create calls service and reloads', () => {
    const request = {
      name: 'New Source',
      type: IdeaSourceType.RSS,
      feedUrl: 'https://example.com/rss',
      category: 'Tech',
      pollIntervalMinutes: 30,
    };

    store.create(request);

    expect(ideaService.createSource).toHaveBeenCalledWith(request);
    expect(ideaService.listSources).toHaveBeenCalled();
  });

  it('update calls service and reloads', () => {
    store.update('src-1', { name: 'Updated' });

    expect(ideaService.updateSource).toHaveBeenCalledWith('src-1', { name: 'Updated' });
    expect(ideaService.listSources).toHaveBeenCalled();
  });

  it('remove calls service and reloads', () => {
    store.remove('src-1');

    expect(ideaService.deleteSource).toHaveBeenCalledWith('src-1');
    expect(ideaService.listSources).toHaveBeenCalled();
  });

  it('refreshAll calls service and stores count', () => {
    ideaService.refreshSources.and.returnValue(of(5));

    store.refreshAll();

    expect(ideaService.refreshSources).toHaveBeenCalled();
    expect(store.lastRefreshCount()).toBe(5);
    expect(ideaService.listSources).toHaveBeenCalled();
  });

  it('handles loading errors', () => {
    ideaService.listSources.and.returnValue(
      throwError(() => new Error('Connection failed'))
    );

    store.loadAll();

    expect(store.loading()).toBeFalse();
    expect(store.error()).toBe('Connection failed');
  });
});
