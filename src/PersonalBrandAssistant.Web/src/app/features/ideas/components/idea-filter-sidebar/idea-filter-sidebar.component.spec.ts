import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { IdeaFilterSidebarComponent } from './idea-filter-sidebar.component';
import { IdeaStore } from '../../store/idea.store';
import { IdeaSourceStore } from '../../store/idea-source.store';
import { IdeaStatus } from '../../../../models/idea.model';

describe('IdeaFilterSidebarComponent', () => {
  let fixture: ComponentFixture<IdeaFilterSidebarComponent>;
  let component: IdeaFilterSidebarComponent;
  let store: InstanceType<typeof IdeaStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeaFilterSidebarComponent],
      providers: [provideHttpClient()],
    }).compileComponents();

    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(IdeaFilterSidebarComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render filter sections', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="filter-sidebar"]')).toBeTruthy();
    const headers = el.querySelectorAll('.filter-section h4');
    expect(headers.length).toBe(4);
  });

  it('should call store.setFilter on source change', () => {
    spyOn(store, 'setFilter');
    component.selectedSourceId = 'source-1';
    component.onSourceChange();
    expect(store.setFilter).toHaveBeenCalledWith({ sourceId: 'source-1' });
  });

  it('should call store.setFilter on category change', () => {
    spyOn(store, 'setFilter');
    component.categoryText = 'Tech';
    component.onCategoryChange();
    expect(store.setFilter).toHaveBeenCalledWith({ category: 'Tech' });
  });

  it('should clear all filters', () => {
    spyOn(store, 'setFilter');
    component.selectedSourceId = 'source-1';
    component.categoryText = 'Tech';
    component.selectedStatus = IdeaStatus.New;
    component.clearAll();
    expect(component.selectedSourceId).toBeNull();
    expect(component.selectedStatus).toBeNull();
    expect(component.categoryText).toBe('');
    expect(store.setFilter).toHaveBeenCalled();
  });
});
