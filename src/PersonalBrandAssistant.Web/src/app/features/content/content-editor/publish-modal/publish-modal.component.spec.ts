import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PublishModalComponent } from './publish-modal.component';
import {
  Platform,
  ContentStatus,
  ContentType,
  PublishStatus,
} from '../../models/content.model';
import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';

function makeDetail(overrides: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'c-1',
    title: 'Test Post',
    body: 'Hello world content here.',
    status: ContentStatus.Approved,
    contentType: ContentType.BlogPost,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog, Platform.LinkedIn],
    voiceScore: null,
    tags: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    children: [],
    ...overrides,
  };
}

function makeConnection(platform: Platform, connected = true): PlatformConnectionStatus {
  return {
    platform,
    isConnected: connected,
    isExpiring: false,
    expiresAt: null,
    capabilities: {
      maxCharacters: 0,
      supportsMarkdown: true,
      supportsHtml: false,
      supportsImages: true,
      supportsScheduling: true,
      supportsThreads: false,
    },
  };
}

describe('PublishModalComponent', () => {
  let component: PublishModalComponent;
  let fixture: ComponentFixture<PublishModalComponent>;

  const connections = [
    makeConnection(Platform.Blog),
    makeConnection(Platform.LinkedIn),
    makeConnection(Platform.Medium, false),
    makeConnection(Platform.Twitter),
    makeConnection(Platform.Substack),
  ];

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PublishModalComponent],
    });
    fixture = TestBed.createComponent(PublishModalComponent);
    component = fixture.componentInstance;
  });

  function setupModal(overrides: Partial<ContentDetail> = {}) {
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('content', makeDetail(overrides));
    fixture.componentRef.setInput('connectedPlatforms', connections);
    fixture.componentRef.setInput('mode', 'publish');
    fixture.detectChanges();
  }

  it('should show primary platform prominently at top', () => {
    setupModal();
    const primary = fixture.nativeElement.querySelector('[data-testid="primary-platform"]');
    expect(primary).toBeTruthy();
    expect(primary.textContent).toContain('Blog');
  });

  it('should show connection status per platform', () => {
    setupModal();
    const badges = fixture.nativeElement.querySelectorAll('.connection-status');
    expect(badges.length).toBeGreaterThan(0);
  });

  it('should allow toggling secondary platforms', () => {
    setupModal({ targetPlatforms: [Platform.Blog, Platform.LinkedIn, Platform.Twitter] });
    const twitterCheckbox = fixture.nativeElement.querySelector('[data-platform="Twitter"] input') as HTMLInputElement;
    expect(twitterCheckbox).toBeTruthy();
    expect(twitterCheckbox.disabled).toBeFalse();
  });

  it('should not allow deselecting the primary platform', () => {
    setupModal();
    const primaryCheckbox = fixture.nativeElement.querySelector('[data-testid="primary-platform"] input') as HTMLInputElement;
    expect(primaryCheckbox.disabled).toBeTrue();
    expect(primaryCheckbox.checked).toBeTrue();
  });

  it('should emit confirm with selected platforms', () => {
    setupModal({ targetPlatforms: [Platform.Blog, Platform.LinkedIn] });
    let emitted: { platforms: Platform[]; scheduledAt?: string } | undefined;
    component.confirm.subscribe((v: { platforms: Platform[]; scheduledAt?: string }) => (emitted = v));

    const confirmBtn = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]') as HTMLButtonElement;
    confirmBtn.click();

    expect(emitted).toBeTruthy();
    expect(emitted!.platforms).toContain(Platform.Blog);
    expect(emitted!.platforms).toContain(Platform.LinkedIn);
  });

  it('should keep confirm button enabled when only primary platform is selected', () => {
    setupModal({ targetPlatforms: [] });
    fixture.detectChanges();
    const confirmBtn = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]') as HTMLButtonElement;
    expect(confirmBtn.disabled).toBeFalse();
  });

  it('should emit cancel when cancel button clicked', () => {
    setupModal();
    let cancelled = false;
    component.cancel.subscribe(() => (cancelled = true));

    const cancelBtn = fixture.nativeElement.querySelector('[data-testid="cancel-btn"]') as HTMLButtonElement;
    cancelBtn.click();

    expect(cancelled).toBeTrue();
  });

  it('should show Schedule header when mode is schedule', () => {
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('content', makeDetail());
    fixture.componentRef.setInput('connectedPlatforms', connections);
    fixture.componentRef.setInput('mode', 'schedule');
    fixture.detectChanges();

    const header = fixture.nativeElement.querySelector('.modal-header');
    expect(header?.textContent).toContain('Schedule');
  });
});
