import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { QuickComposeWidgetComponent } from './quick-compose-widget.component';
import { IdeaService } from '../../../core/services/idea.service';
import { ContentService } from '../../content/services/content.service';
import { Router } from '@angular/router';
import { of } from 'rxjs';

describe('QuickComposeWidgetComponent', () => {
  let fixture: ComponentFixture<QuickComposeWidgetComponent>;
  let component: QuickComposeWidgetComponent;
  let ideaServiceSpy: jasmine.SpyObj<IdeaService>;
  let contentServiceSpy: jasmine.SpyObj<ContentService>;
  let routerSpy: jasmine.SpyObj<Router>;

  beforeEach(async () => {
    ideaServiceSpy = jasmine.createSpyObj('IdeaService', ['create']);
    contentServiceSpy = jasmine.createSpyObj('ContentService', ['create']);
    routerSpy = jasmine.createSpyObj('Router', ['navigate']);

    ideaServiceSpy.create.and.returnValue(of('idea-1'));
    contentServiceSpy.create.and.returnValue(of('content-1'));
    routerSpy.navigate.and.returnValue(Promise.resolve(true));

    await TestBed.configureTestingModule({
      imports: [QuickComposeWidgetComponent],
      providers: [
        { provide: IdeaService, useValue: ideaServiceSpy },
        { provide: ContentService, useValue: contentServiceSpy },
        { provide: Router, useValue: routerSpy },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuickComposeWidgetComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should default to Quick Idea mode', () => {
    const activeTab = fixture.nativeElement.querySelector('[data-testid="mode-idea"]') as HTMLElement;
    expect(activeTab.classList.contains('active')).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="note-field"]')).toBeTruthy();
  });

  it('should toggle to New Content mode', () => {
    const contentTab = fixture.nativeElement.querySelector('[data-testid="mode-content"]') as HTMLButtonElement;
    contentTab.click();
    fixture.detectChanges();

    expect(contentTab.classList.contains('active')).toBeTrue();
    expect(fixture.nativeElement.querySelector('[data-testid="content-type-field"]')).toBeTruthy();
  });

  it('should show title and note fields in Quick Idea mode', () => {
    expect(fixture.nativeElement.querySelector('[data-testid="title-field"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="note-field"]')).toBeTruthy();
  });

  it('should show title and content type dropdown in New Content mode', () => {
    const contentTab = fixture.nativeElement.querySelector('[data-testid="mode-content"]') as HTMLButtonElement;
    contentTab.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="title-field"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="content-type-field"]')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('[data-testid="note-field"]')).toBeNull();
  });

  it('should call IdeaService.create on Quick Idea submit', fakeAsync(() => {
    const titleInput = fixture.nativeElement.querySelector('[data-testid="title-field"]') as HTMLInputElement;
    const noteInput = fixture.nativeElement.querySelector('[data-testid="note-field"]') as HTMLTextAreaElement;

    titleInput.value = 'Test Idea';
    titleInput.dispatchEvent(new Event('input'));
    noteInput.value = 'Some notes';
    noteInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const submitBtn = fixture.nativeElement.querySelector('[data-testid="submit-btn"]') as HTMLButtonElement;
    submitBtn.click();
    tick();

    expect(ideaServiceSpy.create).toHaveBeenCalledWith(
      jasmine.objectContaining({ title: 'Test Idea', description: 'Some notes' })
    );
  }));

  it('should call ContentService.create on New Content submit and navigate', fakeAsync(() => {
    const contentTab = fixture.nativeElement.querySelector('[data-testid="mode-content"]') as HTMLButtonElement;
    contentTab.click();
    fixture.detectChanges();

    const titleInput = fixture.nativeElement.querySelector('[data-testid="title-field"]') as HTMLInputElement;
    titleInput.value = 'New Blog Post';
    titleInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const submitBtn = fixture.nativeElement.querySelector('[data-testid="submit-btn"]') as HTMLButtonElement;
    submitBtn.click();
    tick();

    expect(contentServiceSpy.create).toHaveBeenCalledWith(
      jasmine.objectContaining({ title: 'New Blog Post' })
    );
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/content', 'content-1']);
  }));

  it('should clear form after successful idea submission', fakeAsync(() => {
    const titleInput = fixture.nativeElement.querySelector('[data-testid="title-field"]') as HTMLInputElement;
    const noteInput = fixture.nativeElement.querySelector('[data-testid="note-field"]') as HTMLTextAreaElement;

    titleInput.value = 'Test Idea';
    titleInput.dispatchEvent(new Event('input'));
    noteInput.value = 'Some notes';
    noteInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const submitBtn = fixture.nativeElement.querySelector('[data-testid="submit-btn"]') as HTMLButtonElement;
    submitBtn.click();
    tick();
    fixture.detectChanges();
    tick();
    fixture.detectChanges();

    const updatedTitle = fixture.nativeElement.querySelector('[data-testid="title-field"]') as HTMLInputElement;
    const updatedNote = fixture.nativeElement.querySelector('[data-testid="note-field"]') as HTMLTextAreaElement;
    expect(updatedTitle.value).toBe('');
    expect(updatedNote.value).toBe('');
  }));
});
