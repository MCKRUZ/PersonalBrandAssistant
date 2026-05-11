import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { ViewToggleComponent } from './view-toggle.component';
import { IdeaStore } from '../../store/idea.store';

describe('ViewToggleComponent', () => {
  let fixture: ComponentFixture<ViewToggleComponent>;
  let component: ViewToggleComponent;
  let store: InstanceType<typeof IdeaStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ViewToggleComponent],
      providers: [provideHttpClient()],
    }).compileComponents();

    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(ViewToggleComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should render grid and list toggle buttons', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="grid-toggle"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="list-toggle"]')).toBeTruthy();
  });

  it('should default to list mode', () => {
    expect(store.viewMode()).toBe('list');
  });

  it('should toggle to grid mode on grid button click', () => {
    const gridBtn = fixture.nativeElement.querySelector('[data-testid="grid-toggle"] button') as HTMLElement;
    gridBtn.click();
    fixture.detectChanges();
    expect(store.viewMode()).toBe('grid');
  });
});
