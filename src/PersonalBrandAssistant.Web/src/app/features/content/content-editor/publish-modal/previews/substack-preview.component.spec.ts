import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SubstackPreviewComponent } from './substack-preview.component';
import { ProseBlock } from '../markdown-blocks';

describe('SubstackPreviewComponent', () => {
  let fixture: ComponentFixture<SubstackPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SubstackPreviewComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(SubstackPreviewComponent);
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders Subscribe, the title, and a subscriber count line', () => {
    const blocks: ProseBlock[] = [{ type: 'p', text: 'Newsletter body.' }];
    fixture.componentRef.setInput('blocks', blocks);
    fixture.componentRef.setInput('title', 'Newsletter Title');
    fixture.detectChanges();

    expect(text()).toContain('Subscribe');
    expect(text()).toContain('Newsletter Title');
    expect(text()).toContain('subscribers');
  });
});
