import { Component, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MessageService } from 'primeng/api';
import { InputTextModule } from 'primeng/inputtext';
import { TextareaModule } from 'primeng/textarea';
import { Select } from 'primeng/select';
import { MultiSelect } from 'primeng/multiselect';
import { ButtonModule } from 'primeng/button';
import { Chip } from 'primeng/chip';
import { Card } from 'primeng/card';
import { PageHeaderComponent } from '../../../shared/components/page-header/page-header.component';
import { ContentService } from '../services/content.service';
import { ContentStore } from '../store/content.store';
import { ContentType, PlatformType } from '../../../shared/models';

@Component({
  selector: 'app-content-form',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, InputTextModule, TextareaModule,
    Select, MultiSelect, ButtonModule, Chip, Card, PageHeaderComponent,
  ],
  template: `
    <app-page-header [title]="isEdit ? 'Edit Content' : 'New Content'" />
    <p-card>
      <form [formGroup]="form" (ngSubmit)="onSubmit()">
        <div class="grid">
          <div class="col-12 md:col-6">
            <label for="contentType">Content Type</label>
            <p-select
              id="contentType"
              formControlName="contentType"
              [options]="contentTypes"
              optionLabel="label"
              optionValue="value"
              placeholder="Select type"
              styleClass="w-full"
            />
          </div>
          <div class="col-12 md:col-6">
            <label for="targetPlatforms">Target Platforms</label>
            <p-multiselect
              id="targetPlatforms"
              formControlName="targetPlatforms"
              [options]="platforms"
              optionLabel="label"
              optionValue="value"
              placeholder="Select platforms"
              styleClass="w-full"
            />
          </div>
          <div class="col-12">
            <label for="title">Title</label>
            <input id="title" type="text" pInputText formControlName="title" class="w-full" />
          </div>
          <div class="col-12">
            <label for="body">Body</label>
            <textarea id="body" pTextarea formControlName="body" [rows]="10" class="w-full"></textarea>
          </div>
          <div class="col-12">
            <label for="tags">Tags (comma-separated)</label>
            <input id="tags" type="text" pInputText formControlName="tags" class="w-full" />
          </div>
          <div class="col-12 flex gap-2 justify-content-end">
            <p-button label="Cancel" severity="secondary" (onClick)="onCancel()" />
            <p-button label="Save" icon="pi pi-check" type="submit" [loading]="store.saving()" [disabled]="form.invalid" />
          </div>
        </div>
      </form>
    </p-card>
  `,
  styles: `
    label { display: block; margin-bottom: 0.5rem; font-weight: 600; margin-top: 1rem; }
    label:first-child { margin-top: 0; }
  `,
})
export class ContentFormComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly contentService = inject(ContentService);
  private readonly messageService = inject(MessageService);
  readonly store = inject(ContentStore);

  isEdit = false;
  private contentId?: string;

  readonly contentTypes = [
    { label: 'Blog Post', value: 'BlogPost' as ContentType },
    { label: 'Social Post', value: 'SocialPost' as ContentType },
    { label: 'Thread', value: 'Thread' as ContentType },
    { label: 'Video Description', value: 'VideoDescription' as ContentType },
  ];

  readonly platforms = [
    { label: 'Twitter/X', value: 'TwitterX' as PlatformType },
    { label: 'LinkedIn', value: 'LinkedIn' as PlatformType },
    { label: 'Instagram', value: 'Instagram' as PlatformType },
    { label: 'YouTube', value: 'YouTube' as PlatformType },
    { label: 'Reddit', value: 'Reddit' as PlatformType },
    { label: 'Substack', value: 'Substack' as PlatformType },
    { label: 'Personal Blog', value: 'PersonalBlog' as PlatformType },
  ];

  readonly form = this.fb.group({
    contentType: ['BlogPost' as ContentType, Validators.required],
    title: [''],
    body: ['', Validators.required],
    targetPlatforms: [[] as PlatformType[]],
    tags: [''],
  });

  ngOnInit() {
    this.contentId = this.route.snapshot.params['id'];
    this.isEdit = !!this.contentId;

    if (this.isEdit && this.contentId) {
      this.contentService.getById(this.contentId).subscribe(content => {
        this.form.patchValue({
          contentType: content.contentType,
          title: content.title ?? '',
          body: content.body,
          targetPlatforms: [...content.targetPlatforms],
          tags: content.metadata.tags.join(', '),
        });
      });
    }
  }

  onSubmit() {
    if (this.form.invalid) return;
    const val = this.form.getRawValue();
    const tags = val.tags ? val.tags.split(',').map(t => t.trim()).filter(Boolean) : [];

    this.store.setSaving(true);

    if (this.isEdit && this.contentId) {
      this.contentService.update({
        id: this.contentId,
        title: val.title || undefined,
        body: val.body || undefined,
        targetPlatforms: val.targetPlatforms ?? [],
        metadata: { tags, seoKeywords: [], platformSpecificData: {} },
        version: this.store.selectedContent()?.version ?? 0,
      }).subscribe({
        next: () => {
          this.store.setSaving(false);
          this.messageService.add({ severity: 'success', summary: 'Updated', detail: 'Content updated' });
          this.router.navigate(['/content', this.contentId]);
        },
        error: () => this.store.setSaving(false),
      });
    } else {
      this.contentService.create({
        contentType: val.contentType!,
        body: val.body!,
        title: val.title || undefined,
        targetPlatforms: val.targetPlatforms ?? [],
        metadata: { tags, seoKeywords: [], platformSpecificData: {} },
      }).subscribe({
        next: res => {
          this.store.setSaving(false);
          this.messageService.add({ severity: 'success', summary: 'Created', detail: 'Content created' });
          this.router.navigate(['/content', res.id]);
        },
        error: () => this.store.setSaving(false),
      });
    }
  }

  onCancel() {
    this.router.navigate(['/content']);
  }
}
