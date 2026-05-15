import { Component, input, output, computed } from '@angular/core';
import { ButtonModule } from 'primeng/button';
import { ContentStatus } from '../../models/content.model';

export interface DraftActionEvent {
  action: string;
  instructions?: string;
  toneName?: string;
}

@Component({
  selector: 'app-editor-toolbar',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="toolbar" data-testid="editor-toolbar">
      <p-button label="Draft" icon="pi pi-pencil" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !canDraft()"
        (onClick)="draftAction.emit({ action: 'draft' })" data-testid="chip-draft" />
      <p-button label="Refine" icon="pi pi-sync" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !hasBody()"
        (onClick)="draftAction.emit({ action: 'refine' })" data-testid="chip-refine" />
      <p-button label="Shorten" icon="pi pi-minus-circle" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !hasBody()"
        (onClick)="draftAction.emit({ action: 'shorten' })" data-testid="chip-shorten" />
      <p-button label="Expand" icon="pi pi-plus-circle" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !hasBody()"
        (onClick)="draftAction.emit({ action: 'expand' })" data-testid="chip-expand" />
      <p-button label="Change Tone" icon="pi pi-palette" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !hasBody()"
        (onClick)="draftAction.emit({ action: 'changeTone' })" data-testid="chip-tone" />
      <p-button label="Cross-Post" icon="pi pi-share-alt" [rounded]="true" [outlined]="true"
        size="small" [disabled]="allDisabled() || !canCrossPost()"
        (onClick)="crossPostAction.emit()" data-testid="chip-crosspost" />
    </div>
  `,
  styles: [`
    .toolbar {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      flex-wrap: wrap;
    }
  `],
})
export class EditorToolbarComponent {
  readonly isLoading = input<boolean>(false);
  readonly status = input<ContentStatus | null>(null);
  readonly hasBody = input<boolean>(false);

  readonly draftAction = output<DraftActionEvent>();
  readonly crossPostAction = output<void>();

  readonly allDisabled = computed(() => {
    const s = this.status();
    return this.isLoading() || s === ContentStatus.Published || s === ContentStatus.Archived;
  });

  readonly canDraft = computed(() => {
    const s = this.status();
    return (s === ContentStatus.Idea || s === ContentStatus.Draft) && !this.hasBody();
  });

  readonly canCrossPost = computed(() => {
    const s = this.status();
    return this.hasBody() && s !== ContentStatus.Idea;
  });
}
