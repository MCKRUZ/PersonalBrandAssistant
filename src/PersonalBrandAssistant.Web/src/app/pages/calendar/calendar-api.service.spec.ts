import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { CalendarApiService } from './calendar-api.service';
import { CalendarSlotRequest, ContentSeriesRequest } from '../../shared/models';

describe('CalendarApiService', () => {
  let service: CalendarApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CalendarApiService, provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CalendarApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should send GET /api/calendar with from and to query params', () => {
    service.getSlots('2026-05-01', '2026-05-07').subscribe();
    const req = httpMock.expectOne(r => r.url.includes('/api/calendar') && r.method === 'GET');
    expect(req.request.params.get('from')).toBe('2026-05-01');
    expect(req.request.params.get('to')).toBe('2026-05-07');
    req.flush([]);
  });

  it('should send POST /api/calendar/slot with CalendarSlotRequest body', () => {
    const request: CalendarSlotRequest = { scheduledAt: '2026-05-01T10:00:00Z', platform: 'LinkedIn' };
    service.createSlot(request).subscribe();
    const req = httpMock.expectOne(r => r.url.includes('/api/calendar/slot') && r.method === 'POST');
    expect(req.request.body).toEqual(request);
    req.flush({ id: '1', ...request, status: 'Open', isOverride: false, createdAt: '', updatedAt: '' });
  });

  it('should send PUT /api/calendar/slot/{id}/assign with contentId', () => {
    service.assignContent('slot-1', 'content-1').subscribe();
    const req = httpMock.expectOne(r => r.url.includes('/api/calendar/slot/slot-1/assign') && r.method === 'PUT');
    expect(req.request.body).toEqual({ contentId: 'content-1' });
    req.flush(null);
  });

  it('should send POST /api/calendar/auto-fill', () => {
    service.autoFill('2026-05-01', '2026-05-07').subscribe();
    const req = httpMock.expectOne(r => r.url.includes('/api/calendar/auto-fill') && r.method === 'POST');
    expect(req.request.body).toEqual({ from: '2026-05-01', to: '2026-05-07' });
    req.flush(null);
  });

  it('should send POST /api/calendar/series with ContentSeriesRequest body', () => {
    const request: ContentSeriesRequest = {
      name: 'Weekly LinkedIn',
      recurrenceRule: 'FREQ=WEEKLY',
      contentType: 'SocialPost',
      targetPlatforms: ['LinkedIn'],
      themeTags: [],
      timeZoneId: 'America/New_York',
      startsAt: '2026-05-01T00:00:00Z',
    };
    service.createSeries(request).subscribe();
    const req = httpMock.expectOne(r => r.url.includes('/api/calendar/series') && r.method === 'POST');
    expect(req.request.body).toEqual(request);
    req.flush({ id: '1', ...request, isActive: true, createdAt: '', updatedAt: '' });
  });
});
