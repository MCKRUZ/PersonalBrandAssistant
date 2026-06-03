import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { PublishModalComponent } from './publish-modal.component';
import { ContentService } from '../../services/content.service';
import {
  ContentStatus,
  ContentType,
  Platform,
  PUBLISHABLE_PLATFORMS,
} from '../../models/content.model';
import type { ContentDetail, PlatformConnectionStatus } from '../../models/content.model';

function detail(over: Partial<ContentDetail> = {}): ContentDetail {
  return {
    id: 'c1',
    title: 'A Post',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Approved,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 70,
    tags: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    body: '# Title\n\nA paragraph of body text.',
    viralityPrediction: null,
    sourceIdeaId: null,
    parentContentId: null,
    children: [],
    ...over,
  } as ContentDetail;
}

const allConnected: PlatformConnectionStatus[] = PUBLISHABLE_PLATFORMS.map((platform) => ({
  platform,
  isConnected: platform !== Platform.Twitter, // Twitter disconnected to exercise the warn badge
  isExpiring: false,
  expiresAt: null,
  capabilities: {
    maxCharacters: 0, supportsMarkdown: true, supportsHtml: true,
    supportsImages: true, supportsScheduling: true, supportsThreads: true,
  },
}));

describe('PublishModalComponent', () => {
  let fixture: ComponentFixture<PublishModalComponent>;
  let svc: jasmine.SpyObj<ContentService>;

  function setup(mode: 'publish' | 'schedule' = 'publish', content = detail()): void {
    fixture.componentRef.setInput('visible', true);
    fixture.componentRef.setInput('content', content);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('mode', mode);
    fixture.detectChanges();
  }

  beforeEach(async () => {
    svc = jasmine.createSpyObj('ContentService', ['getPublishStatus', 'retryPlatform']);
    svc.getPublishStatus.and.returnValue(of({ contentId: 'c1', primaryPlatform: Platform.Blog, platformStatuses: [] }));
    svc.retryPlatform.and.returnValue(of(void 0));

    await TestBed.configureTestingModule({
      imports: [PublishModalComponent],
      providers: [{ provide: ContentService, useValue: svc }],
    }).compileComponents();
    fixture = TestBed.createComponent(PublishModalComponent);
  });

  it('renders one destination row per publishable platform; primary checked+disabled', () => {
    setup();
    const rows = fixture.nativeElement.querySelectorAll('.dest');
    expect(rows.length).toBe(PUBLISHABLE_PLATFORMS.length);
    const primary = fixture.nativeElement.querySelector('[data-platform="Blog"] input') as HTMLInputElement;
    expect(primary.checked).toBeTrue();
    expect(primary.disabled).toBeTrue();
  });

  it('shows a delivery badge and usage per row', () => {
    setup();
    expect(fixture.nativeElement.querySelectorAll('app-delivery-badge').length).toBe(PUBLISHABLE_PLATFORMS.length);
    const li = fixture.nativeElement.querySelector('[data-platform="LinkedIn"]') as HTMLElement;
    expect(li.textContent).toMatch(/\d+\/3000/);
    const tw = fixture.nativeElement.querySelector('[data-platform="Twitter"]') as HTMLElement;
    expect(tw.textContent).toContain('tweets');
  });

  it('selecting a destination updates the footer summary and adds a preview tab', () => {
    setup();
    fixture.componentInstance.toggle(Platform.Medium);
    fixture.detectChanges();
    const summary = fixture.nativeElement.querySelector('.pub-summary') as HTMLElement;
    expect(summary.textContent).toContain('2 destinations');
    expect(fixture.nativeElement.querySelectorAll('.pub-tab').length).toBe(2);
  });

  it('a11y: role=dialog, aria-modal, cdkTrapFocus; Escape cancels', () => {
    setup();
    const dialog = fixture.nativeElement.querySelector('.pub');
    expect(dialog.getAttribute('role')).toBe('dialog');
    expect(dialog.getAttribute('aria-modal')).toBe('true');
    expect(dialog.hasAttribute('cdktrapfocus')).toBeTrue();

    const cancelSpy = jasmine.createSpy('cancel');
    fixture.componentInstance.cancel.subscribe(cancelSpy);
    fixture.componentInstance.onEscape();
    expect(cancelSpy).toHaveBeenCalled();
  });

  it('now-mode confirm emits the selected platforms and swaps in the result view', () => {
    setup('publish');
    fixture.componentInstance.toggle(Platform.LinkedIn);
    const confirmSpy = jasmine.createSpy('confirm');
    fixture.componentInstance.confirm.subscribe(confirmSpy);

    fixture.componentInstance.onConfirm();
    fixture.detectChanges();

    expect(confirmSpy).toHaveBeenCalledWith(jasmine.objectContaining({
      platforms: jasmine.arrayContaining([Platform.Blog, Platform.LinkedIn]),
    }));
    expect(confirmSpy.calls.mostRecent().args[0].scheduledAt).toBeUndefined();
    expect(fixture.nativeElement.querySelector('app-publish-result')).toBeTruthy();
  });

  it('schedule mode disables destination toggles and requires a datetime', () => {
    setup('schedule');
    const confirmBtn = fixture.nativeElement.querySelector('[data-testid="confirm-btn"]') as HTMLButtonElement;
    expect(confirmBtn.disabled).toBeTrue();
    const medium = fixture.nativeElement.querySelector('[data-platform="Medium"] input') as HTMLInputElement;
    expect(medium.disabled).toBeTrue();

    fixture.componentInstance.scheduledAt.set('2026-07-01T09:00');
    fixture.detectChanges();
    expect(confirmBtn.disabled).toBeFalse();

    const confirmSpy = jasmine.createSpy('confirm');
    fixture.componentInstance.confirm.subscribe(confirmSpy);
    fixture.componentInstance.onConfirm();
    expect(confirmSpy.calls.mostRecent().args[0].scheduledAt).toBeTruthy();
  });
});
