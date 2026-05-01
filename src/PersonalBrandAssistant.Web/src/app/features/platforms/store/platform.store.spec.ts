import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PlatformStore } from './platform.store';
import { Platform } from '../../../shared/models';

const API = 'http://localhost:5000/api';

const mockPlatforms: Platform[] = [
  { id: '1', type: 'LinkedIn', displayName: 'LinkedIn', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
  { id: '2', type: 'TwitterX', displayName: 'Twitter/X', isConnected: false, version: 1, createdAt: '', updatedAt: '' },
  { id: '3', type: 'Instagram', displayName: 'Instagram', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
];

describe('PlatformStore', () => {
  let store: InstanceType<typeof PlatformStore>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [PlatformStore, provideHttpClient(), provideHttpClientTesting()],
    });
    store = TestBed.inject(PlatformStore);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should load platforms on init', fakeAsync(() => {
    const req = httpMock.expectOne(`${API}/platforms`);
    req.flush(mockPlatforms);
    tick();
    expect(store.platforms().length).toBe(3);
    expect(store.loading()).toBe(false);
  }));

  it('should set loading true during load', () => {
    expect(store.loading()).toBe(true);
    httpMock.expectOne(`${API}/platforms`).flush([]);
  });

  it('should set loading false on error', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).error(new ProgressEvent('error'));
    tick();
    expect(store.loading()).toBe(false);
    expect(store.platforms()).toEqual([]);
  }));

  it('should call disconnect endpoint', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).flush(mockPlatforms);
    tick();
    store.disconnect('LinkedIn');
    const req = httpMock.expectOne(`${API}/platforms/LinkedIn/disconnect`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
    tick();
  }));

  it('should set connecting flag', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).flush([]);
    tick();
    store.setConnecting(true);
    expect(store.connecting()).toBe(true);
    store.setConnecting(false);
    expect(store.connecting()).toBe(false);
  }));
});
