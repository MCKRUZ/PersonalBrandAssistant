import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { NewsStore } from './news.store';
import { environment } from '../../../environments/environment';

describe('NewsStore dismiss', () => {
  let store: InstanceType<typeof NewsStore>;
  let httpMock: HttpTestingController;

  const makeSuggestion = (id: string, title: string) => ({
    id,
    topic: title,
    rationale: 'test',
    relevanceScore: 0.8,
    suggestedContentType: 'SocialPost',
    suggestedPlatforms: ['LinkedIn'],
    createdAt: new Date().toISOString(),
    status: 'Pending',
    relatedTrends: [{
      trendItemId: `ti-${id}`,
      source: 'TestSource',
      sourceName: 'Test',
      title,
      description: 'desc',
      url: 'https://example.com',
      score: 0.8,
      sourceCategory: 'AI/ML',
    }],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        NewsStore,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MessageService, useValue: jasmine.createSpyObj('MessageService', ['add']) },
      ],
    });
    store = TestBed.inject(NewsStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should have 3 suggestions initially after manual state set', () => {
    // Manually load suggestions into the store
    const suggestions = [
      makeSuggestion('aaa', 'First Story'),
      makeSuggestion('bbb', 'Second Story'),
      makeSuggestion('ccc', 'Third Story'),
    ];

    // Use load and intercept the HTTP call to inject test data
    store.load(undefined);
    const req = httpMock.expectOne(r => r.url.includes('trends/suggestions'));
    req.flush(suggestions);

    expect(store.suggestions().length).toBe(3);
    expect(store.filteredItems().length).toBe(3);
    expect(store.groupedByCategory().length).toBeGreaterThan(0);
  });

  it('should remove the dismissed suggestion from state immediately', fakeAsync(() => {
    const suggestions = [
      makeSuggestion('aaa', 'First Story'),
      makeSuggestion('bbb', 'Second Story'),
      makeSuggestion('ccc', 'Third Story'),
    ];

    store.load(undefined);
    const loadReq = httpMock.expectOne(r => r.url.includes('trends/suggestions'));
    loadReq.flush(suggestions);
    tick();

    expect(store.suggestions().length).toBe(3);

    // Dismiss the first item — feedItemId format is "suggestionId-trendIndex"
    store.dismiss('aaa-0');
    tick();

    // Should immediately drop to 2 suggestions
    expect(store.suggestions().length).toBe(2);
    expect(store.suggestions().find(s => s.id === 'aaa')).toBeUndefined();
    expect(store.filteredItems().length).toBe(2);

    // The API call should be in-flight
    const dismissReq = httpMock.expectOne(r => r.url.includes('trends/suggestions/aaa/dismiss'));
    expect(dismissReq.request.method).toBe('POST');
    dismissReq.flush(null);
    tick();

    // Still 2 after API success
    expect(store.suggestions().length).toBe(2);
  }));

  it('should rollback on API error', fakeAsync(() => {
    const suggestions = [
      makeSuggestion('aaa', 'First Story'),
      makeSuggestion('bbb', 'Second Story'),
    ];

    store.load(undefined);
    httpMock.expectOne(r => r.url.includes('trends/suggestions')).flush(suggestions);
    tick();

    store.dismiss('aaa-0');
    tick();

    expect(store.suggestions().length).toBe(1);

    // API fails
    const dismissReq = httpMock.expectOne(r => r.url.includes('trends/suggestions/aaa/dismiss'));
    dismissReq.flush('error', { status: 500, statusText: 'Server Error' });
    tick();

    // Should rollback
    expect(store.suggestions().length).toBe(2);
  }));

  it('should update groupedByCategory after dismiss', fakeAsync(() => {
    const suggestions = [
      makeSuggestion('aaa', 'First Story'),
      makeSuggestion('bbb', 'Second Story'),
      makeSuggestion('ccc', 'Third Story'),
    ];

    store.load(undefined);
    httpMock.expectOne(r => r.url.includes('trends/suggestions')).flush(suggestions);
    tick();

    const groupBefore = store.groupedByCategory();
    const itemsBefore = groupBefore.flatMap(g => g.items);
    expect(itemsBefore.length).toBe(3);

    store.dismiss('aaa-0');
    tick();

    const groupAfter = store.groupedByCategory();
    const itemsAfter = groupAfter.flatMap(g => g.items);
    expect(itemsAfter.length).toBe(2);
    expect(itemsAfter.find(i => i.title === 'First Story')).toBeUndefined();
    expect(itemsAfter[0].title).toBe('Second Story');

    httpMock.expectOne(r => r.url.includes('dismiss')).flush(null);
    tick();
  }));
});
