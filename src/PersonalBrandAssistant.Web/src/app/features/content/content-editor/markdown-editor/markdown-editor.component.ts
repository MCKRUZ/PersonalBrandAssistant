import { Component, input, output } from '@angular/core';
import { CodeEditorModule } from '@acrodata/code-editor';
import { markdown } from '@codemirror/lang-markdown';
import { LanguageDescription } from '@codemirror/language';
import { EditorView } from '@codemirror/view';
import { Extension } from '@codemirror/state';

const darkTheme = EditorView.theme({
  '&': { backgroundColor: 'var(--surface-base)', color: 'var(--text-primary)', height: '100%' },
  '.cm-content': { caretColor: 'var(--brand-primary)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: 'var(--accent-soft) !important',
  },
  '.cm-gutters': { backgroundColor: 'var(--surface-card)', color: 'var(--text-secondary)', border: 'none' },
  '.cm-activeLineGutter': { backgroundColor: 'rgba(20, 20, 24, 0.5)' },
  '.cm-activeLine': { backgroundColor: 'rgba(20, 20, 24, 0.25)' },
});

@Component({
  selector: 'app-markdown-editor',
  standalone: true,
  imports: [CodeEditorModule],
  template: `
    <code-editor
      [value]="value()"
      [extensions]="editorExtensions"
      [languages]="languages"
      [readonly]="readOnly()"
      language="markdown"
      (change)="onValueChange($event)"
      class="editor-container"
      data-testid="code-editor">
    </code-editor>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .editor-container, :host ::ng-deep .code-editor { height: 100%; }
  `],
})
export class MarkdownEditorComponent {
  readonly value = input<string>('');
  readonly readOnly = input<boolean>(false);
  readonly valueChange = output<string>();

  readonly languages = [LanguageDescription.of({ name: 'markdown', alias: ['md'], extensions: ['md'], load: () => Promise.resolve(markdown()) })];
  readonly editorExtensions: Extension[] = [darkTheme];

  onValueChange(newValue: string): void {
    this.valueChange.emit(newValue);
  }
}
