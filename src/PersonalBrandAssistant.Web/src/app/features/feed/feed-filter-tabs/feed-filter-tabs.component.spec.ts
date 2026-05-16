import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { FeedFilterTabsComponent } from './feed-filter-tabs.component';
import { FeedStore } from '../store/feed.store';
import { FeedItemType } from '../models/feed-item.model';
import { createMockFeedStore, mockFeedSummary } from '../testing/feed-test-utils';

describe('FeedFilterTabsComponent', () => {
  let fixture: ComponentFixture<FeedFilterTabsComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;
  let mockRouter: jasmine.SpyObj<Router>;

  beforeEach(() => {
    mockStore = createMockFeedStore();
    mockStore.summary.set(mockFeedSummary());
    mockRouter = jasmine.createSpyObj('Router', ['navigate']);

    TestBed.configureTestingModule({
      imports: [FeedFilterTabsComponent],
      providers: [
        { provide: FeedStore, useValue: mockStore },
        { provide: Router, useValue: mockRouter },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(FeedFilterTabsComponent);
    fixture.detectChanges();
  });

  it('should render 6 tabs', () => {
    const tabs = fixture.nativeElement.querySelectorAll('[role="tab"]');
    expect(tabs.length).toBe(6);
  });

  it('should highlight active tab', () => {
    mockStore.activeFilter.set(FeedItemType.TrendAlert);
    fixture.detectChanges();

    const trendsTab = fixture.nativeElement.querySelector('[data-testid="tab-trends"]');
    expect(trendsTab.classList).toContain('active');
  });

  it('should call setFilter on tab click', () => {
    const draftsTab = fixture.nativeElement.querySelector('[data-testid="tab-drafts"]');
    draftsTab.click();

    expect(mockStore.setFilter).toHaveBeenCalledWith(FeedItemType.AgentDraft);
  });

  it('should pass null for All tab', () => {
    const allTab = fixture.nativeElement.querySelector('[data-testid="tab-all"]');
    allTab.click();

    expect(mockStore.setFilter).toHaveBeenCalledWith(null);
  });

  it('should read initial filter from query params', () => {
    TestBed.resetTestingModule();

    const freshStore = createMockFeedStore();
    freshStore.summary.set(mockFeedSummary());

    TestBed.configureTestingModule({
      imports: [FeedFilterTabsComponent],
      providers: [
        { provide: FeedStore, useValue: freshStore },
        { provide: Router, useValue: jasmine.createSpyObj('Router', ['navigate']) },
        { provide: ActivatedRoute, useValue: { queryParams: of({ type: 'TrendAlert' }) } },
      ],
      schemas: [NO_ERRORS_SCHEMA],
    });

    TestBed.createComponent(FeedFilterTabsComponent).detectChanges();

    expect(freshStore.setFilter).toHaveBeenCalledWith('TrendAlert' as FeedItemType);
  });
});
