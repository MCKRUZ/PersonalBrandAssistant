import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { TopbarComponent } from './topbar.component';

@Component({
  standalone: true,
  imports: [TopbarComponent],
  template: `<app-topbar
    [pageTitle]="title"
    [sidebarCollapsed]="collapsed"
    (toggleSidebar)="sidebarToggled = true"
    (toggleSidecar)="sidecarToggled = true"
  />`,
})
class TestHostComponent {
  title = 'Dashboard';
  collapsed = false;
  sidebarToggled = false;
  sidecarToggled = false;
}

describe('TopbarComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should display page title', () => {
    const title = fixture.debugElement.query(By.css('.page-title'));
    expect(title.nativeElement.textContent.trim()).toBe('Dashboard');
  });

  it('should update title when input changes', () => {
    host.title = 'Settings';
    fixture.detectChanges();
    const title = fixture.debugElement.query(By.css('.page-title'));
    expect(title.nativeElement.textContent.trim()).toBe('Settings');
  });

  it('should render notification bell', () => {
    const bell = fixture.debugElement.query(By.css('app-notification-bell'));
    expect(bell).toBeTruthy();
  });

  it('should render sidebar toggle button', () => {
    const buttons = fixture.debugElement.queryAll(By.css('p-button'));
    expect(buttons.length).toBeGreaterThanOrEqual(2);
  });
});
