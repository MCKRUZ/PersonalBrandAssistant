import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { AddSourceDialogComponent } from './add-source-dialog.component';
import { IdeaSourceStore } from '../../../store/idea-source.store';
import { IdeaSource, IdeaSourceType } from '../../../../../models/idea.model';

describe('AddSourceDialogComponent', () => {
  let fixture: ComponentFixture<AddSourceDialogComponent>;
  let component: AddSourceDialogComponent;
  let store: InstanceType<typeof IdeaSourceStore>;

  const mockSource: IdeaSource = {
    id: 'src-1',
    name: 'Tech Blog',
    type: IdeaSourceType.RSS,
    feedUrl: 'https://example.com/rss',
    apiUrl: null,
    category: 'Technology',
    pollIntervalMinutes: 60,
    isEnabled: true,
    lastPolledAt: null,
    lastSuccessAt: null,
    lastError: null,
    consecutiveFailures: 0,
    ideaCount: 0,
    isHealthy: true,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AddSourceDialogComponent],
      providers: [provideHttpClient(), provideAnimationsAsync()],
    }).compileComponents();

    store = TestBed.inject(IdeaSourceStore);
    fixture = TestBed.createComponent(AddSourceDialogComponent);
    component = fixture.componentInstance;
  });

  it('should show "Add Source" header when no editSource', () => {
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const header = fixture.nativeElement.querySelector('.p-dialog-title') as HTMLElement;
    expect(header?.textContent?.trim()).toBe('Add Source');
  });

  it('should show "Edit Source" header when editSource provided', () => {
    fixture.componentRef.setInput('editSource', mockSource);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const header = fixture.nativeElement.querySelector('.p-dialog-title') as HTMLElement;
    expect(header?.textContent?.trim()).toBe('Edit Source');
  });

  it('should populate form when editSource is set', () => {
    fixture.componentRef.setInput('editSource', mockSource);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(component.form.get('name')?.value).toBe('Tech Blog');
    expect(component.form.get('type')?.value).toBe(IdeaSourceType.RSS);
    expect(component.form.get('feedUrl')?.value).toBe('https://example.com/rss');
    expect(component.form.get('pollIntervalMinutes')?.value).toBe(60);
  });

  it('should require name field', () => {
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(component.form.get('name')?.valid).toBeFalse();
    component.form.get('name')?.setValue('Test');
    expect(component.form.get('name')?.valid).toBeTrue();
  });

  it('should call store.create on submit for new source', () => {
    spyOn(store, 'create');
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.form.patchValue({
      name: 'New Source',
      type: IdeaSourceType.Manual,
      category: 'General',
      pollIntervalMinutes: 30,
    });
    component.onSubmit();
    expect(store.create).toHaveBeenCalled();
  });

  it('should call store.update on submit for existing source', () => {
    spyOn(store, 'update');
    fixture.componentRef.setInput('editSource', mockSource);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.form.get('name')?.setValue('Updated Name');
    component.onSubmit();
    expect(store.update).toHaveBeenCalledWith('src-1', jasmine.objectContaining({ name: 'Updated Name' }));
  });

  it('should offer Hacker News and GitHub as source types', () => {
    const values = component.typeOptions.map((o) => o.value);
    expect(values).toContain(IdeaSourceType.HackerNews);
    expect(values).toContain(IdeaSourceType.GitHub);
  });

  it('should show GitHub repo/user input when type is GitHub', () => {
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.form.get('type')?.setValue(IdeaSourceType.GitHub);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="github-url-input"]')).toBeTruthy();
  });

  it('should show a no-URL hint when type is Hacker News', () => {
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.form.get('type')?.setValue(IdeaSourceType.HackerNews);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid="hackernews-hint"]')).toBeTruthy();
  });

  it('should reset form on cancel', () => {
    fixture.componentRef.setInput('editSource', mockSource);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.onCancel();
    expect(component.visible()).toBeFalse();
    expect(component.form.get('name')?.value).toBeFalsy();
  });

  it('should emit saved event on submit', () => {
    spyOn(store, 'create');
    spyOn(component.saved, 'emit');
    fixture.componentRef.setInput('editSource', null);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.form.patchValue({
      name: 'Test',
      type: IdeaSourceType.Manual,
      category: 'General',
      pollIntervalMinutes: 30,
    });
    component.onSubmit();
    expect(component.saved.emit).toHaveBeenCalled();
  });
});
