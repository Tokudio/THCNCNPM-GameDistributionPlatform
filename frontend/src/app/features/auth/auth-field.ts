import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

@Component({
  selector: 'app-auth-field',
  imports: [ReactiveFormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './auth-field.html',
  styleUrl: './auth-field.scss',
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
