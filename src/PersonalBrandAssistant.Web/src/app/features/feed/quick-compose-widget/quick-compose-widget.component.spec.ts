import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { NO_ERRORS_SCHEMA } from '@angular/core';
import { QuickComposeWidgetComponent } from './quick-compose-widget.component';
import { IdeaService } from '../../../core/services/idea.service';
import { ContentService } from '../../content/services/content.service';

describe('QuickComposeWidgetComponent', () => {
  let fixture: ComponentFixture<QuickComposeWidgetComponent>;
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
      schemas: [NO_ERRORS_SCHEMA],
    }).compileComponents();

    fixture = TestBed.createComponent(QuickComposeWidgetComponent);
    fixture.detectChanges();
  });

  function query(testId: string): HTMLElement | null {
    return fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  }

  function setInputValue(testId: string, value: string): void {
    const el = query(testId) as HTMLInputElement | HTMLTextAreaElement;
    el.value = value;
    el.dispatchEvent(new Event('input'));
  }

  it('should default to idea mode', () => {
    const ideaTab = query('mode-idea')!;
    expect(ideaTab.classList.contains('active')).toBeTrue();
    expect(query('note-field')).toBeTruthy();
  });

  it('should toggle to content mode', () => {
    (query('mode-content') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(query('content-type-field')).toBeTruthy();
    expect(query('note-field')).toBeNull();
  });

  it('should show title and note fields in idea mode', () => {
    expect(query('title-field')).toBeTruthy();
    expect(query('note-field')).toBeTruthy();
  });

  it('should show title and content type dropdown in content mode', () => {
    (query('mode-content') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(query('title-field')).toBeTruthy();
    expect(query('content-type-field')).toBeTruthy();
    expect(query('note-field')).toBeNull();
  });

  it('should call ideaService.create on idea submit', fakeAsync(() => {
    setInputValue('title-field', 'My Idea');
    fixture.detectChanges();
    tick();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    tick();

    expect(ideaServiceSpy.create).toHaveBeenCalledWith(
      jasmine.objectContaining({ title: 'My Idea' })
    );
  }));

  it('should call contentService.create on content submit and navigate', fakeAsync(() => {
    (query('mode-content') as HTMLButtonElement).click();
    fixture.detectChanges();
    tick();

    setInputValue('title-field', 'Blog Post Title');
    fixture.detectChanges();
    tick();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    tick();

    expect(contentServiceSpy.create).toHaveBeenCalledWith(
      jasmine.objectContaining({ title: 'Blog Post Title' })
    );
    expect(routerSpy.navigate).toHaveBeenCalledWith(['/content', 'content-1']);
  }));

  it('should clear form after successful idea submission', fakeAsync(() => {
    setInputValue('title-field', 'Temp Idea');
    fixture.detectChanges();
    tick();

    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    tick();
    fixture.detectChanges();
    tick();

    const titleInput = query('title-field') as HTMLInputElement;
    expect(titleInput.value).toBe('');
  }));
});
