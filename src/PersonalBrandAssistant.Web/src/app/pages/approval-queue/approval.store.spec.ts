import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApprovalStore } from './approval.store';
import { ApprovalApiService } from './approval-api.service';
import { ContentItem } from '../../core/models/content.model';
import { environment } from '../../environments/environment';

describe('ApprovalStore', () => {
  let store: InstanceType<typeof ApprovalStore>;
  let httpMock: HttpTestingController;

  const mockItems: ContentItem[] = [
    {
      id: 'item-1', title: 'Post 1', body: 'Body 1', type: 'SocialPost',
      status: 'Review', platform: 'LinkedIn', createdAt: '2026-04-30T08:00:00Z',
      updatedAt: '2026-04-30T08:00:00Z', version: 1, capturedAutonomyLevel: 'Manual',
    },
    {
      id: 'item-2', title: 'Post 2', body: 'Body 2', type: 'SocialPost',
      status: 'Review', platform: 'TwitterX', createdAt: '2026-04-30T09:00:00Z',
      updatedAt: '2026-04-30T09:00:00Z', version: 1, capturedAutonomyLevel: 'Manual',
    },
    {
      id: 'item-3', title: 'Post 3', body: 'Body 3', type: 'BlogPost',
      status: 'Review', platform: 'LinkedIn', createdAt: '2026-04-30T10:00:00Z',
      updatedAt: '2026-04-30T10:00:00Z', version: 1, capturedAutonomyLevel: 'Manual',
    },
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ApprovalStore,
        ApprovalApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    store = TestBed.inject(ApprovalStore);
    httpMock = TestBed.inject(HttpTestingController);

    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush(mockItems);
  });

  afterEach(() => httpMock.verify());

  it('should load pending items on init', () => {
    expect(store.items().length).toBe(3);
    expect(store.isLoading()).toBe(false);
    expect(store.pendingCount()).toBe(3);
  });

  it('should approve and remove item from list', () => {
    store.approve('item-1');
    httpMock.expectOne(`${environment.apiUrl}/approval/item-1/approve`).flush(null);
    expect(store.items().length).toBe(2);
    expect(store.items().find(i => i.id === 'item-1')).toBeUndefined();
  });

  it('should reject and remove item from list', () => {
    store.reject('item-2', 'Too casual');
    const req = httpMock.expectOne(`${environment.apiUrl}/approval/item-2/reject`);
    expect(req.request.body).toEqual({ feedback: 'Too casual' });
    req.flush(null);
    expect(store.items().length).toBe(2);
  });

  it('should batch approve selected items', () => {
    store.toggleSelection('item-1');
    store.toggleSelection('item-2');
    store.batchApprove();
    httpMock.expectOne(`${environment.apiUrl}/approval/batch-approve`).flush({ successCount: 2 });
    expect(store.items().length).toBe(1);
    expect(store.selectedIds().length).toBe(0);
  });

  it('should filter items by platform', () => {
    store.filterByPlatform('LinkedIn');
    expect(store.filteredItems().length).toBe(2);
    expect(store.filteredItems().every(i => i.platform === 'LinkedIn')).toBe(true);
  });

  it('should show all items when filter is null', () => {
    store.filterByPlatform('LinkedIn');
    store.filterByPlatform(null);
    expect(store.filteredItems().length).toBe(3);
  });

  it('should toggle selection', () => {
    store.toggleSelection('item-1');
    expect(store.selectedIds()).toEqual(['item-1']);
    expect(store.hasSelection()).toBe(true);

    store.toggleSelection('item-1');
    expect(store.selectedIds()).toEqual([]);
    expect(store.hasSelection()).toBe(false);
  });

  it('should select all filtered items', () => {
    store.filterByPlatform('LinkedIn');
    store.selectAll();
    expect(store.selectedIds().length).toBe(2);
    expect(store.selectedCount()).toBe(2);
  });

  it('should clear selection', () => {
    store.toggleSelection('item-1');
    store.toggleSelection('item-2');
    store.clearSelection();
    expect(store.selectedIds().length).toBe(0);
  });

  it('should set error on API failure', () => {
    store.loadPending();
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    expect(store.isLoading()).toBe(false);
    expect(store.error()).toBeTruthy();
  });
});
