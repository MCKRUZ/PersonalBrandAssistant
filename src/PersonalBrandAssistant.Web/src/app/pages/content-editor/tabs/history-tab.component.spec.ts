import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HistoryTabComponent } from './history-tab.component';

describe('HistoryTabComponent', () => {
  let component: HistoryTabComponent;
  let fixture: ComponentFixture<HistoryTabComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HistoryTabComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(HistoryTabComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('executions', []);
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should show empty state when no executions', () => {
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-history');
    expect(empty).toBeTruthy();
  });

  it('should format recent timestamps as minutes ago', () => {
    const twoMinAgo = new Date(Date.now() - 120000).toISOString();
    expect(component.relativeDate(twoMinAgo)).toBe('2m ago');
  });

  it('should format older timestamps as hours ago', () => {
    const threeHrAgo = new Date(Date.now() - 3 * 3600000).toISOString();
    expect(component.relativeDate(threeHrAgo)).toBe('3h ago');
  });
});
