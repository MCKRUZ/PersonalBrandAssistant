import { TestBed } from '@angular/core/testing';
import { AuthStore } from './auth.store';

describe('AuthStore', () => {
  let store: InstanceType<typeof AuthStore>;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    store = TestBed.inject(AuthStore);
  });

  it('should initialize with empty user info', () => {
    expect(store.displayName()).toBe('');
    expect(store.email()).toBe('');
  });

  it('should set user info', () => {
    store.setUser('Matt Kruczek', 'matt@example.com');

    expect(store.displayName()).toBe('Matt Kruczek');
    expect(store.email()).toBe('matt@example.com');
  });
});
