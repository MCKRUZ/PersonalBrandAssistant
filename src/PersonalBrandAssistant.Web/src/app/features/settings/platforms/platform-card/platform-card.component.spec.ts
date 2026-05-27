import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlatformCardComponent } from './platform-card.component';
import type {
  PlatformConfig,
  PlatformStatus,
} from '../../models/platform-connection.model';

describe('PlatformCardComponent', () => {
  let component: PlatformCardComponent;
  let fixture: ComponentFixture<PlatformCardComponent>;

  const oauthConfig: PlatformConfig = {
    platform: 'LinkedIn',
    displayName: 'LinkedIn',
    description: 'OAuth 2.0 authentication',
    connectionType: 'oauth',
  };

  const tokenConfig: PlatformConfig = {
    platform: 'Medium',
    displayName: 'Medium',
    description: 'Integration token authentication',
    connectionType: 'token',
  };

  const noneConfig: PlatformConfig = {
    platform: 'Blog',
    displayName: 'Blog',
    description: 'matthewkruczek.ai static site',
    connectionType: 'none',
  };

  const connectedStatus: PlatformStatus = {
    platform: 'LinkedIn',
    isConnected: true,
    status: 'Connected',
    expiresAt: '2026-08-01T00:00:00Z',
    lastPublishDate: '2026-05-20T12:00:00Z',
    capabilities: null,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlatformCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(PlatformCardComponent);
    component = fixture.componentInstance;
  });

  it('should display platform name', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.platform-name')?.textContent).toContain('LinkedIn');
  });

  it('should show Not Connected for NotConfigured status', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', { ...connectedStatus, status: 'NotConfigured', isConnected: false });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.status-badge');
    expect(badge?.textContent).toContain('Not Connected');
    expect(badge?.classList).toContain('status-not-configured');
  });

  it('should show Connected with green indicator', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', connectedStatus);
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.status-badge');
    expect(badge?.textContent).toContain('Connected');
    expect(badge?.classList).toContain('status-connected');
  });

  it('should show Expired with warning indicator', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', { ...connectedStatus, status: 'Expired' });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.status-badge');
    expect(badge?.textContent).toContain('Expired');
    expect(badge?.classList).toContain('status-expired');
  });

  it('should show expiry date when connected', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', connectedStatus);
    fixture.detectChanges();
    const details = fixture.nativeElement.querySelector('.card-details');
    expect(details?.textContent).toContain('Expires');
  });

  it('should show last publish date when available', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', connectedStatus);
    fixture.detectChanges();
    const details = fixture.nativeElement.querySelector('.card-details');
    expect(details?.textContent).toContain('Last published');
  });

  it('should emit connect event for OAuth platforms', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', null);
    fixture.detectChanges();
    let emitted: string | undefined;
    component.connect.subscribe((v: string) => emitted = v);

    const btn = fixture.nativeElement.querySelector('.btn-connect') as HTMLButtonElement;
    btn.click();

    expect(emitted).toBe('LinkedIn');
  });

  it('should emit disconnect event when Disconnect clicked', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', connectedStatus);
    fixture.detectChanges();
    let emitted: string | undefined;
    component.disconnect.subscribe((v: string) => emitted = v);

    const btn = fixture.nativeElement.querySelector('.btn-disconnect') as HTMLButtonElement;
    btn.click();

    expect(emitted).toBe('LinkedIn');
  });

  it('should show Disconnect only when connected', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', null);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.btn-disconnect')).toBeNull();
  });

  it('should show Connect only when not connected', () => {
    fixture.componentRef.setInput('config', oauthConfig);
    fixture.componentRef.setInput('status', connectedStatus);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.btn-connect')).toBeNull();
  });

  it('should show Always Connected for Blog with no action buttons', () => {
    fixture.componentRef.setInput('config', noneConfig);
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('.status-badge');
    expect(badge?.textContent).toContain('Always Connected');
    expect(fixture.nativeElement.querySelector('.btn-connect')).toBeNull();
    expect(fixture.nativeElement.querySelector('.btn-disconnect')).toBeNull();
  });

  it('should toggle token form for Medium when Connect clicked', () => {
    fixture.componentRef.setInput('config', tokenConfig);
    fixture.componentRef.setInput('status', null);
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('.btn-connect') as HTMLButtonElement;
    btn.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('app-medium-token-form')).toBeTruthy();
  });
});
