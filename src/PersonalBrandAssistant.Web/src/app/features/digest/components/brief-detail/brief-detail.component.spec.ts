import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BriefDetailComponent } from './brief-detail.component';
import { Digest } from '../../models/digest.model';

const digest: Digest = {
  id: 'd1', date: '2026-06-05', title: 'AI Brief', intro: 'Top stories', itemCount: 2,
  createdAt: '2026-06-05T22:00:00Z',
  items: [
    { ideaId: 'a', rank: 1, score: 9, whyItMatters: 'Big', title: 'First', url: 'https://x/a' },
    { ideaId: 'b', rank: 2, score: 7, whyItMatters: 'Also', title: 'Second', url: null },
  ],
};

describe('BriefDetailComponent', () => {
  let fixture: ComponentFixture<BriefDetailComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BriefDetailComponent] }).compileComponents();
    fixture = TestBed.createComponent(BriefDetailComponent);
  });

  it('heroes the rank-1 item', () => {
    fixture.componentRef.setInput('digest', digest);
    fixture.detectChanges();
    const hero = fixture.nativeElement.querySelector('[data-testid="brief-hero"]');
    expect(hero.textContent).toContain('First');
    expect(hero.querySelector('app-score-badge')).toBeTruthy();
  });

  it('lists the remaining items', () => {
    fixture.componentRef.setInput('digest', digest);
    fixture.detectChanges();
    const rest = fixture.nativeElement.querySelectorAll('[data-testid="brief-item"]');
    expect(rest.length).toBe(1);
    expect(rest[0].textContent).toContain('Second');
  });

  it('shows an empty state when digest is null', () => {
    fixture.componentRef.setInput('digest', null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="brief-empty"]')).toBeTruthy();
  });
});
