import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { DailyBriefComponent } from './daily-brief.component';
import { DigestService } from '../../services/digest.service';

describe('DailyBriefComponent', () => {
  let fixture: ComponentFixture<DailyBriefComponent>;
  const digest = {
    id: '1', date: '2026-06-05', title: 'Daily Brief', intro: 'Today in AI.',
    itemCount: 1, createdAt: '',
    items: [{ ideaId: 'a', rank: 1, score: 9, whyItMatters: 'Big.', title: 'Story A', url: 'http://a' }],
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyBriefComponent],
      providers: [{ provide: DigestService, useValue: { getLatest: () => of(digest) } }],
    }).compileComponents();
    fixture = TestBed.createComponent(DailyBriefComponent);
    fixture.detectChanges();
  });

  it('renders the digest intro and ranked items', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Today in AI.');
    expect(text).toContain('Story A');
    expect(text).toContain('Big.');
  });
});
