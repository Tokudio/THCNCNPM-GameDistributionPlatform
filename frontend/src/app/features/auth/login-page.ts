import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { finalize } from 'rxjs';
import { postLoginUrl } from '../../core/auth/auth.guards';
import { AuthService } from '../../core/auth/auth.service';
import { AuthField } from './auth-field';
import { applyValidationProblem, emailErrors, fieldError, nonBlank } from './auth-form';

@Component({
  imports: [ReactiveFormsModule, AuthField],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
})
export class LoginPage {
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly registered = this.router.currentNavigation()?.extras.state?.['registered'] === true;
  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', nonBlank],
  });
  protected readonly submitting = signal(false);
  protected readonly feedback = signal<string | null>(null);

  protected get emailError(): string | null {
    return fieldError(this.form.controls.email, emailErrors);
  }

  protected get passwordError(): string | null {
    return fieldError(this.form.controls.password, { required: 'Enter your password.' });
  }

  protected submit(): void {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.feedback.set(null);
    this.submitting.set(true);
    const { email, password } = this.form.getRawValue();

    this.auth.login({ email: email.trim(), password }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.submitting.set(false)),
    ).subscribe({
      next: () => void this.router.navigateByUrl(
        postLoginUrl(this.router, this.route.snapshot.queryParamMap.get('returnUrl')),
      ),
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          this.feedback.set('The email address or password is incorrect.');
        } else if (error instanceof HttpErrorResponse && error.status === 400) {
          applyValidationProblem(this.form, error, {
            email: 'Check your email address.',
            password: 'Check your password.',
          });
          this.feedback.set('Check the fields and try again.');
        } else {
          this.feedback.set('We couldn’t sign you in. Please try again.');
        }
      },
    });
  }
}
