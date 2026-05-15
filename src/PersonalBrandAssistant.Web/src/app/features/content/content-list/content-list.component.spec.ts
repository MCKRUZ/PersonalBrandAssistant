import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { ContentListComponent } from './content-list.component';
import { ContentStore } from '../stores/content.store';
import { ContentService } from '../services/content.service';
import { ContentStatus, ContentType, Platform } from '../models/content.model';
import type { Content } from '../models/content.model';
import type { PagedResult } from '../../../models/pagination.model';

describe('ContentListComponent', () => {
  let component: ContentListComponent;
  let fixture: ComponentFixture<ContentListComponent>;
  let store: InstanceType<typeof ContentStore>;
  let router: Router;
  let contentService: jasmine.SpyObj<ContentService>;

  const mockContent: Content = {
    id: 'content-1',
    title: 'Test Content',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    voiceScore: 85,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
  };

  const emptyPage: PagedResult<Content> = {
    items: [],
    totalCount: 0,
    page: 1,
    pageSize: 20,
    totalPages: 0,
  };

  beforeEach(() => {
    contentService = jasmine.createSpyObj('ContentService', ['list', 'delete']);
    contentService.list.and.returnValue(of(emptyPage));
    contentService.delete.and.returnValue(of(void 0));

    TestBed.configureTestingModule({
      imports: [ContentListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        ContentStore,
        { provide: ContentService, useValue: contentService },
      ],
    });

    fixture = TestBed.createComponent(ContentListComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
    router = TestBed.inject(Router);
  });

  it('should load contents on init', () => {
    fixture.detectChanges();
    expect(contentService.list).toHaveBeenCalled();
  });

  it('should render the two-column layout', () => {
    fixture.detectChanges();
    const page = fixture.nativeElement.querySelector('[data-testid="content-list-page"]');
    const sidebar = fixture.nativeElement.querySelector('.filter-sidebar');
    const main = fixture.nativeElement.querySelector('.content-main');
    expect(page).toBeTruthy();
    expect(sidebar).toBeTruthy();
    expect(main).toBeTruthy();
  });

  it('should render Content Studio title', () => {
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1.textContent).toContain('Content Studio');
  });

  it('should render New Content button', () => {
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="new-content-btn"]');
    expect(btn).toBeTruthy();
  });

  it('should render search input', () => {
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('[data-testid="search-input"]');
    expect(input).toBeTruthy();
  });

  it('should render content rows when contents exist', fakeAsync(() => {
    contentService.list.and.returnValue(
      of({ items: [mockContent], totalCount: 1, page: 1, pageSize: 20, totalPages: 1 })
    );
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="content-row"]');
    expect(rows.length).toBe(1);
  }));

  it('should show empty state when no contents', () => {
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('[data-testid="empty-state"]');
    expect(empty).toBeTruthy();
  });

  it('should navigate to /content/new on New Content click', () => {
    spyOn(router, 'navigate');
    fixture.detectChanges();
    component.onNewContent();
    expect(router.navigate).toHaveBeenCalledWith(['/content/new']);
  });

  it('should show paginator when totalCount exceeds pageSize', () => {
    contentService.list.and.returnValue(
      of({ items: [mockContent], totalCount: 45, page: 1, pageSize: 20, totalPages: 3 })
    );
    fixture.detectChanges();
    store.loadContents();
    fixture.detectChanges();
    const paginator = fixture.nativeElement.querySelector('[data-testid="paginator"]');
    expect(paginator).toBeTruthy();
  });

  it('should call store.setPage on paginator page change', () => {
    spyOn(store, 'setPage');
    component.onPageChange({ first: 20, rows: 20 });
    expect(store.setPage).toHaveBeenCalledWith(2);
  });

  it('should debounce search input and call store.setFilter', fakeAsync(() => {
    spyOn(store, 'setFilter');
    fixture.detectChanges();
    component.searchText = 'test query';
    component.onSearchInput();
    tick(300);
    expect(store.setFilter).toHaveBeenCalledWith('search', 'test query');
  }));
});
