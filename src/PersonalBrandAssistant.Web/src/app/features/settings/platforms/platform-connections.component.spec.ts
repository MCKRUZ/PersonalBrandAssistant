import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { of, throwError } from 'rxjs';
import { PlatformConnectionsComponent } from './platform-connections.component';
import { PlatformConnectionService } from '../services/platform-connection.service';
import type { PlatformStatus } from '../models/platform-connection.model';

describe('PlatformConnectionsComponent', () => {
  let component: PlatformConnectionsComponent;
  let fixture: ComponentFixture<PlatformConnectionsComponent>;
  let mockService: jasmine.SpyObj<PlatformConnectionService>;

  const mockPlatforms: PlatformStatus[] = [
    {
      platform: 'Blog',
      isConnected: true,
      status: 'Connected',
      expiresAt: null,
      lastPublishDate: null,
      capabilities: null,
    },
    {
      platform: 'LinkedIn',
      isConnected: false,
      status: 'NotConfigured',
      expiresAt: null,
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

  beforeEach(async () => {
    mockService = jasmine.createSpyObj('PlatformConnectionService', [
      'getPlatforms',
      'getAuthorizeUrl',
      'storeCredentials',
      'disconnect',
    ]);
    mockService.getPlatforms.and.returnValue(of(mockPlatforms));
    mockService.getAuthorizeUrl.and.callFake(
      (p: string) => `/api/auth/${p}/authorize`
    );

    await TestBed.configureTestingModule({
      imports: [PlatformConnectionsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PlatformConnectionService, useValue: mockService },
        {
          provide: ActivatedRoute,
          useValue: { queryParams: of({}) },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PlatformConnectionsComponent);
    component = fixture.componentInstance;
  });

  it('should load platforms on init', fakeAsync(() => {
    fixture.detectChanges();
    tick();
    expect(mockService.getPlatforms).toHaveBeenCalled();
    expect(component.platforms).toEqual(mockPlatforms);
    expect(component.loading).toBeFalse();
  }));

  it('should render platform cards', fakeAsync(() => {
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    const cards = fixture.nativeElement.querySelectorAll('app-platform-card');
    expect(cards.length).toBe(5);
  }));

  it('should show error state when API fails', fakeAsync(() => {
    mockService.getPlatforms.and.returnValue(throwError(() => new Error('fail')));
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    expect(component.error).toBeTrue();
    expect(fixture.nativeElement.querySelector('.error-state')).toBeTruthy();
  }));

  it('should show success notification from query param', fakeAsync(() => {
    TestBed.resetTestingModule();
    mockService.getPlatforms.and.returnValue(of(mockPlatforms));

    TestBed.configureTestingModule({
      imports: [PlatformConnectionsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PlatformConnectionService, useValue: mockService },
        {
          provide: ActivatedRoute,
          useValue: { queryParams: of({ connected: 'LinkedIn' }) },
        },
      ],
    });

    const fix = TestBed.createComponent(PlatformConnectionsComponent);
    fix.detectChanges();
    tick();

    expect(fix.componentInstance.notification?.type).toBe('success');
    expect(fix.componentInstance.notification?.message).toContain('LinkedIn');
  }));

  it('should show error notification from query param', fakeAsync(() => {
    TestBed.resetTestingModule();
    mockService.getPlatforms.and.returnValue(of(mockPlatforms));

    TestBed.configureTestingModule({
      imports: [PlatformConnectionsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: PlatformConnectionService, useValue: mockService },
        {
          provide: ActivatedRoute,
          useValue: { queryParams: of({ error: 'auth_failed' }) },
        },
      ],
    });

    const fix = TestBed.createComponent(PlatformConnectionsComponent);
    fix.detectChanges();
    tick();

    expect(fix.componentInstance.notification?.type).toBe('error');
  }));

  it('should call disconnect and reload on disconnect event', fakeAsync(() => {
    mockService.disconnect.and.returnValue(of(void 0));
    fixture.detectChanges();
    tick();

    const callsBefore = mockService.getPlatforms.calls.count();
    component.onDisconnect('LinkedIn');
    tick();

    expect(mockService.disconnect).toHaveBeenCalledWith('LinkedIn');
    expect(mockService.getPlatforms.calls.count()).toBeGreaterThan(callsBefore);
  }));

  it('should call storeCredentials and reload on submit', fakeAsync(() => {
    mockService.storeCredentials.and.returnValue(of(void 0));
    fixture.detectChanges();
    tick();

    const callsBefore = mockService.getPlatforms.calls.count();
    component.onCredentialsSubmitted({
      platform: 'Medium',
      credentials: { token: 'test-token-12345' },
    });
    tick();

    expect(mockService.storeCredentials).toHaveBeenCalledWith('Medium', {
      token: 'test-token-12345',
    });
    expect(mockService.getPlatforms.calls.count()).toBeGreaterThan(callsBefore);
  }));
});
