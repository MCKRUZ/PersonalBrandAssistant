import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { IdeaListComponent } from './idea-list.component';
import { IdeaStore } from '../../store/idea.store';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

describe('IdeaListComponent', () => {
  let fixture: ComponentFixture<IdeaListComponent>;
  let component: IdeaListComponent;
  let store: InstanceType<typeof IdeaStore>;

  const mockIdeas: Idea[] = [
    {
      id: 'idea-1',
      title: 'First Idea',
      sourceName: 'Blog',
      category: 'Tech',
      summary: null,
      thumbnailUrl: null,
      status: IdeaStatus.New,
      tags: [],
      detectedAt: '2026-01-01T00:00:00Z',
      hasSavedDetails: false,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeaListComponent],
      providers: [provideHttpClient()],
    }).compileComponents();

    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(IdeaListComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('ideas', mockIdeas);
    fixture.detectChanges();
  });

  it('should render rows for each idea', () => {
    const rows = fixture.nativeElement.querySelectorAll('[data-testid="idea-row"]');
    expect(rows.length).toBe(1);
  });

  it('should show empty state when no ideas', () => {
    fixture.componentRef.setInput('ideas', []);
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-state') as HTMLElement;
    expect(empty).toBeTruthy();
  });

  it('should display idea title in row', () => {
    const title = fixture.nativeElement.querySelector('.list-row .col-title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('First Idea');
  });

  it('should call store.setSort on header click', () => {
    spyOn(store, 'setSort');
    const titleHeader = fixture.nativeElement.querySelectorAll('.sortable')[0] as HTMLElement;
    titleHeader.click();
    expect(store.setSort).toHaveBeenCalledWith({ field: 'title', direction: 'asc' });
  });
});
