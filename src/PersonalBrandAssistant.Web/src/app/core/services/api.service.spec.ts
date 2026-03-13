import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { MessageService } from 'primeng/api';
import { ApiService } from './api.service';
import { apiKeyInterceptor } from '../interceptors/api-key.interceptor';
import { errorInterceptor } from '../interceptors/error.interceptor';
import { environment } from '../../environments/environment';

describe('ApiService', () => {
  let service: ApiService;
  let httpMock: HttpTestingController;
  let messageService: jasmine.SpyObj<MessageService>;

  beforeEach(() => {
    messageService = jasmine.createSpyObj('MessageService', ['add']);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(
          withInterceptors([apiKeyInterceptor, errorInterceptor])
        ),
        provideHttpClientTesting(),
        { provide: MessageService, useValue: messageService },
      ],
    });

    service = TestBed.inject(ApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('should send GET request to correct URL', () => {
    service.get<string>('test-path').subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/test-path`);
    expect(req.request.method).toBe('GET');
    req.flush('ok');
  });

  it('should send POST body as JSON', () => {
    const body = { name: 'test', value: 42 };
    service.post<object>('items', body).subscribe();

    const req = httpMock.expectOne(`${environment.apiUrl}/items`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  describe('with API key set', () => {
    const originalApiKey = environment.apiKey;

    beforeEach(() => {
      (environment as { apiKey: string }).apiKey = 'test-key-123';
    });

    afterEach(() => {
      (environment as { apiKey: string }).apiKey = originalApiKey;
    });

    it('should add X-Api-Key header when apiKey is set', () => {
      service.get<string>('secure').subscribe();

      const req = httpMock.expectOne(`${environment.apiUrl}/secure`);
      expect(req.request.headers.get('X-Api-Key')).toBe('test-key-123');
      req.flush('ok');
    });
  });

  it('should show toast on 500 error', () => {
    service.get<string>('fail').subscribe({
      error: () => {
        /* expected */
      },
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/fail`);
    req.flush('error', { status: 500, statusText: 'Internal Server Error' });

    expect(messageService.add).toHaveBeenCalledWith(
      jasmine.objectContaining({
        severity: 'error',
        detail: 'Server error — please try again',
      })
    );
  });

  it('should show toast on 401 error', () => {
    service.get<string>('unauthorized').subscribe({
      error: () => {
        /* expected */
      },
    });

    const req = httpMock.expectOne(`${environment.apiUrl}/unauthorized`);
    req.flush('', { status: 401, statusText: 'Unauthorized' });

    expect(messageService.add).toHaveBeenCalledWith(
      jasmine.objectContaining({
        severity: 'error',
        detail: 'API key invalid or missing',
      })
    );
  });
});
