import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SourceCardComponent } from './source-card.component';
import { IdeaSource, IdeaSourceType } from '../../../../../models/idea.model';

describe('SourceCardComponent', () => {
  let fixture: ComponentFixture<SourceCardComponent>;
  let component: SourceCardComponent;

  const mockSource: IdeaSource = {
    id: 'src-1',
    name: 'Tech Blog RSS',
    type: IdeaSourceType.RSS,
    feedUrl: 'https://example.com/rss',
    apiUrl: null,
    category: 'Technology',
    pollIntervalMinutes: 30,
    isEnabled: true,
    lastPolledAt: '2026-01-01T12:00:00Z',
    lastSuccessAt: '2026-01-01T12:00:00Z',
    lastError: null,
    consecutiveFailures: 0,
    ideaCount: 42,
    isHealthy: true,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SourceCardComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SourceCardComponent);
    component = fixture.componentInstance;
  });

  it('should display source name', () => {
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const name = fixture.nativeElement.querySelector('.source-name') as HTMLElement;
    expect(name.textContent?.trim()).toBe('Tech Blog RSS');
  });

  it('should display idea count', () => {
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const count = fixture.nativeElement.querySelector('.idea-count') as HTMLElement;
    expect(count.textContent?.trim()).toBe('42 ideas');
  });

  it('should show green health dot when healthy', () => {
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    expect(component.healthColor()).toBe('green');
  });

  it('should show yellow health dot with 1-2 failures', () => {
    fixture.componentRef.setInput('source', { ...mockSource, consecutiveFailures: 2 });
    fixture.detectChanges();
    expect(component.healthColor()).toBe('yellow');
  });

  it('should show red health dot with 3+ failures', () => {
    fixture.componentRef.setInput('source', { ...mockSource, consecutiveFailures: 5 });
    fixture.detectChanges();
    expect(component.healthColor()).toBe('red');
  });

  it('should show red health dot when disabled', () => {
    fixture.componentRef.setInput('source', { ...mockSource, isEnabled: false });
    fixture.detectChanges();
    expect(component.healthColor()).toBe('red');
  });

  it('should emit edit event', () => {
    spyOn(component.edit, 'emit');
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="edit-btn"] button') as HTMLElement;
    btn.click();
    expect(component.edit.emit).toHaveBeenCalled();
  });

  it('should emit delete event', () => {
    spyOn(component.delete, 'emit');
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="delete-btn"] button') as HTMLElement;
    btn.click();
    expect(component.delete.emit).toHaveBeenCalled();
  });

  it('should emit toggleEnabled event', () => {
    spyOn(component.toggleEnabled, 'emit');
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="toggle-btn"] button') as HTMLElement;
    btn.click();
    expect(component.toggleEnabled.emit).toHaveBeenCalled();
  });

  it('should display feed URL when present', () => {
    fixture.componentRef.setInput('source', mockSource);
    fixture.detectChanges();
    const url = fixture.nativeElement.querySelector('.feed-url') as HTMLElement;
    expect(url.textContent?.trim()).toBe('https://example.com/rss');
  });

  it('should show error text when lastError exists', () => {
    fixture.componentRef.setInput('source', { ...mockSource, lastError: 'Connection timeout' });
    fixture.detectChanges();
    const err = fixture.nativeElement.querySelector('[data-testid="source-error"]') as HTMLElement;
    expect(err.textContent?.trim()).toBe('Connection timeout');
  });
});
