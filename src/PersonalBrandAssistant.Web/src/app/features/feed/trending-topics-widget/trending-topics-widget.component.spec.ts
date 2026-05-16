import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { TrendingTopicsWidgetComponent } from './trending-topics-widget.component';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';
import { createMockFeedStore, mockTrendingTopic } from '../testing/feed-test-utils';

describe('TrendingTopicsWidgetComponent', () => {
  let fixture: ComponentFixture<TrendingTopicsWidgetComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;

  beforeEach(async () => {
    mockStore = createMockFeedStore();

    await TestBed.configureTestingModule({
      imports: [TrendingTopicsWidgetComponent],
      providers: [{ provide: FeedStore, useValue: mockStore }],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(TrendingTopicsWidgetComponent);
    fixture.detectChanges();
  });

  function query(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  function queryAll(testId: string): NodeListOf<HTMLElement> {
    return fixture.nativeElement.querySelectorAll(`[data-testid="${testId}"]`);
  }

  it('should render topic list from store', () => {
    mockStore.trendingTopics.set([
      mockTrendingTopic({ topic: 'Angular' }),
      mockTrendingTopic({ topic: 'Claude' }),
      mockTrendingTopic({ topic: 'NgRx' }),
    ]);
    fixture.detectChanges();

    expect(queryAll('trending-topic').length).toBe(3);
  });

  it('should show topic name and count', () => {
    mockStore.trendingTopics.set([
      mockTrendingTopic({ topic: 'Angular', count: 5 }),
    ]);
    fixture.detectChanges();

    const name = query('topic-name')!;
    const count = query('topic-count')!;
    expect(name.textContent!.trim()).toBe('Angular');
    expect(count.textContent!.trim()).toBe('5');
  });

  it('should call setFilter on topic click', () => {
    mockStore.trendingTopics.set([mockTrendingTopic()]);
    fixture.detectChanges();

    (query('trending-topic') as HTMLElement).click();

    expect(mockStore.setFilter).toHaveBeenCalledWith(FeedItemType.TrendAlert);
  });

  it('should show skeleton when loading with no topics', () => {
    mockStore.loading.set(true);
    mockStore.trendingTopics.set([]);
    fixture.detectChanges();

    expect(query('trending-skeleton')).toBeTruthy();
  });

  it('should not show skeleton when topics exist', () => {
    mockStore.loading.set(true);
    mockStore.trendingTopics.set([mockTrendingTopic()]);
    fixture.detectChanges();

    expect(query('trending-skeleton')).toBeNull();
  });
});
