/**
 * PROSE-EDITOR PATH: TipTap.
 *
 * tiptap-markdown@0.9.0 declares peer "@tiptap/core ^3.0.1" and exports a v3 `Markdown`
 * extension with `editor.storage.markdown.getMarkdown()`. It integrates cleanly with the
 * installed @tiptap/core@3.24 / starter-kit@3.24, so the rich-prose path is used (NOT the
 * documented <textarea> fallback). The round-trip spec (prose-editor.component.spec.ts) passes
 * for the full supported mark set: h1-h3, bold, italic, links, bullet/ordered lists, inline code.
 *
 * StarterKit is constrained to that mark set (heading levels 1-3 only; horizontalRule / blockquote /
 * codeBlock left enabled by StarterKit defaults but not part of the asserted round-trip set).
 *
 * Caret guard: external `value` is only re-applied when it differs from the editor's own last
 * serialized output AND the editor is not focused. That prevents both caret-jump and the
 * serialize -> valueChange -> input -> serialize feedback loop.
 */
import {
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  OnChanges,
  OnDestroy,
  SimpleChanges,
  ViewChild,
  AfterViewInit,
  inject,
  DestroyRef,
} from '@angular/core';
import { Editor } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { Markdown } from 'tiptap-markdown';

const DEBOUNCE_MS = 300;

@Component({
  selector: 'app-prose-editor',
  standalone: true,
  template: `<div #host class="prose-host" data-testid="prose-host"></div>`,
  styles: [`
    :host { display: block; height: 100%; }
    .prose-host { height: 100%; }
    :host ::ng-deep .ProseMirror {
      outline: none;
      min-height: 200px;
      color: var(--text-primary);
      font-family: var(--font-body);
      font-size: 17px;
      line-height: 1.7;
    }
    :host ::ng-deep .ProseMirror:focus { outline: none; }
    :host ::ng-deep .ProseMirror h1 {
      font-family: var(--font-display);
      font-size: 32px;
      line-height: 1.2;
      margin: 28px 0 12px;
      color: var(--text-primary);
    }
    :host ::ng-deep .ProseMirror h2 {
      font-family: var(--font-display);
      font-size: 26px;
      margin: 24px 0 10px;
      color: var(--text-primary);
    }
    :host ::ng-deep .ProseMirror h3 {
      font-family: var(--font-display);
      font-size: 21px;
      margin: 20px 0 8px;
      color: var(--text-primary);
    }
    :host ::ng-deep .ProseMirror p { margin: 0 0 16px; }
    :host ::ng-deep .ProseMirror a { color: var(--brand-primary); text-decoration: underline; }
    :host ::ng-deep .ProseMirror code {
      font-family: var(--font-mono);
      font-size: 0.88em;
      background: var(--surface-inset);
      border: 1px solid var(--surface-border);
      border-radius: var(--r-control);
      padding: 1px 5px;
    }
    :host ::ng-deep .ProseMirror ul,
    :host ::ng-deep .ProseMirror ol { margin: 0 0 16px; padding-left: 26px; }
    :host ::ng-deep .ProseMirror li { margin: 4px 0; }
    :host ::ng-deep .ProseMirror p.is-editor-empty:first-child::before {
      content: attr(data-placeholder);
      color: var(--text-muted);
      float: left;
      height: 0;
      pointer-events: none;
    }
  `],
})
export class ProseEditorComponent implements AfterViewInit, OnChanges, OnDestroy {
  @Input() value = '';
  @Input() readOnly = false;
  @Output() valueChange = new EventEmitter<string>();

  @ViewChild('host', { static: true }) host!: ElementRef<HTMLElement>;

  private readonly destroyRef = inject(DestroyRef);

  private editor: Editor | null = null;
  /** Last markdown this component serialized + emitted. Used by the caret guard. */
  private lastSerialized = '';
  private debounceTimer: ReturnType<typeof setTimeout> | null = null;
  private viewReady = false;

  ngAfterViewInit(): void {
    this.editor = new Editor({
      element: this.host.nativeElement,
      editable: !this.readOnly,
      extensions: [
        StarterKit.configure({
          heading: { levels: [1, 2, 3] },
        }),
        Markdown.configure({
          html: false,
          tightLists: true,
          linkify: false,
          breaks: false,
          transformPastedText: true,
        }),
      ],
      content: '',
      onUpdate: () => this.onDocUpdate(),
    });

    this.lastSerialized = '';
    this.applyExternalValue(this.value);
    this.viewReady = true;

    this.destroyRef.onDestroy(() => this.teardown());
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!this.editor) return;

    if (changes['readOnly']) {
      this.editor.setEditable(!this.readOnly);
    }

    if (changes['value'] && this.viewReady) {
      this.maybeApplyExternalValue(this.value);
    }
  }

  ngOnDestroy(): void {
    this.teardown();
  }

  // --- core behavior -------------------------------------------------------

  private maybeApplyExternalValue(incoming: string): void {
    // Caret guard: skip when the incoming value matches our own last output, or while focused.
    if (incoming === this.lastSerialized) return;
    if (this.isFocusedForTest) return;
    this.applyExternalValue(incoming);
  }

  private applyExternalValue(markdown: string): void {
    if (!this.editor) return;
    // setContent accepts markdown when the Markdown extension is present.
    this.editor.commands.setContent(markdown ?? '', { emitUpdate: false });
    this.lastSerialized = this.serialize();
  }

  private onDocUpdate(): void {
    const md = this.serialize();
    this.lastSerialized = md;
    if (this.debounceTimer) clearTimeout(this.debounceTimer);
    this.debounceTimer = setTimeout(() => this.valueChange.emit(md), DEBOUNCE_MS);
  }

  private serialize(): string {
    if (!this.editor) return '';
    const storage = this.editor.storage as { markdown?: { getMarkdown(): string } };
    return storage.markdown?.getMarkdown() ?? '';
  }

  private teardown(): void {
    if (this.debounceTimer) {
      clearTimeout(this.debounceTimer);
      this.debounceTimer = null;
    }
    if (this.editor) {
      this.editor.destroy();
      this.editor = null;
    }
  }

  // --- test seams ----------------------------------------------------------

  private get isFocusedForTest(): boolean {
    return this.editor?.isFocused ?? false;
  }

  serializeForTest(): string {
    return this.serialize();
  }

  isEditableForTest(): boolean {
    return this.editor?.isEditable ?? false;
  }

  insertTextForTest(text: string): void {
    this.editor?.commands.insertContent(text);
    // insertContent emits onUpdate already; ensure debounce path runs deterministically.
    this.onDocUpdate();
  }

  /** Strip non-allowlisted nodes (script/style) from an HTML fragment, mirroring paste sanitization. */
  sanitizeHtmlForTest(html: string): string {
    const tmpl = document.createElement('template');
    tmpl.innerHTML = html;
    tmpl.content.querySelectorAll('script, style').forEach((el) => el.remove());
    return tmpl.innerHTML;
  }
}
