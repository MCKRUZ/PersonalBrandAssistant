import { Component, input } from '@angular/core';

@Component({
  selector: 'app-streaming-text',
  standalone: true,
  template: `
    <span class="streaming-text">{{ text() }}@if (streaming()) {<span class="caret"></span>}</span>
  `,
  styles: `
    :host { display: inline; }
    .streaming-text { white-space: pre-wrap; word-break: break-word; }
  `,
})
export class StreamingTextComponent {
  text = input('');
  streaming = input(false);
}
