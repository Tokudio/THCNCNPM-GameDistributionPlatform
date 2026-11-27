import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn } from '@angular/forms';

export const emailErrors = {
  required: 'Enter your email address.',
  email: 'Enter a valid email address.',
};

export function rateLimitMessage(error: HttpErrorResponse): string {
  const retryAfter = error.headers.get('Retry-After');
  const seconds = Number(retryAfter);
  return Number.isSafeInteger(seconds) && seconds > 0
    ? `Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau ${seconds} giây.`
    : 'Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.';
}

export function fieldError(
  control: AbstractControl,
  messages: Record<string, string>,
): string | null {
  if (control.hasError('server')) {
    return control.getError('server') as string;
  }
  if (!control.touched) {
    return null;
  }
  const error = Object.keys(messages).find(key => control.hasError(key));
  return error ? messages[error] : null;
}

export function applyValidationProblem(
  form: FormGroup,
  error: HttpErrorResponse,
  messages: Record<string, string>,
): void {
  const errors: unknown = error.error?.errors;
  if (!errors || typeof errors !== 'object') {
    return;
  }

  for (const [key, details] of Object.entries(errors)) {
    if (!Array.isArray(details) || details.length === 0) {
      continue;
    }
    const property = key.split('.').at(-1)?.toLowerCase();
    const field = Object.keys(messages).find(name => name.toLowerCase() === property);
    if (field) {
      const control = form.get(field);
      control?.setErrors({ ...control.errors, server: messages[field] });
    }
  }
}

export const nonBlank: ValidatorFn = (control): ValidationErrors | null =>
  control.value.trim() ? null : { required: true };

export const identityPasswordHint = 'At least 8 characters, with uppercase, lowercase, a number and a symbol.';

export const identityPassword: ValidatorFn = (control): ValidationErrors | null => {
  const password = control.value as string;
  const categories = [/[A-Z]/, /[a-z]/, /[0-9]/, /[^a-zA-Z0-9]/];
  return !password || (password.length >= 8 && categories.every(category => category.test(password)))
    ? null
    : { identityPassword: true };
};

export const matchingPasswords: ValidatorFn = (control): ValidationErrors | null =>
  control.get('password')?.value === control.get('confirmPassword')?.value
    ? null
    : { passwordMismatch: true };
