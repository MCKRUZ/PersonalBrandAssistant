import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { SidecarSignalRService } from './sidecar-signalr.service';

describe('SidecarSignalRService', () => {
  let service: SidecarSignalRService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SidecarSignalRService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should start with disconnected status', () => {
    expect(service.connectionStatus()).toBe('disconnected');
  });

  it('should expose on() method for registering callbacks', () => {
    expect(typeof service.on).toBe('function');
  });

  it('should expose invoke() method', () => {
    expect(typeof service.invoke).toBe('function');
  });

  it('should not throw on invoke when not connected', async () => {
    await expectAsync(service.invoke('SendMessage', 'ctx', 'text')).toBeResolved();
  });

  it('should not throw on disconnect when no connection exists', async () => {
    await expectAsync(service.disconnect()).toBeResolved();
    expect(service.connectionStatus()).toBe('disconnected');
  });
});
