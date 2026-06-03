import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ContentListTableComponent } from './content-list-table.component';
import { ContentStatus, ContentType, Platform } from '../../models/content.model';
import type { Content } from '../../models/content.model';

function makeContent(over: Partial<Content> = {}): Content {
  return {
    id: 'c1',
    title: 'Hello world',
    contentType: ContentType.BlogPost,
    status: ContentStatus.Draft,
    primaryPlatform: Platform.Blog,
    targetPlatforms: [Platform.Blog, Platform.LinkedIn],
    voiceScore: 82,
    tags: ['angular'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    scheduledAt: null,
    publishedAt: null,
    platformPublishes: [],
    ...over,
  };
}

describe('ContentListTableComponent', () => {
  let fixture: ComponentFixture<ContentListTableComponent>;
  let component: ContentListTableComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [ContentListTableComponent] });
    fixture = TestBed.createComponent(ContentListTableComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('contents', [makeContent()]);
    fixture.detectChanges();
  });

  it('renders Status/Title/Type/Platforms/Voice/Updated headers and no Actions column', () => {
    const header = (fixture.nativeElement as HTMLElement).querySelector('.list-header')!;
    const text = header.textContent ?? '';
    expect(text).toContain('Status');
    expect(text).toContain('Title');
    expect(text).toContain('Type');
    expect(text).toContain('Platforms');
    expect(text).toContain('Voice');
    expect(text).toContain('Updated');
    expect(text).not.toContain('Actions');
  });

  it('renders status tag, voice ring and platform dots per row', () => {
    const row = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="content-row"]')!;
    expect(row.querySelector('app-status-tag')).toBeTruthy();
    expect(row.querySelector('app-voice-score-ring')).toBeTruthy();
    expect(row.querySelectorAll('app-platform-dot').length).toBe(2);
  });

  it('row click emits the content id (opens the drawer)', () => {
    spyOn(component.openRow, 'emit');
    const row = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="content-row"]'
    ) as HTMLElement;
    row.click();
    expect(component.openRow.emit).toHaveBeenCalledWith('c1');
  });

  it('shows empty state when there are no rows', () => {
    fixture.componentRef.setInput('contents', []);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="empty-state"]')).toBeTruthy();
  });
});
