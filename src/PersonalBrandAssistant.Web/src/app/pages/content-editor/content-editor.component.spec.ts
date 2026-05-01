import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ContentEditorComponent, PLATFORM_CHAR_LIMITS } from './content-editor.component';
import { ContentEditorStore } from './content-editor.store';
import { DraftApplyService } from '../../shell/sidecar/draft-apply.service';

describe('ContentEditorComponent', () => {
  let component: ContentEditorComponent;
  let fixture: ComponentFixture<ContentEditorComponent>;
  let store: InstanceType<typeof ContentEditorStore>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ContentEditorComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: { get: () => 'test-id' } },
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ContentEditorComponent);
    component = fixture.componentInstance;
    store = fixture.debugElement.injector.get(ContentEditorStore);
    spyOn(store, 'loadContent');
    spyOn(store, 'updateField');
    spyOn(store, 'scoreContent');
    spyOn(store, 'approveAndPublish');
    spyOn(store, 'applyDraft');
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should call store.loadContent on init with route param', () => {
    component.ngOnInit();
    expect(store.loadContent).toHaveBeenCalledWith('test-id');
  });

  it('should subscribe to DraftApplyService', () => {
    const draftService = TestBed.inject(DraftApplyService);
    component.ngOnInit();
    draftService.applyDraft('draft text');
    expect(store.applyDraft).toHaveBeenCalledWith('draft text');
  });

  it('should delegate field changes to store', () => {
    component.onTitleChange('New Title');
    expect(store.updateField).toHaveBeenCalledWith('title', 'New Title');

    component.onBodyChange('New body');
    expect(store.updateField).toHaveBeenCalledWith('body', 'New body');
  });

  it('should have correct platform options', () => {
    expect(component.platforms.length).toBe(7);
    expect(component.platforms[0].value).toBe('LinkedIn');
  });

  it('should have correct content type options', () => {
    expect(component.contentTypes.length).toBe(4);
    expect(component.contentTypes[0].value).toBe('SocialPost');
  });

  it('should define platform character limits', () => {
    expect(PLATFORM_CHAR_LIMITS['TwitterX']).toBe(280);
    expect(PLATFORM_CHAR_LIMITS['LinkedIn']).toBe(3000);
    expect(PLATFORM_CHAR_LIMITS['PersonalBlog']).toBeNull();
  });
});
