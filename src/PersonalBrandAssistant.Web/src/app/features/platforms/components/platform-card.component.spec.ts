import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { PlatformCardComponent } from './platform-card.component';
import { Platform } from '../../../shared/models';

const connectedPlatform: Platform = {
  id: '1', type: 'LinkedIn', displayName: 'LinkedIn', isConnected: true,
  lastSyncAt: new Date(Date.now() - 3600000).toISOString(),
  grantedScopes: ['r_liteprofile', 'w_member_social'],
  version: 1, createdAt: '', updatedAt: '',
};

const disconnectedPlatform: Platform = {
  id: '2', type: 'TwitterX', displayName: 'Twitter/X', isConnected: false,
  version: 1, createdAt: '', updatedAt: '',
};

@Component({
  standalone: true,
  imports: [PlatformCardComponent],
  template: `<app-platform-card [platform]="platform()" (connect)="connected = true" (disconnect)="disconnected = true" (testPost)="tested = true" />`,
})
class TestHostComponent {
  platform = signal<Platform>(connectedPlatform);
  connected = false;
  disconnected = false;
  tested = false;
}

describe('PlatformCardComponent', () => {
  let host: TestHostComponent;
  let fixture: ComponentFixture<TestHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHostComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(TestHostComponent);
    host = fixture.componentInstance;
  });

  it('should render card with platform name', () => {
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('LinkedIn');
  });

  it('should show connected status with green dot', () => {
    fixture.detectChanges();
    const dot = fixture.nativeElement.querySelector('.status-dot');
    expect(dot.classList).toContain('connected');
    expect(fixture.nativeElement.textContent).toContain('Connected');
  });

  it('should show disconnected status without green dot', () => {
    host.platform.set(disconnectedPlatform);
    fixture.detectChanges();
    const dot = fixture.nativeElement.querySelector('.status-dot');
    expect(dot.classList).not.toContain('connected');
    expect(fixture.nativeElement.textContent).toContain('Disconnected');
  });

  it('should show last sync as relative time when connected', () => {
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('1h ago');
  });

  it('should render scope chips when connected with scopes', () => {
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('p-chip');
    expect(chips.length).toBe(2);
  });

  it('should show Connect button when disconnected', () => {
    host.platform.set(disconnectedPlatform);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('p-button');
    const labels = Array.from(buttons).map((b: any) => b.getAttribute('label'));
    expect(labels).toContain('Connect');
    expect(labels).not.toContain('Disconnect');
  });

  it('should show Disconnect button when connected', () => {
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('p-button');
    const labels = Array.from(buttons).map((b: any) => b.getAttribute('label'));
    expect(labels).toContain('Disconnect');
    expect(labels).not.toContain('Connect');
  });

  it('should emit connect when Connect clicked', () => {
    host.platform.set(disconnectedPlatform);
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('p-button[label="Connect"]');
    btn.querySelector('button')?.click();
    expect(host.connected).toBe(true);
  });

  it('should emit disconnect when Disconnect clicked', () => {
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('p-button[label="Disconnect"]');
    btn.querySelector('button')?.click();
    expect(host.disconnected).toBe(true);
  });
});
