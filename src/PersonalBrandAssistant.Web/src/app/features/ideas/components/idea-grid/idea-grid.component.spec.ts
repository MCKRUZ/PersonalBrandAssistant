import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IdeaGridComponent } from './idea-grid.component';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

describe('IdeaGridComponent', () => {
  let fixture: ComponentFixture<IdeaGridComponent>;
  let component: IdeaGridComponent;

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
    },
    {
      id: 'idea-2',
      title: 'Second Idea',
      sourceName: 'RSS',
      category: 'AI',
      summary: null,
      thumbnailUrl: null,
      status: IdeaStatus.Saved,
      tags: [],
      detectedAt: '2026-01-02T00:00:00Z',
      hasSavedDetails: false,
      description: null,
      url: null,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeaGridComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(IdeaGridComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('ideas', mockIdeas);
    fixture.detectChanges();
  });

  it('should render one card per idea', () => {
    const cards = fixture.nativeElement.querySelectorAll('app-idea-card');
    expect(cards.length).toBe(2);
  });

  it('should show empty state when no ideas', () => {
    fixture.componentRef.setInput('ideas', []);
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-state') as HTMLElement;
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No ideas found');
  });

  it('should propagate save event from card', () => {
    spyOn(component.save, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="save-btn"] button') as HTMLElement;
    btn.click();
    expect(component.save.emit).toHaveBeenCalledWith('idea-1');
  });
});
