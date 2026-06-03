import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { FiltersPopoverComponent } from './filters-popover.component';
import { ContentStore } from '../../stores/content.store';
import { ContentService } from '../../services/content.service';
import { ContentType, Platform } from '../../models/content.model';
import type { Content } from '../../models/content.model';
import type { PagedResult } from '../../../../models/pagination.model';

function page(items: Content[]): PagedResult<Content> {
  return { items, totalCount: items.length, page: 1, pageSize: 1000, totalPages: 1 };
}

describe('FiltersPopoverComponent', () => {
  let fixture: ComponentFixture<FiltersPopoverComponent>;
  let component: FiltersPopoverComponent;
  let store: InstanceType<typeof ContentStore>;

  beforeEach(() => {
    const svc = jasmine.createSpyObj('ContentService', ['list']);
    svc.list.and.returnValue(of(page([])));

    TestBed.configureTestingModule({
      imports: [FiltersPopoverComponent],
      providers: [ContentStore, { provide: ContentService, useValue: svc }],
    });

    fixture = TestBed.createComponent(FiltersPopoverComponent);
    component = fixture.componentInstance;
    store = TestBed.inject(ContentStore);
    fixture.detectChanges();
  });

  it('platform selection updates store filters', () => {
    component.selectedPlatform = Platform.LinkedIn;
    component.onPlatformChange();
    expect(store.filters().platform).toBe(Platform.LinkedIn);
  });

  it('type selection updates store filters', () => {
    component.selectedType = ContentType.Tweet;
    component.onTypeChange();
    expect(store.filters().contentType).toBe(ContentType.Tweet);
  });

  it('date range updates store filters', () => {
    component.dateFrom = new Date('2026-01-01T00:00:00Z');
    component.dateTo = new Date('2026-02-01T00:00:00Z');
    component.onDateChange();
    expect(store.filters().dateFrom).toBe(component.dateFrom.toISOString());
    expect(store.filters().dateTo).toBe(component.dateTo.toISOString());
  });

  it('clear resets all filters', () => {
    component.selectedPlatform = Platform.Twitter;
    component.onPlatformChange();
    component.clear();
    expect(store.filters().platform).toBeUndefined();
    expect(store.filters().contentType).toBeUndefined();
    expect(store.filters().dateFrom).toBeUndefined();
    expect(store.filters().dateTo).toBeUndefined();
    expect(component.selectedPlatform).toBeNull();
  });
});
