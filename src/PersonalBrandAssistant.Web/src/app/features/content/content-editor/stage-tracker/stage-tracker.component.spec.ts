import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StageTrackerComponent } from './stage-tracker.component';
import { ContentStatus } from '../../models/content.model';

describe('StageTrackerComponent', () => {
  let fixture: ComponentFixture<StageTrackerComponent>;
  let component: StageTrackerComponent;

  function setup(status: ContentStatus | null) {
    TestBed.configureTestingModule({ imports: [StageTrackerComponent] });
    fixture = TestBed.createComponent(StageTrackerComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('status', status);
    fixture.detectChanges();
  }

  const cases: [ContentStatus, number][] = [
    [ContentStatus.Idea, 0],
    [ContentStatus.Draft, 1],
    [ContentStatus.Review, 2],
    [ContentStatus.Approved, 3],
    [ContentStatus.Scheduled, 4],
    [ContentStatus.Published, 5],
  ];

  for (const [status, idx] of cases) {
    it(`maps ${status} to active index ${idx}`, () => {
      setup(status);
      expect(component.activeIndex()).toBe(idx);
    });
  }

  it('renders 6 dots for a linear status', () => {
    setup(ContentStatus.Draft);
    const dots = fixture.nativeElement.querySelectorAll('[data-testid="stage-dot"]');
    expect(dots.length).toBe(6);
  });

  it('renders Archived as an all-muted terminal state with no active dot', () => {
    setup(ContentStatus.Archived);
    expect(component.activeIndex()).toBe(-1);
    expect(component.isArchived()).toBeTrue();
    const label = fixture.nativeElement.querySelector('[data-testid="archived-label"]');
    expect(label?.textContent).toContain('Archived');
    const active = fixture.nativeElement.querySelector('.dot.active');
    expect(active).toBeFalsy();
  });

  it('marks dots before the active index as completed and after as empty', () => {
    setup(ContentStatus.Review); // active index 2
    const dots = Array.from(fixture.nativeElement.querySelectorAll('[data-testid="stage-dot"]')) as HTMLElement[];
    expect(dots[0].classList.contains('completed')).toBeTrue();
    expect(dots[1].classList.contains('completed')).toBeTrue();
    expect(dots[2].classList.contains('active')).toBeTrue();
    expect(dots[3].classList.contains('empty')).toBeTrue();
    expect(dots[5].classList.contains('empty')).toBeTrue();
  });
});
