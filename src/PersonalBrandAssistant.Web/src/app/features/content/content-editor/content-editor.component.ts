import { Component } from '@angular/core';

@Component({
  selector: 'app-content-editor',
  standalone: true,
  template: `
    <div class="editor-placeholder">
      <h2>Content Editor</h2>
      <p>Coming in section 15</p>
    </div>
  `,
  styles: [
    `
      .editor-placeholder {
        padding: 48px;
        text-align: center;
        color: #8b949e;
      }
      h2 {
        color: #f0f6fc;
        margin: 0 0 8px;
      }
    `,
  ],
})
export class ContentEditorComponent {}
