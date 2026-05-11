import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideAnimationsAsync } from '@angular/platform-browser/animations/async';
import { SaveIdeaDialogComponent } from './save-idea-dialog.component';
import { IdeaStore } from '../../store/idea.store';
import { Idea, IdeaStatus } from '../../../../models/idea.model';

describe('SaveIdeaDialogComponent', () => {
  let fixture: ComponentFixture<SaveIdeaDialogComponent>;
  let component: SaveIdeaDialogComponent;
  let store: InstanceType<typeof IdeaStore>;

  const mockIdea: Idea = {
    id: 'idea-1',
    title: 'Test Idea',
    sourceName: 'Blog',
    category: 'Tech',
    summary: 'A summary',
    thumbnailUrl: null,
    status: IdeaStatus.New,
    tags: ['existing-tag'],
    detectedAt: '2026-01-01T00:00:00Z',
    hasSavedDetails: false,
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SaveIdeaDialogComponent],
      providers: [provideHttpClient(), provideAnimationsAsync()],
    }).compileComponents();

    store = TestBed.inject(IdeaStore);
    fixture = TestBed.createComponent(SaveIdeaDialogComponent);
    component = fixture.componentInstance;
  });

  it('should display idea title when open', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    const title = fixture.nativeElement.querySelector('[data-testid="dialog-title"]') as HTMLElement;
    expect(title?.textContent?.trim()).toBe('Test Idea');
  });

  it('should pre-populate tags from idea', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    expect(component.tags).toEqual(['existing-tag']);
  });

  it('should add tag on enter key', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.tagInput = 'new-tag';
    component.addTag();
    expect(component.tags).toContain('new-tag');
    expect(component.tagInput).toBe('');
  });

  it('should not add duplicate tag', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.tagInput = 'existing-tag';
    component.addTag();
    expect(component.tags.filter((t) => t === 'existing-tag').length).toBe(1);
  });

  it('should remove tag', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.removeTag('existing-tag');
    expect(component.tags).not.toContain('existing-tag');
  });

  it('should call store.saveIdea on save', () => {
    spyOn(store, 'saveIdea');
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.notes = 'My notes';
    component.tags = ['tag1', 'tag2'];
    component.onSave();
    expect(store.saveIdea).toHaveBeenCalledWith('idea-1', 'My notes', ['tag1', 'tag2']);
  });

  it('should close dialog and reset state on cancel', () => {
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.notes = 'Some notes';
    component.tags = ['tag1'];
    component.onCancel();
    expect(component.visible()).toBeFalse();
    expect(component.notes).toBe('');
    expect(component.tags).toEqual([]);
  });

  it('should emit saved event on save', () => {
    spyOn(store, 'saveIdea');
    spyOn(component.saved, 'emit');
    fixture.componentRef.setInput('idea', mockIdea);
    fixture.componentRef.setInput('visible', true);
    fixture.detectChanges();
    component.onSave();
    expect(component.saved.emit).toHaveBeenCalled();
  });
});
