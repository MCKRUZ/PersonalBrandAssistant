import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PublishResultComponent, ResultRow } from './publish-result.component';
import { Platform } from '../../models/content.model';

describe('PublishResultComponent', () => {
  let fixture: ComponentFixture<PublishResultComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PublishResultComponent] }).compileComponents();
    fixture = TestBed.createComponent(PublishResultComponent);
  });

  function setRows(rows: ResultRow[]): HTMLElement {
    fixture.componentRef.setInput('rows', rows);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders a published auto row with a View link', () => {
    const el = setRows([
      { platform: Platform.LinkedIn, code: 'Li', label: 'LinkedIn', mode: 'auto', state: 'published', url: 'https://x' },
    ]);
    expect(el.textContent).toContain('Published');
    expect((el.querySelector('a') as HTMLAnchorElement).href).toContain('https://x');
  });

  it('renders a manual row with Copy and Open', () => {
    const el = setRows([
      { platform: Platform.Medium, code: 'Me', label: 'Medium', mode: 'manual', state: 'ready', copyText: 'hi', openUrl: 'https://medium.com/new-story' },
    ]);
    expect(el.textContent).toContain('Ready to post');
    expect(el.textContent).toContain('Copy text');
    expect(el.textContent).toContain('Open Medium');
  });

  it('copies the platform text to the clipboard', () => {
    const writeText = jasmine.createSpy('writeText').and.resolveTo();
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    setRows([
      { platform: Platform.Medium, code: 'Me', label: 'Medium', mode: 'manual', state: 'ready', copyText: 'formatted body' },
    ]);
    (fixture.nativeElement.querySelector('.link') as HTMLButtonElement).click();
    expect(writeText).toHaveBeenCalledWith('formatted body');
  });

  it('renders a scheduled row with the datetime', () => {
    const el = setRows([
      { platform: Platform.Blog, code: 'Bl', label: 'Blog', mode: 'scheduled', state: 'scheduled', scheduledAt: '2026-07-01 09:00' },
    ]);
    expect(el.textContent).toContain('Scheduled for');
    expect(el.textContent).toContain('2026-07-01 09:00');
  });

  it('emits retry on a failed row', () => {
    const spy = jasmine.createSpy('retry');
    fixture.componentInstance.retry.subscribe(spy);
    const el = setRows([
      { platform: Platform.Twitter, code: 'Tw', label: 'Twitter', mode: 'auto', state: 'failed' },
    ]);
    (el.querySelector('.link') as HTMLButtonElement).click();
    expect(spy).toHaveBeenCalledWith(Platform.Twitter);
  });
});
