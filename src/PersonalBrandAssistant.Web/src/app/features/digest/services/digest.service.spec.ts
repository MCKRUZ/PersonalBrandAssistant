import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { DigestService } from './digest.service';

describe('DigestService', () => {
  let service: DigestService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [DigestService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DigestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('fetches the latest digest', () => {
    service.getLatest().subscribe();
    const req = httpMock.expectOne('/api/digests/latest');
    expect(req.request.method).toBe('GET');
    req.flush({ id: '1', date: '2026-06-05', title: 't', intro: 'i', itemCount: 0, createdAt: '', items: [] });
  });

  it('lists digests', () => {
    service.list().subscribe();
    const req = httpMock.expectOne('/api/digests');
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });
});
