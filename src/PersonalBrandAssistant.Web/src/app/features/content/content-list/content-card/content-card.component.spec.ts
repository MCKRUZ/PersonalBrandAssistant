import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentCardComponent } from './content-card.component';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
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
    voiceScore: 85,
    tags: ['angular', 'typescript', 'ai', 'extra'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
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
    const type = fixture.nativeElement.querySelector('.content-type');
    expect(type.textContent).toContain('Blog Post');
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
});
