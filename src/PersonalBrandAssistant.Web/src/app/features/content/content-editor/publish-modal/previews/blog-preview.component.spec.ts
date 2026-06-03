import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BlogPreviewComponent } from './blog-preview.component';
import { ProseBlock } from '../markdown-blocks';

describe('BlogPreviewComponent', () => {
  let fixture: ComponentFixture<BlogPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BlogPreviewComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(BlogPreviewComponent);
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders the title and a block of body text', () => {
    const blocks: ProseBlock[] = [
      { type: 'h2', text: 'A Subhead' },
      { type: 'p', text: 'Body paragraph content.' },
    ];
    fixture.componentRef.setInput('blocks', blocks);
    fixture.componentRef.setInput('title', 'My Article Title');
    fixture.detectChanges();

    expect(text()).toContain('My Article Title');
    expect(text()).toContain('A Subhead');
    expect(text()).toContain('Body paragraph content.');
  });

  it('hides the lede when subtitle is empty', () => {
    fixture.componentRef.setInput('blocks', []);
    fixture.componentRef.setInput('title', 'Title');
    fixture.componentRef.setInput('subtitle', '');
    fixture.detectChanges();

    const lede = (fixture.nativeElement as HTMLElement).querySelector('.lede');
    expect(lede).toBeNull();
  });

  it('shows the lede when subtitle is present', () => {
    fixture.componentRef.setInput('blocks', []);
    fixture.componentRef.setInput('subtitle', 'A derived subtitle');
    fixture.detectChanges();

    const lede = (fixture.nativeElement as HTMLElement).querySelector('.lede');
    expect(lede?.textContent).toContain('A derived subtitle');
  });
});
