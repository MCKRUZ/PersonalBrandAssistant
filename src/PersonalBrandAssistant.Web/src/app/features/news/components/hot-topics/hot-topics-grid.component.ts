import { Component, inject, OnInit, computed } from '@angular/core';
import { SkeletonModule } from 'primeng/skeleton';
import { EmptyStateComponent } from '../../../../shared/components/empty-state/empty-state.component';
import { HotTopicsStore } from '../../store/hot-topics.store';
import { TopicClusterCardComponent } from './topic-cluster-card.component';

@Component({
  selector: 'app-hot-topics-grid',
  standalone: true,
  imports: [SkeletonModule, EmptyStateComponent, TopicClusterCardComponent],
  template: `
    @if (store.loading()) {
      <div class="grid">
        @for (i of skeletonItems; track i) {
          <div class="col-12 md:col-4"><p-skeleton height="200px" /></div>
        }
      </div>
    } @else if (store.sortedByHeat().length === 0) {
      <app-empty-state message="No trending topics yet" icon="pi pi-bolt" />
    } @else {
      <div class="grid">
        @for (cluster of store.sortedByHeat(); track cluster.id; let i = $index) {
          <div
            [class]="i === 0 ? 'col-12' : isHighHeat(cluster.heat) ? 'col-12 md:col-6' : 'col-12 md:col-6 lg:col-4'"
          >
            <app-topic-cluster-card [cluster]="cluster" [featured]="i === 0" />
          </div>
        }
      </div>
    }
  `,
})
export class HotTopicsGridComponent implements OnInit {
  readonly store = inject(HotTopicsStore);
  readonly skeletonItems = Array.from({ length: 6 }, (_, i) => i);

  readonly sortedHeats = computed(() => this.store.sortedByHeat().map((c) => c.heat));

  ngOnInit() {
    this.store.load(undefined);
  }

  isHighHeat(heat: number): boolean {
    const heats = this.sortedHeats();
    if (heats.length < 3) return false;
    const threshold = heats[Math.floor(heats.length * 0.2)];
    return heat >= threshold;
  }
}
