import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { provideRouter } from '@angular/router';
import { IdeaSourcesPageComponent } from './idea-sources.component';
import { IdeaSourceStore } from '../../store/idea-source.store';

describe('IdeaSourcesPageComponent', () => {
  let fixture: ComponentFixture<IdeaSourcesPageComponent>;
  let component: IdeaSourcesPageComponent;
  let store: InstanceType<typeof IdeaSourceStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [IdeaSourcesPageComponent],
      providers: [provideHttpClient(), provideAnimationsAsync(), provideRouter([])],
    }).compileComponents();

    store = TestBed.inject(IdeaSourceStore);
    fixture = TestBed.createComponent(IdeaSourcesPageComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    fixture.detectChanges();
    expect(component).toBeTruthy();
  });

  it('should display page title', () => {
    fixture.detectChanges();
    const title = fixture.nativeElement.querySelector('h1') as HTMLElement;
    expect(title.textContent?.trim()).toBe('Idea Sources');
  });

  it('should show empty state when no sources', () => {
    fixture.detectChanges();
    const empty = fixture.nativeElement.querySelector('.empty-state') as HTMLElement;
    expect(empty).toBeTruthy();
    expect(empty.textContent).toContain('No sources configured yet');
  });

  it('should call store.loadAll on init', () => {
    spyOn(store, 'loadAll');
    fixture.detectChanges();
    expect(store.loadAll).toHaveBeenCalled();
  });

  it('should call store.refreshAll on refresh button click', () => {
    spyOn(store, 'loadAll');
    spyOn(store, 'refreshAll');
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="refresh-btn"] button') as HTMLElement;
    btn.click();
    expect(store.refreshAll).toHaveBeenCalled();
  });

  it('should open add dialog on add button click', () => {
    fixture.detectChanges();
    const btn = fixture.nativeElement.querySelector('[data-testid="add-source-btn"] button') as HTMLElement;
    btn.click();
    expect(component.addDialogVisible).toBeTrue();
  });

  it('should call store.remove on delete with confirmation', () => {
    spyOn(store, 'remove');
    spyOn(window, 'confirm').and.returnValue(true);
    fixture.detectChanges();
    component.onDelete({ id: 'src-1', name: 'Test' } as any);
    expect(store.remove).toHaveBeenCalledWith('src-1');
  });

  it('should not call store.remove when delete is cancelled', () => {
    spyOn(store, 'remove');
    spyOn(window, 'confirm').and.returnValue(false);
    fixture.detectChanges();
    component.onDelete({ id: 'src-1', name: 'Test' } as any);
    expect(store.remove).not.toHaveBeenCalled();
  });

  it('should toggle enabled state', () => {
    spyOn(store, 'update');
    fixture.detectChanges();
    component.onToggleEnabled({ id: 'src-1', isEnabled: true } as any);
    expect(store.update).toHaveBeenCalledWith('src-1', { isEnabled: false });
  });
});
