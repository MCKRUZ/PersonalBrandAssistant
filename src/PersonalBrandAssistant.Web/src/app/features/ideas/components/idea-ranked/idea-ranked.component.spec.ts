import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { IdeaRankedComponent } from './idea-ranked.component';
import { IdeaStore } from '../../store/idea.store';
import { IdeaStatus } from '../../../../models/idea.model';
import type { Idea } from '../../../../models/idea.model';

function makeIdea(overrides: Partial<Idea>): Idea {
  return {
    id: 'idea-' + Math.random().toString(36).slice(2),
    title: 'Title', description: null, url: null, sourceName: 'Source',
    category: null, summary: null, thumbnailUrl: null, status: IdeaStatus.New,
    tags: [], detectedAt: '2026-01-01T00:00:00Z', hasSavedDetails: false,
    score: 6, scoreReason: null, isDuplicate: false,
    rank: 0.5, brandFit: 0.5, pillarBreakdown: [], isAntiTopic: null,
    isAuthorityTopic: null, recencyFactor: 1, stale: false,
    ...overrides,
  };
}

describe('IdeaRankedComponent', () => {
  let fixture: ComponentFixture<IdeaRankedComponent>;
  let component: IdeaRankedComponent;
  let store: InstanceType<typeof IdeaStore>;
  let el: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [IdeaRankedComponent],
      providers: [provideHttpClient()],
    });
    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(IdeaRankedComponent);
    component = fixture.componentInstance;
    el = fixture.nativeElement as HTMLElement;
  });

  it('renders a numbered Top-N list ordered as given (highest rank first)', () => {
    component.ideas = [
      makeIdea({ id: 'a', title: 'Top', rank: 0.9 }),
      makeIdea({ id: 'b', title: 'Second', rank: 0.5 }),
    ];
    fixture.detectChanges();

    const numerals = el.querySelectorAll('[data-testid="rank-numeral"]');
    expect(numerals[0].textContent?.trim()).toBe('1');
    expect(numerals[1].textContent?.trim()).toBe('2');
    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(2);
  });

  it('shows each pillar breakdown entry (name + score + reason)', () => {
    component.ideas = [
      makeIdea({
        pillarBreakdown: [{ name: 'Agentic Dev', score: 0.75, reason: 'ownable angle' }],
      }),
    ];
    fixture.detectChanges();

    const breakdown = el.querySelector('[data-testid="pillar-breakdown"]')!;
    expect(breakdown.textContent).toContain('Agentic Dev');
    expect(breakdown.textContent).toContain('75%');
    expect(breakdown.textContent).toContain('ownable angle');
  });

  it('shows the authority badge only when isAuthorityTopic is true', () => {
    component.ideas = [makeIdea({ isAuthorityTopic: true })];
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="authority-badge"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="anti-badge"]')).toBeFalsy();
  });

  it('shows the anti-topic badge when isAntiTopic is true', () => {
    component.ideas = [makeIdea({ isAntiTopic: true })];
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="anti-badge"]')).toBeTruthy();
  });

  it('shows the stale indicator when stale is true', () => {
    component.ideas = [makeIdea({ stale: true })];
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="stale-badge"]')).toBeTruthy();
  });

  it('window toggle calls setRankedWindow', () => {
    const spy = spyOn(store, 'setRankedWindow');
    component.ideas = [makeIdea({})];
    fixture.detectChanges();

    (el.querySelector('[data-testid="window-week"] button') as HTMLElement).click();
    expect(spy).toHaveBeenCalledWith('week');

    (el.querySelector('[data-testid="window-today"] button') as HTMLElement).click();
    expect(spy).toHaveBeenCalledWith('today');
  });

  it('shows the empty state when there are no ideas', () => {
    component.ideas = [];
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="ranked-empty"]')).toBeTruthy();
    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(0);
  });

  it('renders an item with a null score without error (score badge hidden)', () => {
    component.ideas = [makeIdea({ score: null })];
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(1);
    expect(el.querySelector('.score-badge')).toBeFalsy(); // badge renders nothing for null
  });

  it('renders only the first rankedTopN items', () => {
    store.setRankedTopN(2);
    component.ideas = [
      makeIdea({ id: 'a' }), makeIdea({ id: 'b' }), makeIdea({ id: 'c' }),
    ];
    fixture.detectChanges();

    expect(el.querySelectorAll('[data-testid="ranked-item"]').length).toBe(2);
  });
});
