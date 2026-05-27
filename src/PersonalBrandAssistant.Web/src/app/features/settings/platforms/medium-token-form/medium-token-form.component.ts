import { Component, inject, input, output } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import type { StoreCredentialsRequest } from '../../models/platform-connection.model';

@Component({
  selector: 'app-medium-token-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="token-form">
      <p class="helper-text">
        Find your token at Settings &gt; Security and apps &gt; Integration tokens on medium.com
      </p>
      <div class="field">
        <input
          type="password"
          formControlName="token"
          placeholder="Integration token"
          class="input"
        />
        @if (form.controls.token.touched && form.controls.token.errors) {
          <span class="error">
            @if (form.controls.token.errors['required']) { Token is required }
            @if (form.controls.token.errors['minlength']) { Token must be at least 10 characters }
          </span>
        }
      </div>
      <button type="submit" class="btn-save" [disabled]="form.invalid || loading()">
        {{ loading() ? 'Saving...' : 'Save Token' }}
      </button>
    </form>
  `,
  styles: [`
    .token-form { display: flex; flex-direction: column; gap: 12px; padding-top: 12px; }
    .helper-text { color: #8a8a96; font-size: 12px; margin: 0; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .input {
      background: #0e0e10; border: 1px solid #2c2c36; border-radius: 6px;
      padding: 8px 12px; color: #f0f0f5; font-size: 14px; font-family: inherit;
    }
    .input:focus { outline: none; border-color: #c87156; }
    .error { color: #f87171; font-size: 12px; }
    .btn-save {
      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
      padding: 8px 16px; font-size: 14px; cursor: pointer; align-self: flex-start;
      font-family: inherit;
    }
    .btn-save:hover:not(:disabled) { background: #d4836a; }
    .btn-save:disabled { opacity: 0.5; cursor: not-allowed; }
  `],
})
export class MediumTokenFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly loading = input(false);
  readonly submitted = output<StoreCredentialsRequest>();

  form = this.fb.group({
    token: ['', [Validators.required, Validators.minLength(10)]],
  });

  onSubmit(): void {
    if (this.form.valid) {
      this.submitted.emit({ token: this.form.value.token! });
      this.form.reset();
    }
  }
}
