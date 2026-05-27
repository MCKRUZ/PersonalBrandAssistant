import { Component, inject, input, output } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import type { StoreCredentialsRequest } from '../../models/platform-connection.model';

@Component({
  selector: 'app-substack-login-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  template: `
    <form [formGroup]="form" (ngSubmit)="onSubmit()" class="login-form">
      <p class="warning-text">
        Your password is used once to authenticate and is not stored. Only encrypted session cookies are saved.
      </p>
      <div class="field">
        <input
          type="email"
          formControlName="email"
          placeholder="Email"
          class="input"
        />
        @if (form.controls.email.touched && form.controls.email.errors) {
          <span class="error">
            @if (form.controls.email.errors['required']) { Email is required }
            @if (form.controls.email.errors['email']) { Enter a valid email }
          </span>
        }
      </div>
      <div class="field">
        <input
          type="password"
          formControlName="password"
          placeholder="Password"
          class="input"
        />
        @if (form.controls.password.touched && form.controls.password.errors) {
          <span class="error">Password is required</span>
        }
      </div>
      @if (errorMessage()) {
        <span class="error">{{ errorMessage() }}</span>
      }
      <button type="submit" class="btn-login" [disabled]="form.invalid || loading()">
        {{ loading() ? 'Logging in...' : 'Login' }}
      </button>
    </form>
  `,
  styles: [`
    .login-form { display: flex; flex-direction: column; gap: 12px; padding-top: 12px; }
    .warning-text { color: #8a8a96; font-size: 12px; margin: 0; font-style: italic; }
    .field { display: flex; flex-direction: column; gap: 4px; }
    .input {
      background: #0e0e10; border: 1px solid #2c2c36; border-radius: 6px;
      padding: 8px 12px; color: #f0f0f5; font-size: 14px; font-family: inherit;
    }
    .input:focus { outline: none; border-color: #c87156; }
    .error { color: #f87171; font-size: 12px; }
    .btn-login {
      background: #c87156; color: #f0f0f5; border: none; border-radius: 6px;
      padding: 8px 16px; font-size: 14px; cursor: pointer; align-self: flex-start;
      font-family: inherit;
    }
    .btn-login:hover:not(:disabled) { background: #d4836a; }
    .btn-login:disabled { opacity: 0.5; cursor: not-allowed; }
  `],
})
export class SubstackLoginFormComponent {
  private readonly fb = inject(FormBuilder);

  readonly loading = input(false);
  readonly errorMessage = input<string | null>(null);
  readonly submitted = output<StoreCredentialsRequest>();

  form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  onSubmit(): void {
    if (this.form.valid) {
      this.submitted.emit({
        email: this.form.value.email!,
        password: this.form.value.password!,
      });
      this.form.controls.password.reset();
    }
  }
}
