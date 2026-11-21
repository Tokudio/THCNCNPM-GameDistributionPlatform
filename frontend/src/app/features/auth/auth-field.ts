import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'app-auth-field',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <label [for]="fieldId()">{{ label() }}</label>
    <div class="field-control">
      <input
        [id]="fieldId()"
        [type]="inputType()"
        [formControl]="control()"
        [autocomplete]="autocomplete()"
        [placeholder]="placeholder()"
        [attr.aria-invalid]="error() ? 'true' : null"
        [attr.aria-describedby]="error() || hint() ? fieldId() + '-message' : null"
        required
      />
      @if (type() === 'password') {
        <button
          type="button"
          class="password-toggle"
          [attr.aria-label]="(visible() ? 'Hide ' : 'Show ') + label().toLowerCase()"
          [attr.aria-controls]="fieldId()"
          (click)="visible.set(!visible())"
        >{{ visible() ? 'Hide' : 'Show' }}</button>
      }
    </div>
    @if (error() || hint()) {
      <p [id]="fieldId() + '-message'" class="field-message" [class.field-error]="error()">
        {{ error() || hint() }}
      </p>
    }
  `,
  styles: `
    :host { display: block; min-width: 0; }
    label { display: block; margin-bottom: .5rem; font-size: .875rem; font-weight: 650; }
    .field-control { position: relative; }
    input {
      width: 100%;
      min-height: 3.125rem;
      padding: .65rem .875rem;
      border: 1px solid var(--border);
      border-radius: .625rem;
      background: var(--input);
      color: var(--text);
    }
    input::placeholder { color: var(--muted); }
    input[aria-invalid="true"] { border-color: var(--danger); }
    input:has(+ .password-toggle) { padding-right: 4.25rem; }
    .password-toggle {
      position: absolute;
      inset: .375rem .375rem .375rem auto;
      min-width: 3.15rem;
      padding: 0 .4rem;
      background: transparent;
      color: var(--accent-text);
      border: 0;
      border-radius: .375rem;
      font-size: .875rem;
      font-weight: 650;
      cursor: pointer;
    }
    .password-toggle:hover { background: var(--raised); }
    .field-message { margin: .4rem 0 0; color: var(--muted); font-size: .8125rem; line-height: 1.4; }
    .field-error { color: var(--danger); }
  `,
})
export class AuthField {
  readonly fieldId = input.required<string>();
  readonly label = input.required<string>();
  readonly control = input.required<FormControl<string>>();
  readonly autocomplete = input.required<string>();
  readonly type = input<'text' | 'email' | 'password'>('text');
  readonly placeholder = input('');
  readonly hint = input('');
  readonly error = input<string | null>(null);

  protected readonly visible = signal(false);
  protected readonly inputType = computed(() =>
    this.type() === 'password' && this.visible() ? 'text' : this.type(),
  );
}
