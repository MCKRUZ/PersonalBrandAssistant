import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Location } from '@angular/common';
import { Component } from '@angular/core';
import { routes } from './app.routes';

@Component({ standalone: true, template: '' })
class DummyComponent {}

describe('App Routes', () => {
  let router: Router;
  let location: Location;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [provideRouter(routes)],
    }).compileComponents();

    router = TestBed.inject(Router);
    location = TestBed.inject(Location);
  });

  it('should redirect empty path to /dashboard', async () => {
    await router.navigateByUrl('/');
    expect(location.path()).toBe('/dashboard');
  });

  it('should navigate to /content', async () => {
    const success = await router.navigateByUrl('/content');
    expect(success).toBeTrue();
    expect(location.path()).toBe('/content');
  });

  it('should navigate to /calendar', async () => {
    const success = await router.navigateByUrl('/calendar');
    expect(success).toBeTrue();
    expect(location.path()).toBe('/calendar');
  });

  it('should navigate to /analytics', async () => {
    const success = await router.navigateByUrl('/analytics');
    expect(success).toBeTrue();
    expect(location.path()).toBe('/analytics');
  });

  it('should navigate to /platforms', async () => {
    const success = await router.navigateByUrl('/platforms');
    expect(success).toBeTrue();
    expect(location.path()).toBe('/platforms');
  });

  it('should navigate to /settings', async () => {
    const success = await router.navigateByUrl('/settings');
    expect(success).toBeTrue();
    expect(location.path()).toBe('/settings');
  });

  it('should redirect unknown paths to /dashboard', async () => {
    await router.navigateByUrl('/nonexistent');
    expect(location.path()).toBe('/dashboard');
  });
});
