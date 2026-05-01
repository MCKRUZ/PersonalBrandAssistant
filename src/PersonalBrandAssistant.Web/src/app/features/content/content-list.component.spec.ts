import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Router } from '@angular/router';
import { MessageService, ConfirmationService } from 'primeng/api';
import { of } from 'rxjs';
import { ContentListComponent } from './content-list.component';
import { ContentStore } from './store/content.store';
import { ContentService } from './services/content.service';
import { Content } from '../../shared/models';

function makeContent(overrides: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Test Post',
    body: 'Hello',
    contentType: 'SocialPost',
    status: 'Draft',
    targetPlatforms: ['LinkedIn'],
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
    version: 1,
    ...overrides,
  } as Content;
}

describe('ContentListComponent', () => {
  let component: ContentListComponent;
  let fixture: ComponentFixture<ContentListComponent>;
  let serviceSpy: jasmine.SpyObj<ContentService>;
  let router: Router;

  beforeEach(async () => {
    serviceSpy = jasmine.createSpyObj('ContentService', ['getAll', 'remove', 'getById', 'getAllowedTransitions', 'getBrandVoiceScore', 'getAuditLog']);
    serviceSpy.getAll.and.returnValue(of({ items: [makeContent(), makeContent({ id: 'c2', title: 'Second' })], cursor: undefined, hasMore: false }));

    await TestBed.configureTestingModule({
      imports: [ContentListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ContentService, useValue: serviceSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContentListComponent);
    component = fixture.componentInstance;
    router = TestBed.inject(Router);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render table with rows matching item count', () => {
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('p-table tbody tr');
    expect(rows.length).toBe(2);
  });

  it('should render filter controls (type, status, platform, search)', () => {
    fixture.detectChanges();
    const selects = fixture.nativeElement.querySelectorAll('p-select');
    expect(selects.length).toBe(3);
    const searchInput = fixture.nativeElement.querySelector('.search-input');
    expect(searchInput).toBeTruthy();
  });

  it('should render table columns: Title, Type, Platform, Status, Created, Actions', () => {
    fixture.detectChanges();
    const headers = fixture.nativeElement.querySelectorAll('th');
    expect(headers.length).toBe(6);
    expect(headers[0].textContent).toContain('Title');
    expect(headers[2].textContent).toContain('Platform');
  });

  it('should navigate to edit on row click', () => {
    const spy = spyOn(router, 'navigate');
    fixture.detectChanges();
    const row = fixture.nativeElement.querySelector('p-table tbody tr');
    row?.click();
    expect(spy).toHaveBeenCalledWith(['/content', 'c1']);
  });

  it('should show empty state when no items', () => {
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));
    fixture.detectChanges();
    const emptyState = fixture.nativeElement.querySelector('app-empty-state');
    expect(emptyState).toBeTruthy();
  });

  it('should call store.loadContent with platform filter', () => {
    fixture.detectChanges();
    component.platformFilter = 'LinkedIn';
    component.applyFilters();
    expect(serviceSpy.getAll).toHaveBeenCalledWith(jasmine.objectContaining({ platform: 'LinkedIn' }));
  });

  it('should debounce search input', fakeAsync(() => {
    fixture.detectChanges();
    serviceSpy.getAll.calls.reset();
    component.searchText.set('angular');
    fixture.detectChanges();
    tick(350);
    expect(serviceSpy.getAll).toHaveBeenCalledWith(jasmine.objectContaining({ search: 'angular' }));
  }));

  it('should show loading spinner during initial load', () => {
    serviceSpy.getAll.and.returnValue(of({ items: [], cursor: undefined, hasMore: false }));
    TestBed.inject(ContentStore).loadContent({});
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('app-empty-state');
    expect(empty).toBeTruthy();
  });
});
