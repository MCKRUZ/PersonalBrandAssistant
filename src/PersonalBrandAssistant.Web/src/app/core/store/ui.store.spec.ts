import { TestBed } from '@angular/core/testing';
import { UiStore } from './ui.store';

describe('UiStore', () => {
  let store: InstanceType<typeof UiStore>;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    store = TestBed.inject(UiStore);
  });

  it('should initialize with sidebar expanded', () => {
    expect(store.sidebarCollapsed()).toBe(false);
  });

  it('should toggle sidebar state', () => {
    store.toggleSidebar();
    expect(store.sidebarCollapsed()).toBe(true);

    store.toggleSidebar();
    expect(store.sidebarCollapsed()).toBe(false);
  });

  it('should set theme preference', () => {
    expect(store.theme()).toBe('light');

    store.setTheme('dark');
    expect(store.theme()).toBe('dark');

    store.setTheme('light');
    expect(store.theme()).toBe('light');
  });
});
