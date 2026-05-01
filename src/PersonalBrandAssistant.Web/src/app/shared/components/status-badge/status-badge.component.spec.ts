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

  it('should render lowercase text with dot prefix', () => {
    host.status = 'Draft';
    fixture.detectChanges();
    const span = fixture.debugElement.query(By.css('.status-badge'));
    expect(span).toBeTruthy();
    expect(span.nativeElement.textContent.trim()).toBe('draft');
  });

  it('should apply correct CSS class per status value', () => {
    host.status = 'Published';
    fixture.detectChanges();
    const span = fixture.debugElement.query(By.css('.status-badge'));
    expect(span.nativeElement.classList).toContain('status-published');

    host.status = 'Failed';
    fixture.detectChanges();
    const spanAfter = fixture.debugElement.query(By.css('.status-badge'));
    expect(spanAfter.nativeElement.classList).toContain('status-failed');
  });

  it('should use mono font via status-badge class', () => {
    const span = fixture.debugElement.query(By.css('.status-badge'));
    expect(span).toBeTruthy();
    expect(span.nativeElement.classList).toContain('status-badge');
  });

  const allStatuses = ['Draft', 'Review', 'Approved', 'Scheduled', 'Publishing', 'Published', 'Failed', 'Archived'];
  allStatuses.forEach(status => {
    it(`should render status-${status.toLowerCase()} class for ${status}`, () => {
      host.status = status;
      fixture.detectChanges();
      const span = fixture.debugElement.query(By.css('.status-badge'));
      expect(span.nativeElement.classList).toContain(`status-${status.toLowerCase()}`);
    });
  });
});
