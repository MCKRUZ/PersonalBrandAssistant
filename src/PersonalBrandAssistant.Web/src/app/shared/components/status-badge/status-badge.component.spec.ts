import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { StatusBadgeComponent } from './status-badge.component';

@Component({
  standalone: true,
  imports: [StatusBadgeComponent],
  template: `<app-status-badge [status]="status" />`,
})
class TestHostComponent {
  status = 'Draft';
}

describe('StatusBadgeComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  const statusTests: { status: string; expected: string }[] = [
    { status: 'Draft', expected: 'secondary' },
    { status: 'Review', expected: 'info' },
    { status: 'Approved', expected: 'success' },
    { status: 'Scheduled', expected: 'warn' },
    { status: 'Publishing', expected: 'warn' },
    { status: 'Published', expected: 'success' },
    { status: 'Failed', expected: 'danger' },
    { status: 'Archived', expected: 'secondary' },
  ];

  statusTests.forEach(({ status, expected }) => {
    it(`should render ${expected} severity for ${status}`, () => {
      host.status = status;
      fixture.detectChanges();

      const badge = fixture.debugElement.query(By.directive(StatusBadgeComponent));
      expect(badge).toBeTruthy();

      const component = badge.query(By.css('app-status-badge'))
        ? badge.componentInstance
        : badge.children[0]?.componentInstance;

      const statusBadge = fixture.debugElement
        .query(By.directive(StatusBadgeComponent))
        .componentInstance as StatusBadgeComponent;
      expect(statusBadge.severity()).toBe(expected);
    });
  });
});
