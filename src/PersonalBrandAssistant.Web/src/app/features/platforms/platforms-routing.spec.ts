import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MessageService } from 'primeng/api';
import { routes } from '../../app.routes';

describe('Platforms OAuth Callback Routing', () => {
  let router: Router;
  let location: Location;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MessageService, useValue: jasmine.createSpyObj('MessageService', ['add']) },
      ],
    });

    router = TestBed.inject(Router);
    location = TestBed.inject(Location);
  });

  it('should route to /platforms without redirect', async () => {
    await router.navigateByUrl('/platforms');
    expect(location.path()).toBe('/platforms');
  });

  it('should route to /platforms/linkedin/callback without redirecting to dashboard', async () => {
    const success = await router.navigateByUrl('/platforms/linkedin/callback?code=test&state=test');
    expect(success).toBeTrue();
    expect(location.path()).toContain('/platforms/linkedin/callback');
  });

  it('should NOT redirect callback to dashboard', async () => {
    await router.navigateByUrl('/platforms/linkedin/callback?code=abc&state=xyz');
    expect(location.path()).not.toBe('/dashboard');
  });
});
