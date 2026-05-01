import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApprovalApiService } from './approval-api.service';
import { environment } from '../../environments/environment';

describe('ApprovalApiService', () => {
  let service: ApprovalApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        ApprovalApiService,
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    service = TestBed.inject(ApprovalApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should call GET /api/approval/pending with pageSize', () => {
    service.getPending(20).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=20`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
  });

  it('should call POST /api/approval/{id}/approve', () => {
    service.approve('abc-123').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/approval/abc-123/approve`);
    expect(req.request.method).toBe('POST');
    req.flush(null);
  });

  it('should call POST /api/approval/{id}/reject with feedback body', () => {
    service.reject('abc-123', 'Too casual').subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/approval/abc-123/reject`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ feedback: 'Too casual' });
    req.flush(null);
  });

  it('should call POST /api/approval/batch-approve with contentIds', () => {
    service.batchApprove(['id-1', 'id-2']).subscribe();
    const req = httpMock.expectOne(`${environment.apiUrl}/approval/batch-approve`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ contentIds: ['id-1', 'id-2'] });
    req.flush({ successCount: 2 });
  });
});
