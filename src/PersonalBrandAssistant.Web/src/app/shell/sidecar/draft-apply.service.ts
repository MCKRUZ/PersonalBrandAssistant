import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class DraftApplyService {
  private readonly _apply$ = new Subject<string>();
  readonly apply$ = this._apply$.asObservable();

  applyDraft(text: string): void {
    this._apply$.next(text);
  }
}
