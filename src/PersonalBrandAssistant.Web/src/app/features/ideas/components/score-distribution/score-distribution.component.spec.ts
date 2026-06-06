import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScoreDistributionComponent } from './score-distribution.component';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

function idea(score: number | null): Idea {
  return { id: crypto.randomUUID(), title: 't', description: null, url: null, sourceName: 's',
    category: null, summary: null, thumbnailUrl: null, status: IdeaStatus.New, tags: [],
    detectedAt: '2026-06-06', hasSavedDetails: false, score, scoreReason: null, isDuplicate: false };
}

describe('ScoreDistributionComponent', () => {
  let fixture: ComponentFixture<ScoreDistributionComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ScoreDistributionComponent] }).compileComponents();
    fixture = TestBed.createComponent(ScoreDistributionComponent);
  });

  it('counts ideas per band, ignoring unscored', () => {
    fixture.componentRef.setInput('ideas', [idea(9), idea(8), idea(5), idea(2), idea(null)]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="band-high"]')?.textContent).toContain('2');
    expect(el.querySelector('[data-testid="band-mid"]')?.textContent).toContain('1');
    expect(el.querySelector('[data-testid="band-low"]')?.textContent).toContain('1');
  });
});
