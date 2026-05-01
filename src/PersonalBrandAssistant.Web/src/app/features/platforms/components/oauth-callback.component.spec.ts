import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { of, throwError } from 'rxjs';
import { OAuthCallbackComponent } from './oauth-callback.component';
import { PlatformService } from '../services/platform.service';

describe('OAuthCallbackComponent', () => {
  let component: OAuthCallbackComponent;
  let fixture: ComponentFixture<OAuthCallbackComponent>;
  let platformService: jasmine.SpyObj<PlatformService>;
  let router: jasmine.SpyObj<Router>;
  let messageService: jasmine.SpyObj<MessageService>;

  function createComponent(routeParams: Record<string, string>, queryParams: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [OAuthCallbackComponent],
      providers: [
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              params: routeParams,
              queryParams: queryParams,
            },
          },
        },
        { provide: PlatformService, useValue: platformService },
        { provide: Router, useValue: router },
        { provide: MessageService, useValue: messageService },
      ],
    });
    fixture = TestBed.createComponent(OAuthCallbackComponent);
    component = fixture.componentInstance;
  }

  beforeEach(() => {
    platformService = jasmine.createSpyObj('PlatformService', ['handleCallback']);
    router = jasmine.createSpyObj('Router', ['navigate']);
    messageService = jasmine.createSpyObj('MessageService', ['add']);
    router.navigate.and.returnValue(Promise.resolve(true));
  });

  // ========================================================================
  // BUG: LinkedIn OAuth callback uses lowercase 'linkedin' in the URL path
  // (e.g., /platforms/linkedin/callback?code=xxx&state=yyy) but the API
  // requires PascalCase 'LinkedIn'. The component must normalize the case.
  // ========================================================================

  it('should normalize lowercase "linkedin" route param to PascalCase "LinkedIn"', fakeAsync(() => {
    platformService.handleCallback.and.returnValue(of(undefined as any));

    createComponent(
      { type: 'linkedin' },  // LinkedIn redirects with lowercase
      { code: 'test-auth-code', state: 'test-state-token' },
    );

    fixture.detectChanges(); // triggers ngOnInit
    tick();

    expect(platformService.handleCallback).toHaveBeenCalledOnceWith(
      'LinkedIn',  // Must be PascalCase for the API
      { code: 'test-auth-code', state: 'test-state-token' },
    );
  }));

  it('should normalize lowercase "twitterx" route param to PascalCase "TwitterX"', fakeAsync(() => {
    platformService.handleCallback.and.returnValue(of(undefined as any));

    createComponent(
      { type: 'twitterx' },
      { code: 'test-code', state: 'test-state' },
    );

    fixture.detectChanges();
    tick();

    expect(platformService.handleCallback).toHaveBeenCalledOnceWith(
      'TwitterX',
      { code: 'test-code', state: 'test-state' },
    );
  }));

  it('should handle PascalCase route param without modification', fakeAsync(() => {
    platformService.handleCallback.and.returnValue(of(undefined as any));

    createComponent(
      { type: 'LinkedIn' },
      { code: 'test-code', state: 'test-state' },
    );

    fixture.detectChanges();
    tick();

    expect(platformService.handleCallback).toHaveBeenCalledOnceWith(
      'LinkedIn',
      { code: 'test-code', state: 'test-state' },
    );
  }));

  it('should show error and navigate to /platforms when code is missing', fakeAsync(() => {
    createComponent(
      { type: 'linkedin' },
      { state: 'test-state' },  // no code
    );

    fixture.detectChanges();
    tick();

    expect(platformService.handleCallback).not.toHaveBeenCalled();
    expect(messageService.add).toHaveBeenCalledWith(
      jasmine.objectContaining({ severity: 'error' }),
    );
    expect(router.navigate).toHaveBeenCalledWith(['/platforms']);
  }));

  it('should show error toast on API failure instead of silent redirect', fakeAsync(() => {
    platformService.handleCallback.and.returnValue(
      throwError(() => ({ error: { detail: 'State expired' } })),
    );

    createComponent(
      { type: 'linkedin' },
      { code: 'test-code', state: 'expired-state' },
    );

    fixture.detectChanges();
    tick();

    expect(messageService.add).toHaveBeenCalledWith(
      jasmine.objectContaining({ severity: 'error', detail: 'State expired' }),
    );
    expect(router.navigate).toHaveBeenCalledWith(['/platforms']);
  }));

  it('should navigate to /platforms and show success on successful callback', fakeAsync(() => {
    platformService.handleCallback.and.returnValue(of(undefined as any));

    createComponent(
      { type: 'linkedin' },
      { code: 'real-code', state: 'real-state' },
    );

    fixture.detectChanges();
    tick();

    expect(messageService.add).toHaveBeenCalledWith(
      jasmine.objectContaining({ severity: 'success' }),
    );
    expect(router.navigate).toHaveBeenCalledWith(['/platforms']);
  }));
});
