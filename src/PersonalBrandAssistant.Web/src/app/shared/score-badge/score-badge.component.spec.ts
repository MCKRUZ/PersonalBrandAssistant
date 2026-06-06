import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScoreBadgeComponent } from './score-badge.component';

describe('ScoreBadgeComponent', () => {
  let fixture: ComponentFixture<ScoreBadgeComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [ScoreBadgeComponent] }).compileComponents();
    fixture = TestBed.createComponent(ScoreBadgeComponent);
  });

  function render(score: number | null) {
    fixture.componentRef.setInput('score', score);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows score out of 10', () => {
    expect(render(8).textContent).toContain('8/10');
  });

  it('uses the success band for scores >= 7', () => {
    expect(render(7).querySelector('.score-badge')?.classList).toContain('band-success');
  });

  it('uses the warning band for scores 4-6', () => {
    expect(render(6).querySelector('.score-badge')?.classList).toContain('band-warning');
    expect(render(4).querySelector('.score-badge')?.classList).toContain('band-warning');
  });

  it('uses the danger band for scores < 4', () => {
    expect(render(3).querySelector('.score-badge')?.classList).toContain('band-danger');
  });

  it('renders nothing when score is null', () => {
    expect(render(null).querySelector('.score-badge')).toBeNull();
  });
});
