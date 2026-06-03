import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentCardComponent } from './content-card.component';
import { ContentStatus, ContentType, Platform, PublishStatus } from '../../models/content.model';
import type { Content } from '../../models/content.model';

describe('ContentCardComponent', () => {
  let component: ContentCardComponent;
  let fixture: ComponentFixture<ContentCardComponent>;

  const mockContent: Content = {
    id: 'content-1',
    title: 'Test Content Title',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog],
    voiceScore: 85,
    tags: ['angular', 'typescript', 'ai', 'extra'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ContentCardComponent],
    });
    fixture = TestBed.createComponent(ContentCardComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('content', mockContent);
    fixture.detectChanges();
  });

  it('should render title', () => {
    const title = fixture.nativeElement.querySelector('.card-title');
    expect(title.textContent).toContain('Test Content Title');
  });

  it('should render status badge with correct data-status attribute', () => {
    const badge = fixture.nativeElement.querySelector('.status-badge');
    expect(badge.getAttribute('data-status')).toBe('Draft');
  });

  it('should render platform icon', () => {
    const icon = fixture.nativeElement.querySelector('.platform-icon');
    expect(icon).toBeTruthy();
    expect(icon.getAttribute('data-platform')).toBe('Blog');
  });

  it('should render content type label', () => {
    // ContentType.BlogPost === 'Blog' (backend enum value), so formatContentType yields 'Blog'.
    const type = fixture.nativeElement.querySelector('.content-type');
    expect(type.textContent).toContain('Blog');
  });

  it('should render voice score dot with correct color class', () => {
    const dot = fixture.nativeElement.querySelector('.voice-dot');
    expect(dot.classList.contains('voice-green')).toBeTrue();

    fixture.componentRef.setInput('content', { ...mockContent, voiceScore: 70 });
    fixture.detectChanges();
    const dotAmber = fixture.nativeElement.querySelector('.voice-dot');
    expect(dotAmber.classList.contains('voice-amber')).toBeTrue();

    fixture.componentRef.setInput('content', { ...mockContent, voiceScore: 50 });
    fixture.detectChanges();
    const dotRed = fixture.nativeElement.querySelector('.voice-dot');
    expect(dotRed.classList.contains('voice-red')).toBeTrue();
  });

  it('should display max 3 tags and show +N more', () => {
    const tags = fixture.nativeElement.querySelectorAll('p-tag');
    expect(tags.length).toBe(3);
    const more = fixture.nativeElement.querySelector('.more-tags');
    expect(more.textContent).toContain('+1');
  });

  it('should truncate long titles', () => {
    const longTitle = 'A'.repeat(200);
    fixture.componentRef.setInput('content', { ...mockContent, title: longTitle });
    fixture.detectChanges();
    const title = fixture.nativeElement.querySelector('.card-title');
    expect(title.textContent.length).toBeLessThan(200);
    expect(title.textContent).toContain('...');
  });

  it('should emit edit event on edit button click', () => {
    spyOn(component.edit, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="edit-btn"] button');
    btn.click();
    expect(component.edit.emit).toHaveBeenCalledWith('content-1');
  });

  it('should emit delete event on delete button click', () => {
    spyOn(component.onDelete, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="delete-btn"] button');
    btn.click();
    expect(component.onDelete.emit).toHaveBeenCalledWith('content-1');
  });

  it('should emit duplicate event on duplicate button click', () => {
    spyOn(component.duplicate, 'emit');
    const btn = fixture.nativeElement.querySelector('[data-testid="duplicate-btn"] button');
    btn.click();
    expect(component.duplicate.emit).toHaveBeenCalledWith('content-1');
  });

  it('should show green badge for Published platform status', () => {
    fixture.componentRef.setInput('content', {
      ...mockContent,
      platformPublishes: [
        { platform: Platform.Blog, publishStatus: PublishStatus.Published, publishedUrl: 'https://example.com/1' },
      ],
    });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-status="Published"]');
    expect(badge).toBeTruthy();
  });

  it('should show red badge with retry button for Failed platform status', () => {
    fixture.componentRef.setInput('content', {
      ...mockContent,
      platformPublishes: [
        { platform: Platform.LinkedIn, publishStatus: PublishStatus.Failed, publishedUrl: null },
      ],
    });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-status="Failed"]');
    expect(badge).toBeTruthy();
    const retryBtn = fixture.nativeElement.querySelector('[data-testid="retry-btn"]');
    expect(retryBtn).toBeTruthy();
  });

  it('should show pending badge for Pending platform status', () => {
    fixture.componentRef.setInput('content', {
      ...mockContent,
      platformPublishes: [
        { platform: Platform.Blog, publishStatus: PublishStatus.Pending, publishedUrl: null },
      ],
    });
    fixture.detectChanges();
    const badge = fixture.nativeElement.querySelector('[data-status="Pending"]');
    expect(badge).toBeTruthy();
  });

  it('should not show badges when platformPublishes is empty', () => {
    fixture.componentRef.setInput('content', { ...mockContent, platformPublishes: [] });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="publish-badges"]')).toBeNull();
  });

  it('should emit retry event when retry button clicked', () => {
    fixture.componentRef.setInput('content', {
      ...mockContent,
      platformPublishes: [
        { platform: Platform.LinkedIn, publishStatus: PublishStatus.Failed, publishedUrl: null },
      ],
    });
    fixture.detectChanges();

    let emitted: Platform | undefined;
    component.retry.subscribe((v: Platform) => (emitted = v));

    const retryBtn = fixture.nativeElement.querySelector('[data-testid="retry-btn"]');
    retryBtn.click();

    expect(emitted).toBe(Platform.LinkedIn);
  });

  describe('board variant', () => {
    beforeEach(() => {
      fixture.componentRef.setInput('variant', 'board');
      fixture.detectChanges();
    });

    it('shows type glyph + uppercase type label + voice ring + title + tag chips + platform dots', () => {
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.glyph')?.textContent).toContain('¶'); // BlogPost glyph
      expect(el.querySelector('.type-label')?.textContent).toContain('Blog');
      expect(el.querySelector('app-voice-score-ring')).toBeTruthy();
      expect(el.querySelector('.board-title')?.textContent).toContain('Test Content Title');
      expect(el.querySelectorAll('.tag-chip').length).toBe(4); // 3 chips + "+1"
      expect(el.querySelector('app-platform-dot')).toBeTruthy();
    });

    it('shows relativeTime of updatedAt by default', () => {
      const when = (fixture.nativeElement as HTMLElement).querySelector('.when');
      expect(when?.textContent?.trim().length).toBeGreaterThan(0);
    });

    it('scheduled card shows "in {n}{unit}" from scheduledAt', () => {
      const future = new Date(Date.now() + 3 * 86400_000).toISOString();
      fixture.componentRef.setInput('content', {
        ...mockContent,
        status: ContentStatus.Scheduled,
        scheduledAt: future,
      });
      fixture.detectChanges();
      const when = (fixture.nativeElement as HTMLElement).querySelector('.when');
      expect(when?.textContent).toContain('in ');
      expect(when?.textContent).toContain('d');
    });

    it('does not render list-variant action buttons', () => {
      expect(fixture.nativeElement.querySelector('[data-testid="edit-btn"]')).toBeNull();
    });
  });
});
