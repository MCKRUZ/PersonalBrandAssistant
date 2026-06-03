import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduleDialogComponent } from './schedule-dialog.component';

describe('ScheduleDialogComponent', () => {
  let fixture: ComponentFixture<ScheduleDialogComponent>;
  let component: ScheduleDialogComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ScheduleDialogComponent] });
    fixture = TestBed.createComponent(ScheduleDialogComponent);
    component = fixture.componentInstance;
  });

  it('is hidden when not visible', () => {
    fixture.componentRef.setInput('visible', false);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="schedule-dialog"]')).toBeNull();
  });

  it('renders when visible', () => {
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="schedule-dialog"]')).toBeTruthy();
  });

  it('emits cancelled when cancel pressed', () => {
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    spyOn(component.cancelled, 'emit');
    fixture.nativeElement.querySelector('[data-testid="schedule-cancel"] button').click();
    expect(component.cancelled.emit).toHaveBeenCalled();
  });

  it('emits confirmed with an ISO string on confirm', () => {
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    let emitted: string | undefined;
    component.confirmed.subscribe((v) => (emitted = v));
    const date = new Date('2026-07-01T10:00:00Z');
    component.value.set(date);
    component.onConfirm();
    expect(emitted).toBe(date.toISOString());
  });

  it('does not emit on confirm when no date selected', () => {
    spyOn(component.confirmed, 'emit');
    component.value.set(null);
    component.onConfirm();
    expect(component.confirmed.emit).not.toHaveBeenCalled();
  });
});
