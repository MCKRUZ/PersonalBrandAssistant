import { Component, inject } from '@angular/core';
import { IdeaStore } from '../../store/idea.store';
import { ButtonModule } from 'primeng/button';

@Component({
  selector: 'app-view-toggle',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <div class="view-toggle">
      <p-button
        [icon]="'pi pi-th-large'"
        [severity]="store.viewMode() === 'grid' ? 'primary' : 'secondary'"
        [text]="store.viewMode() !== 'grid'"
        (onClick)="store.setViewMode('grid')"
        size="small"
        data-testid="grid-toggle" />
      <p-button
        [icon]="'pi pi-list'"
        [severity]="store.viewMode() === 'list' ? 'primary' : 'secondary'"
        [text]="store.viewMode() !== 'list'"
        (onClick)="store.setViewMode('list')"
        size="small"
        data-testid="list-toggle" />
      <p-button
        [icon]="'pi pi-sort-amount-down'"
        [severity]="store.viewMode() === 'ranked' ? 'primary' : 'secondary'"
        [text]="store.viewMode() !== 'ranked'"
        (onClick)="store.setViewMode('ranked')"
        size="small"
        data-testid="ranked-toggle" />
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
export class ViewToggleComponent {
  readonly store = inject(IdeaStore);
}
