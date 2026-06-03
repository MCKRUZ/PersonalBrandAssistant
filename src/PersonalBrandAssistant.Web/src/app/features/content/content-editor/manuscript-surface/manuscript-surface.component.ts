import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
} from '@angular/core';
import { ContentDetail, ContentStatus } from '../../models/content.model';
import { STATUS_META } from '../../content-list/content-display.utils';
import { ProseEditorComponent } from '../prose-editor/prose-editor.component';

/** Static studio author label — there is no `author` field on ContentDetail. */
const STUDIO_AUTHOR = 'You';

@Component({
  selector: 'app-manuscript-surface',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ProseEditorComponent],
  template: `
    <article class="manuscript">
      <!-- editable tag chips -->
      <div class="tags-row" data-testid="tags-row">
        @for (tag of content().tags; track tag) {
          <span class="tag-chip" data-testid="tag-chip">
            {{ tag }}
            @if (canEdit()) {
              <button class="tag-remove" type="button"
                [attr.aria-label]="'Remove ' + tag"
                (click)="removeTag(tag)">&times;</button>
            }
          </span>
        }
        @if (canEdit()) {
          <input class="tag-add" type="text" placeholder="Add tag..."
            data-testid="tag-add"
            (keydown.enter)="addTag($event)" />
        }
      </div>

      <!-- editable title -->
      <input class="title" data-testid="title-input"
        [value]="content().title"
        [readonly]="!canEdit()"
        placeholder="Untitled"
        (input)="onTitleInput($any($event.target).value)" />

      <!-- derived, display-only subtitle -->
      <p class="subtitle" data-testid="subtitle">{{ subtitle() }}</p>

      <!-- byline -->
      <div class="byline" data-testid="byline">
        <span class="avatar">{{ authorInitial }}</span>
        <span class="byline-text">{{ author }} &middot; {{ statusLabel() }}</span>
      </div>

      <!-- body -->
      @if (isIdea()) {
        <div class="idea-panel" data-testid="idea-panel">
          @if (drafting()) {
            <span class="drafting-spinner" data-testid="drafting-spinner"></span>
            <p class="idea-text">Drafting your post&hellip;</p>
            <p class="idea-sub">The assistant is writing a first draft. This can take up to a minute.</p>
          } @else {
            <p class="idea-text">This is still just an idea.</p>
            <button class="start-draft" type="button" data-testid="start-draft-btn"
              (click)="startDraft.emit()">
              Start draft
            </button>
          }
        </div>
      } @else {
        <app-prose-editor
          [value]="content().body"
          [readOnly]="!canEdit()"
          (valueChange)="onBodyChange($event)" />
      }
    </article>
  `,
  styles: [`
    .manuscript {
      max-width: 680px;
      margin: 0 auto;
      padding: 46px 32px 120px;
    }
    .tags-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 6px;
      margin-bottom: 18px;
    }
    .tag-chip {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      font-size: 12px;
      color: var(--text-secondary);
      background: var(--surface-inset);
      border: 1px solid var(--surface-border);
      border-radius: var(--r-pill);
      padding: 2px 10px;
    }
    .tag-remove {
      background: none;
      border: none;
      color: var(--text-muted);
      cursor: pointer;
      font-size: 14px;
      line-height: 1;
      padding: 0;
    }
    .tag-remove:hover { color: var(--text-primary); }
    .tag-add {
      background: transparent;
      border: none;
      outline: none;
      color: var(--text-primary);
      font-size: 12px;
      min-width: 90px;
    }
    .title {
      width: 100%;
      background: transparent;
      border: none;
      outline: none;
      font-family: var(--font-display);
      font-size: 40px;
      line-height: 1.15;
      color: var(--text-primary);
      padding: 0;
      margin-bottom: 10px;
    }
    .title::placeholder { color: var(--text-muted); }
    .subtitle {
      font-size: 19px;
      line-height: 1.45;
      color: var(--text-secondary);
      margin: 0 0 20px;
    }
    .byline {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 30px;
      padding-bottom: 20px;
      border-bottom: 1px solid var(--surface-border);
    }
    .avatar {
      display: grid;
      place-items: center;
      width: 30px;
      height: 30px;
      border-radius: 50%;
      background: var(--accent-soft);
      color: var(--brand-primary);
      font-size: 13px;
      font-weight: 600;
    }
    .byline-text { font-size: 13px; color: var(--text-muted); }
    .idea-panel {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      padding: 56px 24px;
      border: 1.5px dashed var(--surface-border);
      border-radius: var(--r);
      text-align: center;
    }
    .idea-text { margin: 0; font-size: 16px; color: var(--text-secondary); }
    .start-draft {
      background: var(--brand-primary);
      color: #1a0f0a;
      border: none;
      border-radius: var(--r-control);
      font-size: 14px;
      font-weight: 600;
      padding: 10px 20px;
      cursor: pointer;
    }
    .start-draft:hover { filter: brightness(1.05); }
    .idea-sub { color: var(--text-muted); font-size: 13px; margin-top: 8px; }
    .drafting-spinner {
      width: 22px; height: 22px; border-radius: 50%;
      border: 2px solid var(--surface-border); border-top-color: var(--brand-primary);
      animation: ms-spin 0.7s linear infinite; margin: 0 auto 14px;
    }
    @keyframes ms-spin { to { transform: rotate(360deg); } }
  `],
})
export class ManuscriptSurfaceComponent {
  readonly content = input.required<ContentDetail>();
  readonly canEdit = input.required<boolean>();
  /** True while a draft is being generated (Start draft -> AI generation in flight). */
  readonly drafting = input(false);

  readonly titleChange = output<string>();
  readonly bodyChange = output<string>();
  readonly tagsChange = output<string[]>();
  readonly startDraft = output<void>();

  readonly author = STUDIO_AUTHOR;
  readonly authorInitial = STUDIO_AUTHOR.charAt(0).toUpperCase();

  readonly isIdea = computed(() => this.content().status === ContentStatus.Idea);

  readonly statusLabel = computed(() => STATUS_META[this.content().status].label);

  /** First non-heading, non-empty line of the body. Display-only; never persisted. */
  readonly subtitle = computed(() => {
    const body = this.content().body ?? '';
    for (const raw of body.split('\n')) {
      const line = raw.trim();
      if (!line || line.startsWith('#')) continue;
      return line.replace(/[*_`>#-]/g, '').trim().slice(0, 160);
    }
    return 'A draft in progress.';
  });

  onTitleInput(value: string): void {
    this.titleChange.emit(value);
  }

  onBodyChange(markdown: string): void {
    this.bodyChange.emit(markdown);
  }

  addTag(event: Event): void {
    const input = event.target as HTMLInputElement;
    const tag = input.value.trim();
    if (!tag) return;
    const current = this.content().tags;
    if (!current.includes(tag)) {
      this.tagsChange.emit([...current, tag]);
    }
    input.value = '';
  }

  removeTag(tag: string): void {
    this.tagsChange.emit(this.content().tags.filter((t) => t !== tag));
  }
}
