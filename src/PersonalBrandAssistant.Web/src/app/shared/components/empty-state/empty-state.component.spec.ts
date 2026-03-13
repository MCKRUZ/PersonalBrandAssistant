import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { EmptyStateComponent } from './empty-state.component';

@Component({
  standalone: true,
  imports: [EmptyStateComponent],
  template: `<app-empty-state [message]="message" [icon]="icon" />`,
})
class TestHostComponent {
  message = 'No items found';
  icon = '';
}

describe('EmptyStateComponent', () => {
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

  it('should render the message', () => {
    const text = fixture.nativeElement.textContent;
    expect(text).toContain('No items found');
  });

  it('should render icon when provided', () => {
    host.icon = 'pi pi-inbox';
    fixture.detectChanges();

    const icon = fixture.nativeElement.querySelector('i');
    expect(icon).toBeTruthy();
  });
});
