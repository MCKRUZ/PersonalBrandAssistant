import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { IdeasComponent } from './ideas.component';
import { IdeaStore } from './store/idea.store';

describe('IdeasComponent', () => {
  let fixture: ComponentFixture<IdeasComponent>;
  let component: IdeasComponent;
  let store: InstanceType<typeof IdeaStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeasComponent],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();

    store = TestBed.inject(IdeaStore);
    spyOn(store, 'loadIdeas');
    fixture = TestBed.createComponent(IdeasComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should load ideas on init', () => {
    expect(store.loadIdeas).toHaveBeenCalled();
  });

  it('should render the three-column layout', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="ideas-page"]')).toBeTruthy();
    expect(el.querySelector('.filter-sidebar')).toBeTruthy();
    expect(el.querySelector('.ideas-main')).toBeTruthy();
    expect(el.querySelector('.suggestions-sidebar')).toBeTruthy();
  });

  it('should render search input', () => {
    const input = fixture.nativeElement.querySelector('[data-testid="search-input"]') as HTMLElement;
    expect(input).toBeTruthy();
  });

  it('should render Idea Bank title', () => {
    const h1 = fixture.nativeElement.querySelector('h1') as HTMLElement;
    expect(h1.textContent?.trim()).toBe('Idea Bank');
  });
});
