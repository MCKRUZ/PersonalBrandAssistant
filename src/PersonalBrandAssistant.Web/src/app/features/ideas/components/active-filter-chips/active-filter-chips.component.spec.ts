import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActiveFilterChipsComponent } from './active-filter-chips.component';
import { IdeaFilterState } from '../../../../models/idea.model';

const empty: IdeaFilterState = {
  status: null, sourceId: null, category: null, tags: [],
  dateFrom: null, dateTo: null, searchText: null, minScore: null,
};

describe('ActiveFilterChipsComponent', () => {
  let fixture: ComponentFixture<ActiveFilterChipsComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ActiveFilterChipsComponent] }).compileComponents();
    fixture = TestBed.createComponent(ActiveFilterChipsComponent);
  });

  it('renders a chip per active filter', () => {
    fixture.componentRef.setInput('filter', { ...empty, minScore: 7, category: 'AI' });
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('[data-testid="filter-chip"]');
    expect(chips.length).toBe(2);
  });

  it('emits clear with the filter key when a chip is removed', () => {
    let cleared: keyof IdeaFilterState | undefined;
    fixture.componentRef.setInput('filter', { ...empty, minScore: 7 });
    fixture.componentInstance.clear.subscribe((k) => (cleared = k));
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="filter-chip"] button') as HTMLButtonElement).click();
    expect(cleared).toBe('minScore');
  });

  it('renders nothing when no filters are active', () => {
    fixture.componentRef.setInput('filter', empty);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="filter-chip"]').length).toBe(0);
  });
});
