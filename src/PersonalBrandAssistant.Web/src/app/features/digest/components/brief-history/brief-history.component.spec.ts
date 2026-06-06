import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BriefHistoryComponent } from './brief-history.component';
import { DigestSummary } from '../../models/digest.model';

const items: DigestSummary[] = [
  { id: 'd2', date: '2026-06-06', title: 'Today', itemCount: 8, createdAt: '2026-06-06T07:00:00Z' },
  { id: 'd1', date: '2026-06-05', title: 'Yesterday', itemCount: 6, createdAt: '2026-06-05T07:00:00Z' },
];

describe('BriefHistoryComponent', () => {
  let fixture: ComponentFixture<BriefHistoryComponent>;
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [BriefHistoryComponent] }).compileComponents();
    fixture = TestBed.createComponent(BriefHistoryComponent);
  });

  it('renders one entry per digest', () => {
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd2');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]').length).toBe(2);
  });

  it('marks the selected entry active', () => {
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd1');
    fixture.detectChanges();
    const active = fixture.nativeElement.querySelector('.history-entry.active');
    expect(active.textContent).toContain('Yesterday');
  });

  it('emits select with the id when an entry is clicked', () => {
    let picked: string | undefined;
    fixture.componentRef.setInput('digests', items);
    fixture.componentRef.setInput('selectedId', 'd2');
    fixture.componentInstance.select.subscribe((id) => (picked = id));
    fixture.detectChanges();
    (fixture.nativeElement.querySelectorAll('[data-testid="history-entry"]')[1] as HTMLElement).click();
    expect(picked).toBe('d1');
  });
});
