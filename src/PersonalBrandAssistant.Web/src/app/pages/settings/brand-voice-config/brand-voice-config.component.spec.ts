import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BrandVoiceConfigComponent } from './brand-voice-config.component';
import { DEFAULT_BRAND_PROFILE } from '../brand-profile.model';

describe('BrandVoiceConfigComponent', () => {
  let component: BrandVoiceConfigComponent;
  let fixture: ComponentFixture<BrandVoiceConfigComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BrandVoiceConfigComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(BrandVoiceConfigComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should render 8 tone sliders', () => {
    fixture.detectChanges();
    const sliders = fixture.nativeElement.querySelectorAll('.tone-row');
    expect(sliders.length).toBe(8);
  });

  it('should render tone slider labels', () => {
    fixture.detectChanges();
    const labels = fixture.nativeElement.querySelectorAll('.tone-label');
    expect(labels[0].textContent).toContain('Authoritative');
    expect(labels[1].textContent).toContain('Casual');
  });

  it('should render vocabulary pills with add/remove', () => {
    fixture.componentRef.setInput('profile', {
      ...DEFAULT_BRAND_PROFILE,
      vocabularyPreferences: { preferredTerms: ['Angular', 'TypeScript'], avoidTerms: ['React'] },
    });
    fixture.detectChanges();
    fixture.detectChanges();
    const chips = fixture.nativeElement.querySelectorAll('p-chip');
    expect(chips.length).toBe(3);
  });

  it('should add preferred term', () => {
    fixture.detectChanges();
    component.newPreferred = 'TestTerm';
    component.addPreferred();
    expect(component.preferredTerms()).toContain('TestTerm');
  });

  it('should remove preferred term', () => {
    fixture.componentRef.setInput('profile', {
      ...DEFAULT_BRAND_PROFILE,
      vocabularyPreferences: { preferredTerms: ['Angular', 'TypeScript'], avoidTerms: [] },
    });
    fixture.detectChanges();
    component.removePreferred(0);
    expect(component.preferredTerms()).toEqual(['TypeScript']);
  });

  it('should render pillars table', () => {
    fixture.componentRef.setInput('profile', {
      ...DEFAULT_BRAND_PROFILE,
      pillars: [{ name: 'AI', description: 'AI content', active: true }],
    });
    fixture.detectChanges();
    fixture.detectChanges();
    const table = fixture.nativeElement.querySelector('p-table');
    expect(table).toBeTruthy();
  });

  it('should add and remove guardrails', () => {
    fixture.detectChanges();
    component.newGuardrail = 'No profanity';
    component.addGuardrail();
    expect(component.guardrails()).toContain('No profanity');
    component.removeGuardrail(0);
    expect(component.guardrails().length).toBe(0);
  });

  it('should emit profileChange on save', () => {
    fixture.detectChanges();
    const spy = spyOn(component.profileChange, 'emit');
    component.save();
    expect(spy).toHaveBeenCalledWith(jasmine.objectContaining({
      toneSliders: jasmine.any(Array),
      vocabularyPreferences: jasmine.any(Object),
    }));
  });
});
