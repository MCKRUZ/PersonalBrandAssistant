import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { StudioEmptyStateComponent, IdeaSuggestion } from './studio-empty-state.component';
import { ContentType } from '../../models/content.model';

const SUGGESTIONS: IdeaSuggestion[] = [
  { title: 'Ship faster', blurb: 'A post on velocity.', topic: 'velocity', type: ContentType.BlogPost },
  { title: 'Hot take', blurb: 'A spicy tweet.', topic: 'ai', type: ContentType.Tweet },
];

describe('StudioEmptyStateComponent', () => {
  let fixture: ComponentFixture<StudioEmptyStateComponent>;
  let component: StudioEmptyStateComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StudioEmptyStateComponent],
      providers: [provideRouter([])],
    });
    fixture = TestBed.createComponent(StudioEmptyStateComponent);
    component = fixture.componentInstance;
  });

  it('inspire variant renders idea cards seeded with topic/type query params', () => {
    fixture.componentRef.setInput('variant', 'inspire');
    fixture.componentRef.setInput('suggestions', SUGGESTIONS);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="empty-inspire"]')).toBeTruthy();
    const cards = el.querySelectorAll('[data-testid="idea-card"]');
    expect(cards.length).toBe(2);
    // routerLink + queryParams build a hash-free href to /content/new with the seed params.
    const href = (cards[0] as HTMLAnchorElement).getAttribute('href') ?? '';
    expect(href).toContain('/content/new');
    expect(href).toContain('topic=velocity');
    expect(href).toContain('type=Blog');
  });

  it('inspire variant has a "start from scratch" link to /content/new', () => {
    fixture.componentRef.setInput('variant', 'inspire');
    fixture.componentRef.setInput('suggestions', SUGGESTIONS);
    fixture.detectChanges();
    const scratch = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="start-scratch"]'
    ) as HTMLAnchorElement;
    expect(scratch.getAttribute('href')).toContain('/content/new');
  });

  it('filtered variant shows "Nothing matches" and emits clearFilters', () => {
    fixture.componentRef.setInput('variant', 'filtered');
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="empty-filtered"]')?.textContent).toContain(
      'Nothing matches'
    );
    spyOn(component.clearFilters, 'emit');
    (el.querySelector('[data-testid="clear-filters"] button') as HTMLButtonElement).click();
    expect(component.clearFilters.emit).toHaveBeenCalled();
  });
});
