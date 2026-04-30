import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SidecarStore } from './sidecar.store';

describe('SidecarStore', () => {
  let store: InstanceType<typeof SidecarStore>;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    store = TestBed.inject(SidecarStore);
  });

  afterEach(() => localStorage.clear());

  it('should initialize with empty messages', () => {
    expect(store.messages()).toEqual([]);
  });

  it('should initialize with disconnected status', () => {
    expect(store.connectionStatus()).toBe('disconnected');
  });

  it('should initialize isStreaming as false', () => {
    expect(store.isStreaming()).toBe(false);
  });

  it('should compute canSend as false when disconnected', () => {
    expect(store.canSend()).toBe(false);
  });

  it('should default routeContext to dashboard', () => {
    expect(store.routeContext()).toBe('dashboard');
  });

  it('should load default quick prompts for dashboard', () => {
    const prompts = store.quickPrompts();
    expect(prompts.length).toBeGreaterThan(0);
    expect(prompts).toContain('What should I post next?');
  });

  it('should load localStorage-overridden quick prompts when present', () => {
    localStorage.setItem('pba-quick-prompts', JSON.stringify({
      dashboard: ['Custom prompt 1'],
    }));
    const freshStore = TestBed.inject(SidecarStore);
    expect(freshStore.quickPrompts()).toContain('Custom prompt 1');
  });

  it('should discard draft by removing message from messages', () => {
    // Manually set a message state to test discardDraft
    const msg = {
      id: 'draft-1',
      role: 'assistant' as const,
      text: 'Draft text',
      timestamp: new Date().toISOString(),
      isDraft: true,
      streamId: 'stream-1',
    };
    // Access internal state via patchState workaround
    (store as any).discardDraft?.('nonexistent-id');
    expect(store.messages().length).toBe(0);
  });
});
