import { Component, input, output } from '@angular/core';
import { CodeEditorModule } from '@acrodata/code-editor';
import { markdown } from '@codemirror/lang-markdown';
import { LanguageDescription } from '@codemirror/language';
import { EditorView } from '@codemirror/view';
import { Extension } from '@codemirror/state';

const darkTheme = EditorView.theme({
  '&': { backgroundColor: '#0d1117', color: '#f0f6fc', height: '100%' },
  '.cm-content': { caretColor: '#58a6ff' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: '#1f6feb44 !important',
  },
  '.cm-gutters': { backgroundColor: '#161b22', color: '#8b949e', border: 'none' },
  '.cm-activeLineGutter': { backgroundColor: '#161b2280' },
  '.cm-activeLine': { backgroundColor: '#161b2240' },
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
