import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { SmartSuggestionsComponent } from './smart-suggestions.component';
import { IdeaStore } from '../../store/idea.store';
import { IdeaConnection, IdeaStatus } from '../../../../models/idea.model';

describe('SmartSuggestionsComponent', () => {
  let fixture: ComponentFixture<SmartSuggestionsComponent>;
  let component: SmartSuggestionsComponent;
  let httpMock: HttpTestingController;
  let store: InstanceType<typeof IdeaStore>;

  const mockConnections: IdeaConnection[] = [
    {
      theme: 'AI Governance',
      relatedIdeaIds: ['idea-1', 'idea-2'],
      suggestedAngle: 'Compare frameworks across industries',
      confidence: 0.85,
    },
    {
      theme: 'Developer Productivity',
      relatedIdeaIds: ['idea-3'],
      suggestedAngle: 'Deep dive into tooling trends',
      confidence: 0.92,
    },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SmartSuggestionsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideAnimationsAsync()],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(SmartSuggestionsComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    httpMock.verify();
  });

  function initWithConnections(connections: IdeaConnection[]): void {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/ideas/connections');
    req.flush(connections);
    fixture.detectChanges();
  }

  it('should render connection groups with theme labels', () => {
    initWithConnections(mockConnections);
    const groups = fixture.nativeElement.querySelectorAll('[data-testid="suggestion-group"]');
    expect(groups.length).toBe(2);
    const themes = fixture.nativeElement.querySelectorAll('.theme-label');
    expect(themes[0].textContent.trim()).toBe('Developer Productivity');
    expect(themes[1].textContent.trim()).toBe('AI Governance');
  });

  it('should show suggested angle text per group', () => {
    initWithConnections(mockConnections);
    const angles = fixture.nativeElement.querySelectorAll('.suggested-angle');
    expect(angles[0].textContent.trim()).toBe('Deep dive into tooling trends');
    expect(angles[1].textContent.trim()).toBe('Compare frameworks across industries');
  });

  it('should emit createContent on Draft It click', () => {
    spyOn(component.createContent, 'emit');
    initWithConnections(mockConnections);
    const btn = fixture.nativeElement.querySelector('[data-testid="draft-btn"] button') as HTMLElement;
    btn.click();
    expect(component.createContent.emit).toHaveBeenCalledWith('idea-3');
  });

  it('should be hidden when connections array is empty', () => {
    initWithConnections([]);
    const panel = fixture.nativeElement.querySelector('[data-testid="suggestions-panel"]');
    expect(panel).toBeNull();
  });

  it('should be collapsible via toggle button', () => {
    initWithConnections(mockConnections);
    let groups = fixture.nativeElement.querySelectorAll('[data-testid="suggestion-group"]');
    expect(groups.length).toBe(2);

    const toggleBtn = fixture.nativeElement.querySelector('[data-testid="collapse-btn"]') as HTMLElement;
    toggleBtn.click();
    fixture.detectChanges();
    groups = fixture.nativeElement.querySelectorAll('[data-testid="suggestion-group"]');
    expect(groups.length).toBe(0);

    toggleBtn.click();
    fixture.detectChanges();
    groups = fixture.nativeElement.querySelectorAll('[data-testid="suggestion-group"]');
    expect(groups.length).toBe(2);
  });

  it('should sort groups by confidence descending', () => {
    initWithConnections(mockConnections);
    const themes = fixture.nativeElement.querySelectorAll('.theme-label');
    expect(themes[0].textContent.trim()).toBe('Developer Productivity');
    expect(themes[1].textContent.trim()).toBe('AI Governance');
  });

  it('should show "Unknown idea" for IDs not in store', () => {
    initWithConnections([mockConnections[0]]);
    const links = fixture.nativeElement.querySelectorAll('.idea-link');
    expect(links[0].textContent.trim()).toBe('Unknown idea');
  });

  it('should handle API error gracefully', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne('/api/ideas/connections');
    req.error(new ProgressEvent('error'));
    fixture.detectChanges();
    const panel = fixture.nativeElement.querySelector('[data-testid="suggestions-panel"]');
    expect(panel).toBeNull();
    expect(component.isLoading()).toBeFalse();
  });
});
