import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { PlatformConnectionService } from './platform-connection.service';
import type {
  PlatformStatus,
  ConnectionStatusResponse,
} from '../models/platform-connection.model';

describe('PlatformConnectionService', () => {
  let service: PlatformConnectionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        PlatformConnectionService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(PlatformConnectionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('getPlatforms() sends GET /api/platforms', () => {
    const mockPlatforms: PlatformStatus[] = [
      {
        platform: 'LinkedIn',
        isConnected: true,
        status: 'Connected',
        expiresAt: '2026-08-01T00:00:00Z',
        lastPublishDate: null,
        capabilities: null,
      },
      {
        platform: 'Medium',
        isConnected: false,
        status: 'NotConfigured',
        expiresAt: null,
        lastPublishDate: null,
        capabilities: null,
      },
    ];

    service.getPlatforms().subscribe((result) => {
      expect(result).toEqual(mockPlatforms);
    });

    const req = httpMock.expectOne('/api/platforms');
    expect(req.request.method).toBe('GET');
    req.flush(mockPlatforms);
  });

  it('getStatus() sends GET /api/auth/{platform}/status', () => {
    const mockStatus: ConnectionStatusResponse = {
      status: 'Connected',
      expiresAt: '2026-08-01T00:00:00Z',
    };

    service.getStatus('LinkedIn').subscribe((result) => {
      expect(result).toEqual(mockStatus);
    });

    const req = httpMock.expectOne('/api/auth/LinkedIn/status');
    expect(req.request.method).toBe('GET');
    req.flush(mockStatus);
  });

  it('getAuthorizeUrl() returns correct URL without HTTP call', () => {
    const url = service.getAuthorizeUrl('LinkedIn');
    expect(url).toBe('/api/auth/LinkedIn/authorize');
  });

  it('storeCredentials() sends POST for Medium token', () => {
    service
      .storeCredentials('Medium', { token: 'abc123' })
      .subscribe();

    const req = httpMock.expectOne('/api/platforms/Medium/credentials');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ token: 'abc123' });
    req.flush(null);
  });

  it('storeCredentials() sends POST for Substack login', () => {
    service
      .storeCredentials('Substack', {
        email: 'a@b.com',
        password: 'pw',
      })
      .subscribe();

    const req = httpMock.expectOne('/api/platforms/Substack/credentials');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.com', password: 'pw' });
    req.flush(null);
  });

  it('disconnect() sends DELETE /api/auth/{platform}', () => {
    service.disconnect('LinkedIn').subscribe();

    const req = httpMock.expectOne('/api/auth/LinkedIn');
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('propagates HTTP errors', () => {
    service.getStatus('LinkedIn').subscribe({
      error: (err) => {
        expect(err.status).toBe(500);
      },
    });

    const req = httpMock.expectOne('/api/auth/LinkedIn/status');
    req.flush('Server Error', { status: 500, statusText: 'Internal Server Error' });
  });
});
