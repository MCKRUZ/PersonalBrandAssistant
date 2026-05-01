import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { MessageService } from 'primeng/api';
import { PlatformsListComponent } from './platforms-list.component';
import { Platform } from '../../shared/models';

const API = 'http://localhost:5000/api';

const mockPlatforms: Platform[] = [
  { id: '1', type: 'LinkedIn', displayName: 'LinkedIn', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
  { id: '2', type: 'TwitterX', displayName: 'Twitter/X', isConnected: false, version: 1, createdAt: '', updatedAt: '' },
  { id: '3', type: 'Instagram', displayName: 'Instagram', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
  { id: '4', type: 'Substack', displayName: 'Substack', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
  { id: '5', type: 'PersonalBlog', displayName: 'Personal Blog', isConnected: true, version: 1, createdAt: '', updatedAt: '' },
];

describe('PlatformsListComponent', () => {
  let component: PlatformsListComponent;
  let fixture: ComponentFixture<PlatformsListComponent>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlatformsListComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), MessageService],
    }).compileComponents();

    fixture = TestBed.createComponent(PlatformsListComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should create', () => {
    expect(component).toBeTruthy();
    httpMock.expectOne(`${API}/platforms`).flush([]);
  });

  it('should show loading spinner while loading', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-loading-spinner')).toBeTruthy();
    httpMock.expectOne(`${API}/platforms`).flush([]);
  });

  it('should show empty state when no platforms', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).flush([]);
    tick();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-empty-state')).toBeTruthy();
  }));

  it('should render cards for OAuth platforms only', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).flush(mockPlatforms);
    tick();
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('app-platform-card');
    expect(cards.length).toBe(3);
  }));

  it('should filter out Substack and PersonalBlog', fakeAsync(() => {
    httpMock.expectOne(`${API}/platforms`).flush(mockPlatforms);
    tick();
    expect(component.oauthPlatforms().length).toBe(3);
    expect(component.oauthPlatforms().find(p => p.type === 'Substack')).toBeUndefined();
    expect(component.oauthPlatforms().find(p => p.type === 'PersonalBlog')).toBeUndefined();
  }));
});
