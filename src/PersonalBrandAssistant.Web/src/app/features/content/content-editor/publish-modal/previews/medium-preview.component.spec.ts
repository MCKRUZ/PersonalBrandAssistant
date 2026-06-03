import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MediumPreviewComponent } from './medium-preview.component';
import { ProseBlock } from '../markdown-blocks';

describe('MediumPreviewComponent', () => {
  let fixture: ComponentFixture<MediumPreviewComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MediumPreviewComponent],
    }).compileComponents();
    fixture = TestBed.createComponent(MediumPreviewComponent);
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  it('renders the title, the Follow pill, and body text', () => {
    const blocks: ProseBlock[] = [{ type: 'p', text: 'Medium body paragraph.' }];
    fixture.componentRef.setInput('blocks', blocks);
    fixture.componentRef.setInput('title', 'Medium Headline');
    fixture.detectChanges();

    expect(text()).toContain('Medium Headline');
    expect(text()).toContain('Follow');
    expect(text()).toContain('Medium body paragraph.');
  });
});
