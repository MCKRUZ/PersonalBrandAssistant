import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ConfirmationService, Confirmation } from 'primeng/api';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { BrandProfileComponent } from './brand-profile.component';
import { IdeaService } from '../../../../core/services/idea.service';
import { IdeaStore } from '../../store/idea.store';
import { BrandRankingProfile } from '../../../../models/brand-profile.model';

function makeProfile(): BrandRankingProfile {
  return {
    id: 'profile-1', version: 1,
    positioning: 'Pos', audiencePrimary: 'Aud', audienceSecondary: null,
    halfLifeDays: 7, decayFloor: 0.075, antiTopicMultiplier: 0.1, authorityBoost: 1.2,
    pillars: [{ id: 'p1', name: 'P1', description: 'D1', weight: 0.6, order: 0 }],
    authorityTopics: ['a1'], antiTopics: ['x1'], voiceMarkers: ['v1'],
    concurrencyToken: '0', updatedAt: '2026-01-01T00:00:00Z',
  };
}

describe('BrandProfileComponent', () => {
  let fixture: ComponentFixture<BrandProfileComponent>;
  let component: BrandProfileComponent;
  let ideaService: jasmine.SpyObj<IdeaService>;
  let store: { loadIdeas: jasmine.Spy };
  let confirmSpy: jasmine.Spy;

  beforeEach(() => {
    ideaService = jasmine.createSpyObj<IdeaService>('IdeaService', [
      'getBrandProfile', 'updateBrandProfile',
    ]);
    ideaService.getBrandProfile.and.returnValue(of(makeProfile()));
    ideaService.updateBrandProfile.and.returnValue(of(makeProfile()));
    store = { loadIdeas: jasmine.createSpy('loadIdeas') };

    TestBed.configureTestingModule({
      imports: [BrandProfileComponent],
      providers: [
        provideNoopAnimations(),
        { provide: IdeaService, useValue: ideaService },
        { provide: IdeaStore, useValue: store },
      ],
    });

    fixture = TestBed.createComponent(BrandProfileComponent);
    component = fixture.componentInstance;
    // Spy on the component-scoped ConfirmationService instance.
    confirmSpy = spyOn(
      fixture.debugElement.injector.get(ConfirmationService),
      'confirm'
    ).and.callThrough();
    component.ngOnInit(); // getBrandProfile is synchronous (of) -> form populated, ready
  });

  it('loads the active profile and populates the form, with no update PUT on load', () => {
    expect(ideaService.getBrandProfile).toHaveBeenCalled();
    expect(component.form.get('positioning')!.value).toBe('Pos');
    expect(component.pillars.length).toBe(1);
    expect(component.version()).toBe(1);
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
  });

  it('weight change auto-applies a weights-only update and reloads ideas, with NO confirm dialog', fakeAsync(() => {
    component.pillars.at(0).get('weight')!.setValue(0.9);
    tick(400);

    expect(ideaService.updateBrandProfile).toHaveBeenCalledTimes(1);
    const req = ideaService.updateBrandProfile.calls.mostRecent().args[0];
    expect(req.positioning).toBe('Pos');        // baseline definition, not smuggled
    expect(req.pillars[0].weight).toBe(0.9);     // the new weight
    expect(req.concurrencyToken).toBe('0');
    expect(store.loadIdeas).toHaveBeenCalled();
    expect(confirmSpy).not.toHaveBeenCalled();
  }));

  it('stages a pillar-definition edit — no immediate PUT, requires the confirm dialog', fakeAsync(() => {
    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
    tick(400);

    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
    expect(component.definitionsDirty()).toBeTrue();
  }));

  it('the confirm dialog warns about LLM cost, and accepting sends the definition update', fakeAsync(() => {
    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
    tick(400);

    component.saveDefinitions();

    expect(confirmSpy).toHaveBeenCalled();
    const cfg = confirmSpy.calls.mostRecent().args[0] as Confirmation;
    expect(cfg.message!.toLowerCase()).toContain('token'); // warns about LLM token cost
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled(); // not until accept

    cfg.accept!();
    expect(ideaService.updateBrandProfile).toHaveBeenCalledTimes(1);
    const req = ideaService.updateBrandProfile.calls.mostRecent().args[0];
    expect(req.pillars[0].name).toBe('Renamed Pillar');
  }));

  it('adding a pillar is staged — no auto-PUT, requires confirm', fakeAsync(() => {
    component.addPillar();
    tick(400);
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
    expect(component.definitionsDirty()).toBeTrue();
  }));

  it('removing a pillar is staged — no auto-PUT', fakeAsync(() => {
    component.addPillar(); // 2 pillars so removal keeps min-1 validity
    tick(400);
    component.pillars.at(0).get('name')!.setValue('Keep'); // make the added one valid-ish
    component.removePillar(1);
    tick(400);
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
    expect(component.definitionsDirty()).toBeTrue();
  }));

  it('a weight nudge while a definition edit is staged does NOT auto-apply', fakeAsync(() => {
    component.pillars.at(0).get('name')!.setValue('Renamed'); // stage a definition edit
    tick(400);
    component.pillars.at(0).get('weight')!.setValue(0.95); // nudge weight while staged
    tick(400);
    // The staged definition edit must not ride a no-confirm weights apply.
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
  }));

  it('blocks save (and auto-apply) when the last pillar is removed', fakeAsync(() => {
    component.removePillar(0); // 0 pillars -> form invalid
    tick(400);
    expect(component.form.invalid).toBeTrue();
    component.saveDefinitions();
    expect(confirmSpy).not.toHaveBeenCalled();
    expect(ideaService.updateBrandProfile).not.toHaveBeenCalled();
  }));

  it('a 409 on the weights auto-apply resyncs from the server and does not blind-retry', fakeAsync(() => {
    ideaService.updateBrandProfile.and.returnValue(throwError(() => ({ status: 409 })));
    ideaService.getBrandProfile.calls.reset();
    ideaService.getBrandProfile.and.returnValue(of(makeProfile()));

    component.pillars.at(0).get('weight')!.setValue(0.9);
    tick(400);

    expect(component.conflictMessage()).toContain('changed elsewhere');
    expect(ideaService.getBrandProfile).toHaveBeenCalledTimes(1); // resynced, token refreshed
  }));

  it('blocks save when validation fails (weight out of range)', () => {
    component.pillars.at(0).get('weight')!.setValue(1.5); // invalid
    component.saveDefinitions();
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('blocks save when positioning is empty', () => {
    component.form.get('positioning')!.setValue('');
    component.saveDefinitions();
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('surfaces a 409 conflict on save instead of losing the edit', fakeAsync(() => {
    ideaService.updateBrandProfile.and.returnValue(throwError(() => ({ status: 409 })));
    component.pillars.at(0).get('name')!.setValue('Renamed Pillar');
    tick(400);

    component.saveDefinitions();
    (confirmSpy.calls.mostRecent().args[0] as Confirmation).accept!();

    expect(component.conflictMessage()).toContain('changed elsewhere');
    // The edit is NOT silently lost: the staged value remains in the form, and the PUT carried it.
    expect(component.pillars.at(0).get('name')!.value).toBe('Renamed Pillar');
    expect(ideaService.updateBrandProfile.calls.mostRecent().args[0].pillars[0].name)
      .toBe('Renamed Pillar');
  }));
});
