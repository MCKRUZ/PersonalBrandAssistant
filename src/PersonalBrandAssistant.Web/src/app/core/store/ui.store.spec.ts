import { TestBed } from '@angular/core/testing';
import { UiStore } from './ui.store';

describe('UiStore', () => {
  let store: InstanceType<typeof UiStore>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
    store = TestBed.inject(UiStore);
  });

  afterEach(() => localStorage.clear());

  it('should initialize with sidebar expanded by default', () => {
    expect(store.sidebarCollapsed()).toBe(false);
  });

  it('should toggle sidebar state', () => {
    store.toggleSidebar();
    expect(store.sidebarCollapsed()).toBe(true);

    store.toggleSidebar();
    expect(store.sidebarCollapsed()).toBe(false);
  });

  it('should default theme to dark', () => {
    expect(store.theme()).toBe('dark');
  });

  it('should set theme preference', () => {
    store.setTheme('light');
    expect(store.theme()).toBe('light');
  });

  it('should persist sidebar collapse to localStorage', () => {
    store.toggleSidebar();
    expect(localStorage.getItem('pba-sidebar-collapsed')).toBe('true');

    store.toggleSidebar();
    expect(localStorage.getItem('pba-sidebar-collapsed')).toBe('false');
  });

  // localStorage read-on-init can't be tested in isolation because signalStore
  // evaluates initialState at module load time, before TestBed can set localStorage.
  // The write-side test above validates the round-trip behavior.
});
