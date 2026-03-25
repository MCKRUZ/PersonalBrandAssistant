import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SubstackSectionComponent } from './substack-section.component';
import { SubstackPost } from '../models/dashboard.model';

describe('SubstackSectionComponent', () => {
  let component: SubstackSectionComponent;
  let fixture: ComponentFixture<SubstackSectionComponent>;

  const mockPosts: readonly SubstackPost[] = [
    { title: 'The Future of AI Agents', url: 'https://matthewkruczek.substack.com/p/future-ai-agents', publishedAt: '2026-03-18T10:00:00Z', summary: 'This is a post about AI agents.' },
    { title: 'Building with Claude Code', url: 'https://matthewkruczek.substack.com/p/claude-code', publishedAt: '2026-03-10T14:00:00Z', summary: 'Tips and tricks for Claude Code workflows.' },
    { title: 'Enterprise AI Adoption', url: 'https://matthewkruczek.substack.com/p/enterprise-ai', publishedAt: '2026-03-01T09:00:00Z', summary: null },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SubstackSectionComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SubstackSectionComponent);
    component = fixture.componentInstance;
  });

  it('should render post list from RSS data', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    const entries = fixture.nativeElement.querySelectorAll('.substack-post');
    expect(entries.length).toBe(3);
    expect(fixture.nativeElement.textContent).toContain('The Future of AI Agents');
    expect(fixture.nativeElement.textContent).toContain('Building with Claude Code');
    expect(fixture.nativeElement.textContent).toContain('Enterprise AI Adoption');
  });

  it('should render post titles as clickable links with target _blank', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    const links = fixture.nativeElement.querySelectorAll('.substack-post a');
    expect(links.length).toBe(3);
    expect(links[0].getAttribute('href')).toBe('https://matthewkruczek.substack.com/p/future-ai-agents');
    expect(links[0].getAttribute('target')).toBe('_blank');
    expect(links[0].getAttribute('rel')).toContain('noopener');
  });

  it('should format publish dates', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Mar');
    expect(el.textContent).toContain('2026');
  });

  it('should display summary when present', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('This is a post about AI agents.');
  });

  it('should handle null summary gracefully', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    const entries = fixture.nativeElement.querySelectorAll('.substack-post');
    const lastEntry = entries[2] as HTMLElement;
    expect(lastEntry.querySelector('.post-summary')).toBeNull();
    expect(lastEntry.textContent).not.toContain('null');
  });

  it('should show empty state when no posts', () => {
    fixture.componentRef.setInput('posts', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No Substack posts found');
  });

  it('should include Substack branding', () => {
    fixture.componentRef.setInput('posts', mockPosts);
    fixture.detectChanges();

    const header = fixture.nativeElement.querySelector('.section-header') as HTMLElement;
    expect(header.textContent).toContain('Substack');
    expect(header.querySelector('.pi-at')).toBeTruthy();
  });
});
