import { Component, inject } from '@angular/core';
import { ContentStore } from '../../stores/content.store';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-content-view-toggle',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="view-toggle">
      <p-button
        [icon]="'pi pi-th-large'"
        [severity]="store.viewMode() === 'grid' ? 'primary' : 'secondary'"
        [text]="store.viewMode() !== 'grid'"
        (onClick)="store.viewMode() !== 'grid' && store.toggleView()"
        size="small"
        data-testid="grid-toggle" />
      <p-button
        [icon]="'pi pi-list'"
        [severity]="store.viewMode() === 'list' ? 'primary' : 'secondary'"
        [text]="store.viewMode() !== 'list'"
        (onClick)="store.viewMode() !== 'list' && store.toggleView()"
        size="small"
        data-testid="list-toggle" />
    </div>
  `,
  styles: [
    `
      .view-toggle {
        display: flex;
        gap: 4px;
      }
    `,
  ],
})
export class ContentViewToggleComponent {
  readonly store = inject(ContentStore);
}
