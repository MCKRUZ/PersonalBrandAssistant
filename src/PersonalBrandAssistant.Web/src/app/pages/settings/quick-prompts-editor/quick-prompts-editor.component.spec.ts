import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QuickPromptsEditorComponent } from './quick-prompts-editor.component';
import { QUICK_PROMPTS } from '../quick-prompts.defaults';

describe('QuickPromptsEditorComponent', () => {
  let component: QuickPromptsEditorComponent;
  let fixture: ComponentFixture<QuickPromptsEditorComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [QuickPromptsEditorComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(QuickPromptsEditorComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('prompts', { ...QUICK_PROMPTS });
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render route selector', () => {
    fixture.detectChanges();
    const select = fixture.nativeElement.querySelector('p-select');
    expect(select).toBeTruthy();
  });

  it('should show prompts when route is selected', () => {
    fixture.detectChanges();
    component.selectedRoute = 'dashboard';
    component.onRouteChange();
    fixture.detectChanges();
    const inputs = fixture.nativeElement.querySelectorAll('.prompt-row');
    expect(inputs.length).toBe(QUICK_PROMPTS['dashboard'].length);
  });

  it('should add a new prompt', () => {
    fixture.detectChanges();
    component.selectedRoute = 'dashboard';
    component.onRouteChange();
    const initial = component.currentPrompts().length;
    component.addPrompt();
    expect(component.currentPrompts().length).toBe(initial + 1);
  });

  it('should remove a prompt', () => {
    fixture.detectChanges();
    component.selectedRoute = 'dashboard';
    component.onRouteChange();
    const initial = component.currentPrompts().length;
    component.removePrompt(0);
    expect(component.currentPrompts().length).toBe(initial - 1);
  });

  it('should emit promptsChange on save', () => {
    fixture.detectChanges();
    const spy = spyOn(component.promptsChange, 'emit');
    component.save();
    expect(spy).toHaveBeenCalledWith(jasmine.objectContaining({ dashboard: jasmine.any(Array) }));
  });

  it('should emit promptsReset on reset', () => {
    fixture.detectChanges();
    const spy = spyOn(component.promptsReset, 'emit');
    component.resetToDefaults();
    expect(spy).toHaveBeenCalled();
  });
});
