import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component } from '@angular/core';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { SidebarComponent } from './sidebar.component';

@Component({
  standalone: true,
  imports: [SidebarComponent],
  template: `<app-sidebar [collapsed]="collapsed" (toggleCollapse)="toggled = true" />`,
})
class TestHostComponent {
  collapsed = false;
  toggled = false;
}

describe('SidebarComponent', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let host: TestHostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render all 4 nav groups', () => {
    const groups = fixture.debugElement.queryAll(By.css('.nav-group'));
    expect(groups.length).toBe(4);
  });

  it('should render correct nav group labels', () => {
    const labels = fixture.debugElement.queryAll(By.css('.nav-group-label'));
    const texts = labels.map(l => l.nativeElement.textContent.trim());
    expect(texts).toEqual(['Create', 'Distribute', 'Analyze', 'System']);
  });

  it('should render correct nav items within groups', () => {
    const items = fixture.debugElement.queryAll(By.css('.nav-item'));
    expect(items.length).toBe(11);
  });

  it('should collapse via input signal', () => {
    host.collapsed = true;
    fixture.detectChanges();
    const sidebar = fixture.debugElement.query(By.css('.sidebar'));
    expect(sidebar.nativeElement.classList).toContain('collapsed');
  });

  it('should emit toggleCollapse on button click', () => {
    const button = fixture.debugElement.query(By.css('.collapse-toggle'));
    button.nativeElement.click();
    expect(host.toggled).toBe(true);
  });
});
