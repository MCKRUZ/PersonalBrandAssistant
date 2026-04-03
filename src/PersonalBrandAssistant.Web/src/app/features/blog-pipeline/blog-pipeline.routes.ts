import { Routes } from '@angular/router';

export const BLOG_PIPELINE_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./blog-pipeline.component').then(
        (m) => m.BlogPipelineComponent
      ),
  },
];
