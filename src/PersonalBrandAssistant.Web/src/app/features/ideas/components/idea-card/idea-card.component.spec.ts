import { ComponentFixture, TestBed } from '@angular/core/testing';
import { IdeaCardComponent } from './idea-card.component';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

describe('IdeaCardComponent', () => {
  let fixture: ComponentFixture<IdeaCardComponent>;
  let component: IdeaCardComponent;

  const mockIdea: Idea = {
    id: 'idea-1',
    title: 'Test Idea Title',
    sourceName: 'Tech Blog',
    category: 'Tech',
    summary: 'A short summary of the idea',
    thumbnailUrl: null,
    status: IdeaStatus.New,
    tags: ['angular', 'typescript', 'testing', 'extra-tag'],
    detectedAt: '2026-01-15T10:00:00Z',
    hasSavedDetails: false,
    description: null,
    url: null,
    score: null,
    scoreReason: null,
    isDuplicate: false,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeaCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(IdeaCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.detectChanges();
  });

  it('should render title and source name', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.card-title')?.textContent?.trim()).toBe('Test Idea Title');
    expect(el.querySelector('.source-name')?.textContent?.trim()).toBe('Tech Blog');
  });

  it('should render status badge with correct data-status', () => {
    const badge = fixture.nativeElement.querySelector('.status-badge') as HTMLElement;
    expect(badge.getAttribute('data-status')).toBe('New');
    expect(badge.textContent?.trim()).toBe('New');
  });

  it('should display max 3 tags and show +N more', () => {
    const tags = fixture.nativeElement.querySelectorAll('.chip');
    expect(tags.length).toBe(3);
    const more = fixture.nativeElement.querySelector('.more-tags') as HTMLElement;
    expect(more.textContent?.trim()).toBe('+1');
  });

  it('should render summary', () => {
    const summary = fixture.nativeElement.querySelector('.card-summary') as HTMLElement;
    expect(summary.textContent?.trim()).toBe('A short summary of the idea');
  });

  it('should truncate long summary', () => {
    const longSummary = 'A'.repeat(200);
    fixture.componentRef.setInput('idea', { ...mockIdea, summary: longSummary });
    fixture.detectChanges();
    const summary = fixture.nativeElement.querySelector('.card-summary') as HTMLElement;
    expect(summary.textContent!.trim().length).toBeLessThan(200);
    expect(summary.textContent!.trim().endsWith('...')).toBeTrue();
  });

  it('should emit save event on save button click', () => {
    spyOn(component.save, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="save-btn"] button') as HTMLElement;
    btn.click();
    expect(component.save.emit).toHaveBeenCalledWith('idea-1');
  });

  it('should emit dismiss event on dismiss button click', () => {
    spyOn(component.dismiss, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="dismiss-btn"] button') as HTMLElement;
    btn.click();
    expect(component.dismiss.emit).toHaveBeenCalledWith('idea-1');
  });

  it('should emit createContent event on create button click', () => {
    spyOn(component.createContent, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="create-content-btn"] button') as HTMLElement;
    btn.click();
    expect(component.createContent.emit).toHaveBeenCalledWith('idea-1');
  });

  it('renders a score badge when score is present', () => {
    fixture.componentRef.setInput('idea', { ...mockIdea, score: 8, scoreReason: 'Strong angle' });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-testid="idea-score-badge"]');
    expect(badge?.textContent).toContain('8');
  });

  it('does not render a score badge when score is null', () => {
    fixture.componentRef.setInput('idea', { ...mockIdea, score: null });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="idea-score-badge"]')).toBeNull();
  });

  it('renders the shared score badge with the score', () => {
    fixture.componentRef.setInput('idea', { ...mockIdea, score: 9, scoreReason: 'High relevance' });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('app-score-badge .score-badge');
    expect(badge?.textContent).toContain('9/10');
    expect(badge?.classList).toContain('band-success');
  });
});
