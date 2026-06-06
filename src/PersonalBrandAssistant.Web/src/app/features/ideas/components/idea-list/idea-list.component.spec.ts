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
      description: null,
      url: null,
      score: null,
      scoreReason: null,
      isDuplicate: false,
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
    const rows = fixture.nativeElement.querySelectorAll('.idea-row');
    expect(rows.length).toBe(1);
  });

  it('should render no rows when no ideas', () => {
    fixture.componentRef.setInput('ideas', []);
    fixture.detectChanges();
    const rows = fixture.nativeElement.querySelectorAll('.idea-row');
    expect(rows.length).toBe(0);
  });

  it('should display idea title in row', () => {
    const title = fixture.nativeElement.querySelector('.idea-row .row-title') as HTMLElement;
    expect(title.textContent?.trim()).toBe('First Idea');
  });

  it('should emit save event when save button clicked', () => {
    const emitted: string[] = [];
    component.save.subscribe((id: string) => emitted.push(id));
    const btn = fixture.nativeElement.querySelector('[data-testid="save-btn"] button') as HTMLElement;
    btn.click();
    expect(emitted).toEqual(['idea-1']);
  });

  it('should emit dismiss event when dismiss button clicked', () => {
    const emitted: string[] = [];
    component.dismiss.subscribe((id: string) => emitted.push(id));
    const btn = fixture.nativeElement.querySelector('[data-testid="dismiss-btn"] button') as HTMLElement;
    btn.click();
    expect(emitted).toEqual(['idea-1']);
  });

  it('should emit createContent event when create content button clicked', () => {
    const emitted: string[] = [];
    component.createContent.subscribe((id: string) => emitted.push(id));
    const btn = fixture.nativeElement.querySelector('[data-testid="create-content-btn"] button') as HTMLElement;
    btn.click();
    expect(emitted).toEqual(['idea-1']);
  });

  it('shows a score badge for each scored idea row', () => {
    fixture.componentRef.setInput('ideas', [
      {
        id: 'a',
        title: 'Scored Idea',
        sourceName: 'Feed',
        category: 'AI',
        summary: null,
        thumbnailUrl: null,
        status: IdeaStatus.New,
        tags: [],
        detectedAt: '2026-01-01T00:00:00Z',
        hasSavedDetails: false,
        description: null,
        url: null,
        score: 9,
        scoreReason: 'Very relevant',
        isDuplicate: false,
      },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-score-badge .band-success')).toBeTruthy();
  });
});
