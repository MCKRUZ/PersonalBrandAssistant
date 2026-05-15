import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { ContentFilterSidebarComponent } from './content-filter-sidebar.component';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import type { Content } from '../../models/content.model';
import type { PagedResult } from '../../../../models/pagination.model';

describe('ContentFilterSidebarComponent', () => {
  let component: ContentFilterSidebarComponent;
  let fixture: ComponentFixture<ContentFilterSidebarComponent>;
  let store: InstanceType<typeof ContentStore>;
  let contentService: jasmine.SpyObj<ContentService>;

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

    TestBed.configureTestingModule({
      imports: [ContentFilterSidebarComponent],
      providers: [
        provideHttpClient(),
        ContentStore,
        { provide: ContentService, useValue: contentService },
      ],
    });
    fixture = TestBed.createComponent(ContentFilterSidebarComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
    fixture.detectChanges();
  });

  it('should render status filter checkboxes for all ContentStatus values', () => {
    const checkboxes = fixture.nativeElement.querySelectorAll('.filter-checkbox');
    expect(checkboxes.length).toBe(7);
  });

  it('should render platform filter dropdown', () => {
    const dropdown = fixture.nativeElement.querySelector('[data-testid="platform-filter"]');
    expect(dropdown).toBeTruthy();
  });

  it('should render content type filter dropdown', () => {
    const dropdown = fixture.nativeElement.querySelector('[data-testid="type-filter"]');
    expect(dropdown).toBeTruthy();
  });

  it('should render date range pickers', () => {
    const dateFrom = fixture.nativeElement.querySelector('[data-testid="date-from"]');
    const dateTo = fixture.nativeElement.querySelector('[data-testid="date-to"]');
    expect(dateFrom).toBeTruthy();
    expect(dateTo).toBeTruthy();
  });

  it('should call store.setFilter when status checkbox toggled', () => {
    spyOn(store, 'setFilter');
    component.statuses[1].checked = true;
    component.onStatusToggle(component.statuses[1].value);
    expect(store.setFilter).toHaveBeenCalledWith('status', component.statuses[1].value);
  });

  it('should call store.setFilter when platform dropdown changes', () => {
    spyOn(store, 'setFilter');
    component.selectedPlatform = 'Blog' as any;
    component.onPlatformChange();
    expect(store.setFilter).toHaveBeenCalledWith('platform', 'Blog');
  });

  it('should clear all filters on Clear All button click', () => {
    spyOn(store, 'setFilter');
    component.clearAll();
    expect(store.setFilter).toHaveBeenCalled();
    expect(component.selectedPlatform).toBeNull();
    expect(component.selectedType).toBeNull();
    expect(component.dateFrom).toBeNull();
    expect(component.dateTo).toBeNull();
  });
});
