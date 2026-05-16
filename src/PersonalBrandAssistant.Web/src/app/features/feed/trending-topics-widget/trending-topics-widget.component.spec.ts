import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TrendingTopicsWidgetComponent } from './trending-topics-widget.component';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';
import { signal } from '@angular/core';
import type { TrendingTopic } from '../models/trending-topic.model';

describe('TrendingTopicsWidgetComponent', () => {
  let fixture: ComponentFixture<TrendingTopicsWidgetComponent>;
  let trendingTopics: ReturnType<typeof signal<TrendingTopic[]>>;
  let loading: ReturnType<typeof signal<boolean>>;
  let setFilterSpy: jasmine.Spy;

  beforeEach(async () => {
    trendingTopics = signal<TrendingTopic[]>([]);
    loading = signal(false);
    setFilterSpy = jasmine.createSpy('setFilter');

    await TestBed.configureTestingModule({
      imports: [TrendingTopicsWidgetComponent],
      providers: [
        {
          provide: FeedStore,
          useValue: {
            trendingTopics,
            loading,
            setFilter: setFilterSpy,
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TrendingTopicsWidgetComponent);
    fixture.detectChanges();
  });

  it('should render topic list from store.trendingTopics', () => {
    trendingTopics.set([
      { topic: 'AI Agents', count: 42, latestAt: new Date().toISOString() },
      { topic: 'Claude Code', count: 28, latestAt: new Date().toISOString() },
    ]);
    fixture.detectChanges();

    const topics = fixture.nativeElement.querySelectorAll('[data-testid="trending-topic"]');
    expect(topics.length).toBe(2);
  });

  it('should show rank number, topic name, and count badge', () => {
    trendingTopics.set([
      { topic: 'AI Agents', count: 42, latestAt: new Date().toISOString() },
    ]);
    fixture.detectChanges();

    const topic = fixture.nativeElement.querySelector('[data-testid="trending-topic"]') as HTMLElement;
    const rank = topic.querySelector('[data-testid="topic-rank"]');
    const name = topic.querySelector('[data-testid="topic-name"]');
    const count = topic.querySelector('[data-testid="topic-count"]');

    expect(rank?.textContent?.trim()).toBe('1');
    expect(name?.textContent?.trim()).toBe('AI Agents');
    expect(count?.textContent?.trim()).toBe('42');
  });

  it('should trigger store.setFilter for TrendAlert on topic click', () => {
    trendingTopics.set([
      { topic: 'AI Agents', count: 42, latestAt: new Date().toISOString() },
    ]);
    fixture.detectChanges();

    const topic = fixture.nativeElement.querySelector('[data-testid="trending-topic"]') as HTMLElement;
    topic.click();

    expect(setFilterSpy).toHaveBeenCalledWith(FeedItemType.TrendAlert);
  });

  it('should show "No trends yet" when list is empty', () => {
    trendingTopics.set([]);
    loading.set(false);
    fixture.detectChanges();

    const empty = fixture.nativeElement.querySelector('[data-testid="trending-empty"]');
    expect(empty?.textContent).toContain('No trends yet');
  });

  it('should show skeleton loader when loading', () => {
    loading.set(true);
    trendingTopics.set([]);
    fixture.detectChanges();

    const skeleton = fixture.nativeElement.querySelector('[data-testid="trending-skeleton"]');
    expect(skeleton).toBeTruthy();
  });
});
