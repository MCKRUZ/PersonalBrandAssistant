import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedNewItemsBannerComponent } from './feed-new-items-banner.component';
import { FeedStore } from '../store/feed.store';
import { createMockFeedStore } from '../testing/feed-test-utils';

describe('FeedNewItemsBannerComponent', () => {
  let fixture: ComponentFixture<FeedNewItemsBannerComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;

  beforeEach(async () => {
    mockStore = createMockFeedStore();

    await TestBed.configureTestingModule({
      imports: [FeedNewItemsBannerComponent],
      providers: [{ provide: FeedStore, useValue: mockStore }],
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(FeedNewItemsBannerComponent);
    fixture.detectChanges();
  });

  function query(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  it('should show banner when newItemCount > 0', () => {
    mockStore.newItemCount.set(5);
    fixture.detectChanges();

    expect(query('new-items-banner')).toBeTruthy();
  });

  it('should hide banner when newItemCount is 0', () => {
    mockStore.newItemCount.set(0);
    fixture.detectChanges();

    expect(query('new-items-banner')).toBeNull();
  });

  it('should display correct count in message', () => {
    mockStore.newItemCount.set(3);
    fixture.detectChanges();

    const message = query('banner-message')!;
    expect(message.textContent).toContain('3');
  });

  it('should call loadNewItems on Show click', () => {
    mockStore.newItemCount.set(2);
    fixture.detectChanges();

    (query('show-btn') as HTMLButtonElement).click();

    expect(mockStore.loadNewItems).toHaveBeenCalled();
  });

  it('should have slide-down class', () => {
    mockStore.newItemCount.set(1);
    fixture.detectChanges();

    const banner = query('new-items-banner')!;
    expect(banner.classList.contains('slide-down')).toBeTrue();
  });
});
