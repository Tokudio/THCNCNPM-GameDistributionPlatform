import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AuthField } from './auth-field';
import {
  applyValidationProblem, emailErrors, fieldError, identityPassword, identityPasswordHint,
  matchingPasswords, nonBlank,
} from './auth-form';

@Component({
  imports: [ReactiveFormsModule, AuthField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="auth-panel" aria-labelledby="page-title">
      <h1 id="page-title">Create your account</h1>
      @if (feedback()) {
        <p class="notice notice-error" role="alert">{{ feedback() }}</p>
      }
      <form class="auth-form" [formGroup]="form" (ngSubmit)="submit()" [attr.aria-busy]="submitting()" novalidate>
        <fieldset [disabled]="submitting()">
          <app-auth-field fieldId="displayName" label="Display name" autocomplete="nickname"
            placeholder="Your player name" [control]="form.controls.displayName" [error]="displayNameError" />
          <app-auth-field fieldId="email" label="Email address" type="email" autocomplete="email"
            placeholder="you@example.com" [control]="form.controls.email" [error]="emailError" />
          <app-auth-field fieldId="password" label="Password" type="password" autocomplete="new-password"
            placeholder="Create a password" [control]="form.controls.password" [error]="passwordError"
            [hint]="passwordHint" />
          <app-auth-field fieldId="confirmPassword" label="Confirm password" type="password" autocomplete="new-password"
            placeholder="Re-enter your password" [control]="form.controls.confirmPassword" [error]="confirmationError" />
          <button type="submit" class="button button-primary" [disabled]="form.invalid || submitting()">
            @if (submitting()) { <span class="spinner" aria-hidden="true"></span> Creating account… }
            @else { Create account }
          </button>
        </fieldset>
      </form>
    </section>
  `,
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [nonBlank, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, identityPassword]],
    confirmPassword: ['', Validators.required],
  }, { validators: matchingPasswords });
  protected readonly submitting = signal(false);
  protected readonly feedback = signal<string | null>(null);
  protected readonly passwordHint = identityPasswordHint;

  protected get displayNameError(): string | null {
    return fieldError(this.form.controls.displayName, {
      required: 'Enter a display name.',
      maxlength: 'Use 100 characters or fewer.',
    });
  }

  protected get emailError(): string | null {
    return fieldError(this.form.controls.email, emailErrors);
  }

  protected get passwordError(): string | null {
    return fieldError(this.form.controls.password, {
      required: 'Enter a password.',
      identityPassword: identityPasswordHint,
    });
  }

  protected get confirmationError(): string | null {
    const control = this.form.controls.confirmPassword;
    return fieldError(control, { required: 'Confirm your password.' }) ??
      (control.touched && this.form.hasError('passwordMismatch') ? 'Passwords must match.' : null);
  }

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.feedback.set(null);
    this.submitting.set(true);
    const { email, displayName, password, confirmPassword } = this.form.getRawValue();

    this.auth.register({ email: email.trim(), displayName: displayName.trim(), password, confirmPassword }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.submitting.set(false)),
    ).subscribe({
      next: () => void this.router.navigate(['/login'], {
        queryParams: { returnUrl: this.route.snapshot.queryParamMap.get('returnUrl') },
        state: { registered: true },
      }),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.form.controls.email.setErrors({ server: 'An account already uses this email address.' });
          this.feedback.set('Use a different email address or sign in to your account.');
        } else if (error instanceof HttpErrorResponse && error.status === 400) {
          applyValidationProblem(this.form, error, {
            displayName: 'Check your display name.',
            email: 'Check your email address.',
            password: identityPasswordHint,
            confirmPassword: 'Check that your passwords match.',
          });
          this.feedback.set('Check the fields and try again.');
        } else {
          this.feedback.set('We couldn’t create your account. Please try again.');
        }
      },
    });
  }
}
