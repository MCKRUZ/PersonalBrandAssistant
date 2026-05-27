import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlatformTargetsComponent } from './platform-targets.component';
import { Platform, PUBLISHABLE_PLATFORMS, PLATFORM_CHAR_LIMITS } from '../../models/content.model';
import type { PlatformConnectionStatus } from '../../models/content.model';

function makeConnection(platform: Platform, isConnected = true): PlatformConnectionStatus {
  return {
    platform,
    isConnected,
    isExpiring: false,
    expiresAt: null,
    capabilities: {
      maxCharacters: PLATFORM_CHAR_LIMITS[platform] ?? 0,
      supportsMarkdown: true,
      supportsHtml: false,
      supportsImages: true,
      supportsScheduling: true,
      supportsThreads: false,
    },
  };
}

describe('PlatformTargetsComponent', () => {
  let component: PlatformTargetsComponent;
  let fixture: ComponentFixture<PlatformTargetsComponent>;

  const allConnected = PUBLISHABLE_PLATFORMS.map((p) => makeConnection(p));

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlatformTargetsComponent],
    });
    fixture = TestBed.createComponent(PlatformTargetsComponent);
    component = fixture.componentInstance;
  });

  it('should show checkboxes for all publishable platforms', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    const checkboxes = fixture.nativeElement.querySelectorAll('.platform-checkbox');
    expect(checkboxes.length).toBe(PUBLISHABLE_PLATFORMS.length);
  });

  it('should disable checkbox for platforms that are not connected', () => {
    const connections = [
      makeConnection(Platform.Blog),
      makeConnection(Platform.Medium, false),
      makeConnection(Platform.Substack),
      makeConnection(Platform.LinkedIn),
      makeConnection(Platform.Twitter),
    ];
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', connections);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    const mediumCheckbox = fixture.nativeElement.querySelector('[data-platform="Medium"] input') as HTMLInputElement;
    expect(mediumCheckbox.disabled).toBeTrue();
  });

  it('should pre-select platforms from selectedPlatforms', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.LinkedIn]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
    expect(linkedInCb.checked).toBeTrue();
  });

  it('should emit targetPlatformsChange when a platform is toggled on', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    let emitted: Platform[] | undefined;
    component.targetPlatformsChange.subscribe((v: Platform[]) => (emitted = v));

    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
    linkedInCb.click();
    fixture.detectChanges();

    expect(emitted).toEqual([Platform.Blog, Platform.LinkedIn]);
  });

  it('should emit targetPlatformsChange when a platform is toggled off', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.LinkedIn]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    let emitted: Platform[] | undefined;
    component.targetPlatformsChange.subscribe((v: Platform[]) => (emitted = v));

    const linkedInCb = fixture.nativeElement.querySelector('[data-platform="LinkedIn"] input') as HTMLInputElement;
    linkedInCb.click();
    fixture.detectChanges();

    expect(emitted).toEqual([Platform.Blog]);
  });

  it('should always show primaryPlatform as checked and disabled', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 100);
    fixture.componentRef.setInput('wordCount', 20);
    fixture.detectChanges();

    const blogCb = fixture.nativeElement.querySelector('[data-platform="Blog"] input') as HTMLInputElement;
    expect(blogCb.checked).toBeTrue();
    expect(blogCb.disabled).toBeTrue();
  });

  it('should show character count for Twitter', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.Twitter]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 200);
    fixture.componentRef.setInput('wordCount', 30);
    fixture.detectChanges();

    const twitterCount = fixture.nativeElement.querySelector('[data-platform="Twitter"] .char-count');
    expect(twitterCount?.textContent).toContain('200/280');
  });

  it('should highlight platforms exceeding character limit', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog, Platform.Twitter]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 300);
    fixture.componentRef.setInput('wordCount', 50);
    fixture.detectChanges();

    const twitterCount = fixture.nativeElement.querySelector('[data-platform="Twitter"] .char-count');
    expect(twitterCount?.classList.contains('over-limit')).toBeTrue();
  });

  it('should show word count for platforms without character limits', () => {
    fixture.componentRef.setInput('selectedPlatforms', [Platform.Blog]);
    fixture.componentRef.setInput('primaryPlatform', Platform.Blog);
    fixture.componentRef.setInput('connectedPlatforms', allConnected);
    fixture.componentRef.setInput('bodyLength', 500);
    fixture.componentRef.setInput('wordCount', 80);
    fixture.detectChanges();

    const blogCount = fixture.nativeElement.querySelector('[data-platform="Blog"] .word-count');
    expect(blogCount?.textContent).toContain('80 words');
  });
});
