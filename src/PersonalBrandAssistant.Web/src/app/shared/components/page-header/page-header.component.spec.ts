import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import {
  PageHeaderComponent,
  PageAction,
} from './page-header.component';

@Component({
  standalone: true,
  imports: [PageHeaderComponent],
  template: `<app-page-header [title]="title" [actions]="actions" />`,
})
class TestHostComponent {
  title = 'Test Title';
  actions: PageAction[] = [];
}

describe('PageHeaderComponent', () => {
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

  it('should render the title', () => {
    const h1 = fixture.nativeElement.querySelector('h1');
    expect(h1.textContent).toContain('Test Title');
  });

  it('should render action buttons when provided', () => {
    host.actions = [
      { label: 'Create', icon: 'pi pi-plus', command: () => {} },
      { label: 'Export', command: () => {} },
    ];
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('.actions p-button, .actions button');
    expect(buttons.length).toBeGreaterThanOrEqual(2);
  });
});
