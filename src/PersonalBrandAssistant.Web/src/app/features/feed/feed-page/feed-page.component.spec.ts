import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedPageComponent } from './feed-page.component';
import { FeedStore } from '../store/feed.store';
import { createMockFeedStore } from '../testing/feed-test-utils';

describe('FeedPageComponent', () => {
  let fixture: ComponentFixture<FeedPageComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;

  beforeEach(() => {
    mockStore = createMockFeedStore();

    TestBed.configureTestingModule({
      imports: [FeedPageComponent],
      providers: [{ provide: FeedStore, useValue: mockStore }],
      schemas: [NO_ERRORS_SCHEMA],
    }).overrideComponent(FeedPageComponent, {
      set: { imports: [], schemas: [NO_ERRORS_SCHEMA] },
    });

    fixture = TestBed.createComponent(FeedPageComponent);
    fixture.detectChanges();
  });

  it('should render app-feed-stats-bar', () => {
    const el = fixture.nativeElement.querySelector('app-feed-stats-bar');
    expect(el).toBeTruthy();
  });

  it('should render app-feed-filter-tabs', () => {
    const el = fixture.nativeElement.querySelector('app-feed-filter-tabs');
    expect(el).toBeTruthy();
  });

  it('should render app-feed-card-list', () => {
    const el = fixture.nativeElement.querySelector('app-feed-card-list');
    expect(el).toBeTruthy();
  });

  it('should render app-feed-sidebar', () => {
    const el = fixture.nativeElement.querySelector('app-feed-sidebar');
    expect(el).toBeTruthy();
  });

  it('should render app-feed-batch-toolbar', () => {
    const el = fixture.nativeElement.querySelector('app-feed-batch-toolbar');
    expect(el).toBeTruthy();
  });

  it('should render app-feed-new-items-banner', () => {
    const el = fixture.nativeElement.querySelector('app-feed-new-items-banner');
    expect(el).toBeTruthy();
  });

  it('should display page title and subtitle', () => {
    const h1 = fixture.nativeElement.querySelector('h1');
    const subtitle = fixture.nativeElement.querySelector('p.subtitle');

    expect(h1.textContent).toContain('Feed');
    expect(subtitle.textContent).toContain('Your daily command center');
  });
});
