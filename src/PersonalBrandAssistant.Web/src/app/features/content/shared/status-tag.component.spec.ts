import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatusTagComponent } from './status-tag.component';
import { ContentStatus } from '../models/content.model';
import { STATUS_META } from '../content-list/content-display.utils';

describe('StatusTagComponent', () => {
  let fixture: ComponentFixture<StatusTagComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatusTagComponent] }).compileComponents();
    fixture = TestBed.createComponent(StatusTagComponent);
  });

  it('renders the label and a dot for each status', () => {
    for (const status of Object.values(ContentStatus)) {
      fixture.componentInstance.status = status;
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain(STATUS_META[status].label);
      expect((fixture.nativeElement as HTMLElement).querySelector('.dot')).toBeTruthy();
    }
  });

  it('colors the tag with the status color', () => {
    fixture.componentInstance.status = ContentStatus.Review;
    fixture.detectChanges();
    const tag = (fixture.nativeElement as HTMLElement).querySelector('.status-tag') as HTMLElement;
    // STATUS_META color is a css var; the binding sets it as inline style
    expect(tag.style.color).toContain('--status-review');
  });
});
