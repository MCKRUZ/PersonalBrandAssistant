import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { of } from 'rxjs';
import { ContentListComponent } from './content-list.component';
import { ContentStore } from '../stores/content.store';
import { ContentService } from '../services/content.service';
import { ContentStatus, ContentType, Platform } from '../models/content.model';
import type { Content, ContentDetail } from '../models/content.model';
import type { PagedResult } from '../../../models/pagination.model';

function makeContent(over: Partial<Content> = {}): Content {
  return {
    id: 'content-1',
    title: 'Test Content',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 85,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    ...over,
  };
}

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

describe('ContentListComponent', () => {
  let component: ContentListComponent;
  let fixture: ComponentFixture<ContentListComponent>;
  let store: InstanceType<typeof ContentStore>;
  let router: Router;
  let svc: jasmine.SpyObj<ContentService>;

  beforeEach(() => {
    svc = jasmine.createSpyObj('ContentService', ['list', 'get', 'delete']);
    svc.list.and.returnValue(of(page([])));
    svc.delete.and.returnValue(of(void 0));
    svc.get.and.returnValue(of({ ...makeContent(), body: 'body' } as ContentDetail));

    TestBed.configureTestingModule({
      imports: [ContentListComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideNoopAnimations(),
        ContentStore,
        { provide: ContentService, useValue: svc },
      ],
    });

    fixture = TestBed.createComponent(ContentListComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
    router = TestBed.inject(Router);
  });

  function seed(items: Content[]): void {
    svc.list.and.returnValue(of(page(items)));
  }

  it('loads all content on init', () => {
    fixture.detectChanges();
    expect(svc.list).toHaveBeenCalled();
  });

  it('subtitle reflects allContents().length', () => {
    seed([makeContent({ id: 'a' }), makeContent({ id: 'b' })]);
    fixture.detectChanges();
    expect(component.subtitle()).toContain('2 pieces');
    const sub = fixture.nativeElement.querySelector('.subtitle');
    expect(sub.textContent).toContain('2 pieces moving through your pipeline');
  });

  it('renders the serif Content Studio title', () => {
    fixture.detectChanges();
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1.textContent).toContain('Content Studio');
  });

  it('shows the inspire empty-state when there is no content', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="empty-inspire"]')).toBeTruthy();
  });

  it('shows the filtered empty-state when content exists but nothing matches', () => {
    seed([makeContent({ id: 'a', status: ContentStatus.Draft })]);
    fixture.detectChanges();
    store.setActiveStatus(ContentStatus.Published); // nothing Published
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="empty-filtered"]')).toBeTruthy();
  });

  it('renders the board by default and switches to table from filtered()', () => {
    seed([makeContent({ id: 'a' })]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="content-board"]')).toBeTruthy();

    store.setView('table');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="content-list-table"]')).toBeTruthy();
  });

  it('grid view reads from filtered()', () => {
    seed([makeContent({ id: 'a' })]);
    fixture.detectChanges();
    store.setView('grid');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="content-grid"]')).toBeTruthy();
  });

  it('+ New Content navigates to /content/new', () => {
    spyOn(router, 'navigate');
    fixture.detectChanges();
    component.onNewContent();
    expect(router.navigate).toHaveBeenCalledWith(['/content/new']);
  });

  it('debounces the search input ~300ms before calling setSearch', fakeAsync(() => {
    fixture.detectChanges();
    spyOn(store, 'setSearch');
    component.searchText = 'hello';
    component.onSearchInput();
    tick(150);
    expect(store.setSearch).not.toHaveBeenCalled();
    tick(200);
    expect(store.setSearch).toHaveBeenCalledWith('hello');
  }));

  it('opening a card sets selectedId; closing clears it', () => {
    seed([makeContent({ id: 'a' })]);
    fixture.detectChanges();
    component.onOpen('a');
    expect(component.selectedId()).toBe('a');
    component.onCloseDrawer();
    expect(component.selectedId()).toBeNull();
  });

  it('clear-filters from the filtered empty-state resets status/search/filters', () => {
    seed([makeContent({ id: 'a', status: ContentStatus.Draft })]);
    fixture.detectChanges();
    store.setActiveStatus(ContentStatus.Published);
    store.setFilter('platform', Platform.LinkedIn);
    component.searchText = 'x';
    store.setSearch('x');

    component.onClearFilters();

    expect(store.activeStatus()).toBeNull();
    expect(store.search()).toBe('');
    expect(store.filters().platform).toBeUndefined();
    expect(component.searchText).toBe('');
  });
});
