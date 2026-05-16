import { ComponentFixture, TestBed } from '@angular/core/testing';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { FeedBatchToolbarComponent } from './feed-batch-toolbar.component';
import { FeedStore } from '../store/feed.store';
import { createMockFeedStore } from '../testing/feed-test-utils';

describe('FeedBatchToolbarComponent', () => {
  let fixture: ComponentFixture<FeedBatchToolbarComponent>;
  let mockStore: ReturnType<typeof createMockFeedStore>;

  beforeEach(() => {
    mockStore = createMockFeedStore();

    TestBed.configureTestingModule({
      imports: [FeedBatchToolbarComponent],
      providers: [{ provide: FeedStore, useValue: mockStore }],
      schemas: [NO_ERRORS_SCHEMA],
    });

    fixture = TestBed.createComponent(FeedBatchToolbarComponent);
    fixture.detectChanges();
  });

  it('should show toolbar when hasSelection is true', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedCount.set(2);
    fixture.detectChanges();

    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar"]');
    expect(toolbar).toBeTruthy();
  });

  it('should hide toolbar when hasSelection is false', () => {
    mockStore.hasSelection.set(false);
    fixture.detectChanges();

    const toolbar = fixture.nativeElement.querySelector('[data-testid="batch-toolbar"]');
    expect(toolbar).toBeFalsy();
  });

  it('should display selected count', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedCount.set(3);
    fixture.detectChanges();

    const countEl = fixture.nativeElement.querySelector('[data-testid="selected-count"]');
    expect(countEl.textContent).toContain('3');
  });

  it('should call batchAct with approve on Approve click', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedIds.set(['a', 'b']);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('[data-testid="btn-approve"]');
    btn.click();

    expect(mockStore.batchAct).toHaveBeenCalledWith(['a', 'b'], 'approve');
  });

  it('should call batchAct with dismiss on Dismiss click', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedIds.set(['a', 'b']);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('[data-testid="btn-dismiss"]');
    btn.click();

    expect(mockStore.batchAct).toHaveBeenCalledWith(['a', 'b'], 'dismiss');
  });

  it('should call clearSelection on Clear click', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedCount.set(1);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('[data-testid="btn-clear"]');
    btn.click();

    expect(mockStore.clearSelection).toHaveBeenCalled();
  });

  it('should call batchMarkReadByIds on Mark Read click', () => {
    mockStore.hasSelection.set(true);
    mockStore.selectedIds.set(['a', 'b']);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('[data-testid="btn-mark-read"]');
    btn.click();

    expect(mockStore.batchMarkReadByIds).toHaveBeenCalledWith(['a', 'b']);
  });
});
