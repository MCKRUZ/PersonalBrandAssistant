import { TestBed } from '@angular/core/testing';
import { DraftApplyService } from './draft-apply.service';

describe('DraftApplyService', () => {
  let service: DraftApplyService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(DraftApplyService);
  });

  it('should emit draft text to apply$ subscribers', () => {
    const received: string[] = [];
    service.apply$.subscribe((text) => received.push(text));

    service.applyDraft('Draft content here');
    expect(received).toEqual(['Draft content here']);
  });

  it('should be a singleton (providedIn root)', () => {
    const second = TestBed.inject(DraftApplyService);
    expect(service).toBe(second);
  });
});
