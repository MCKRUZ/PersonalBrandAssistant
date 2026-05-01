import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { SettingsStore } from './settings.store';
import { SettingsApiService } from './settings-api.service';
import { QUICK_PROMPTS, QUICK_PROMPTS_STORAGE_KEY } from './quick-prompts.defaults';
import { AutonomySettings } from '../../core/models/autonomy.model';

describe('SettingsStore', () => {
  let store: InstanceType<typeof SettingsStore>;
  let apiSpy: jasmine.SpyObj<SettingsApiService>;

  beforeEach(() => {
    apiSpy = jasmine.createSpyObj('SettingsApiService', ['getAutonomy', 'updateAutonomy']);
    apiSpy.getAutonomy.and.returnValue(of({ globalLevel: 'Draft', autoPublishThreshold: 80 } as AutonomySettings));
    apiSpy.updateAutonomy.and.returnValue(of({ globalLevel: 'AutoPublish', autoPublishThreshold: 85 } as AutonomySettings));
    localStorage.removeItem(QUICK_PROMPTS_STORAGE_KEY);

    TestBed.configureTestingModule({
      providers: [
        SettingsStore,
        { provide: SettingsApiService, useValue: apiSpy },
      ],
    });
    store = TestBed.inject(SettingsStore);
  });

  afterEach(() => {
    localStorage.removeItem(QUICK_PROMPTS_STORAGE_KEY);
  });

  it('should load autonomy level from GET /api/settings/autonomy', () => {
    store.loadAutonomy(undefined);
    expect(apiSpy.getAutonomy).toHaveBeenCalled();
    expect(store.autonomy()).toEqual({ globalLevel: 'Draft', autoPublishThreshold: 80 });
    expect(store.loading()).toBe(false);
  });

  it('should save autonomy via PUT /api/settings/autonomy', () => {
    const settings: AutonomySettings = { globalLevel: 'AutoPublish', autoPublishThreshold: 85 };
    const successSpy = jasmine.createSpy('onSuccess');
    store.saveAutonomy(settings, successSpy, () => {});
    expect(apiSpy.updateAutonomy).toHaveBeenCalledWith(settings);
    expect(store.autonomy()).toEqual({ globalLevel: 'AutoPublish', autoPublishThreshold: 85 });
    expect(store.saving()).toBe(false);
    expect(successSpy).toHaveBeenCalled();
  });

  it('should load brand profile with defaults', () => {
    store.loadBrandProfile();
    expect(store.brandProfile().toneSliders.length).toBe(8);
  });

  it('should update brand profile', () => {
    store.loadBrandProfile();
    const updated = { ...store.brandProfile(), guardrails: ['No profanity'] };
    store.updateBrandProfile(updated);
    expect(store.brandProfile().guardrails).toEqual(['No profanity']);
  });

  it('should load quick prompts from localStorage, falling back to defaults', () => {
    store.loadQuickPrompts();
    expect(store.quickPrompts()).toEqual(jasmine.objectContaining({ dashboard: QUICK_PROMPTS['dashboard'] }));
  });

  it('should load quick prompts from localStorage overrides when present', () => {
    localStorage.setItem(QUICK_PROMPTS_STORAGE_KEY, JSON.stringify({ dashboard: ['Custom prompt'] }));
    store.loadQuickPrompts();
    expect(store.quickPrompts()['dashboard']).toEqual(['Custom prompt']);
  });

  it('should save quick prompts to localStorage', () => {
    store.loadQuickPrompts();
    store.updateQuickPrompts('dashboard', ['New prompt']);
    expect(store.quickPrompts()['dashboard']).toEqual(['New prompt']);
    const stored = JSON.parse(localStorage.getItem(QUICK_PROMPTS_STORAGE_KEY)!);
    expect(stored['dashboard']).toEqual(['New prompt']);
  });

  it('should reset quick prompts to defaults', () => {
    localStorage.setItem(QUICK_PROMPTS_STORAGE_KEY, JSON.stringify({ dashboard: ['Custom'] }));
    store.loadQuickPrompts();
    store.resetQuickPrompts();
    expect(store.quickPrompts()['dashboard']).toEqual(QUICK_PROMPTS['dashboard']);
    expect(localStorage.getItem(QUICK_PROMPTS_STORAGE_KEY)).toBeNull();
  });

  it('should handle API error gracefully on load', () => {
    apiSpy.getAutonomy.and.returnValue(throwError(() => new Error('fail')));
    store.loadAutonomy(undefined);
    expect(store.loading()).toBe(false);
    expect(store.autonomy()).toBeUndefined();
  });
});
