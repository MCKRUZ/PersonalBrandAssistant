import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { ApprovalQueueComponent } from './approval-queue.component';
import { ApprovalStore } from './approval.store';
import { environment } from '../../environments/environment';

describe('ApprovalQueueComponent', () => {
  let component: ApprovalQueueComponent;
  let fixture: ComponentFixture<ApprovalQueueComponent>;
  let httpMock: HttpTestingController;

  const mockItems = [
    {
      id: 'item-1', title: 'Test Post', body: 'Body text', type: 'SocialPost',
      status: 'Review', platform: 'LinkedIn', createdAt: '2026-04-30T08:00:00Z',
      updatedAt: '2026-04-30T08:00:00Z', version: 1, capturedAutonomyLevel: 'Manual',
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ApprovalQueueComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ApprovalQueueComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush([]);
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should show empty state when no pending items', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush([]);
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-state');
    expect(empty).toBeTruthy();
  });

  it('should render approval cards for pending items', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush(mockItems);
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('.approval-card');
    expect(cards.length).toBe(1);
  });

  it('should show pending count', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush(mockItems);
    fixture.detectChanges();
    const count = fixture.nativeElement.querySelector('.pending-count');
    expect(count.textContent).toContain('1');
  });

  it('should render platform filter chips', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush([]);
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('p-chip');
    expect(chips.length).toBe(8); // All + 7 platforms
  });

  it('should expand card on click', () => {
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush(mockItems);
    fixture.detectChanges();
    const header = fixture.nativeElement.querySelector('.card-header');
    header.click();
    fixture.detectChanges();
    const body = fixture.nativeElement.querySelector('.card-body');
    expect(body).toBeTruthy();
  });

  it('should show loading skeletons when loading', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('p-skeleton')).toBeTruthy();
    httpMock.expectOne(`${environment.apiUrl}/approval/pending?pageSize=50`).flush([]);
  });
});
