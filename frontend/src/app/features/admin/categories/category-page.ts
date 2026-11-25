import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize, Observable } from 'rxjs';
import { catalogError } from '../catalog-http';
import { Category } from '../catalog.models';
import { CategoryService } from '../category.service';

@Component({
  imports: [ReactiveFormsModule],
  templateUrl: './category-page.html',
  styleUrl: './category-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryPage {
  private readonly api = inject(CategoryService);
  private readonly destroy = inject(DestroyRef);
  private readonly nameInput = viewChild<ElementRef<HTMLInputElement>>('nameInput');
  protected readonly categories = signal<Category[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal('');
  protected readonly actionError = signal('');
  protected readonly notice = signal('');
  protected readonly editing = signal<Category | null>(null);
  protected readonly deleting = signal<Category | null>(null);

  protected readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(100)]],
    slug: ['', [Validators.required, Validators.maxLength(150), Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/)]],
    description: ['', Validators.maxLength(1000)],
    isActive: true,
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    if (this.loading()) return;
    this.loading.set(true);
    this.loadError.set('');
    this.api.list().pipe(takeUntilDestroyed(this.destroy), finalize(() => this.loading.set(false))).subscribe({
      next: categories => this.categories.set(categories),
      error: error => this.loadError.set(catalogError(error)),
    });
  }

  protected edit(category: Category | null, focus = true): void {
    this.editing.set(category);
    this.actionError.set('');
    this.deleting.set(null);
    this.form.reset({
      name: category?.name ?? '', slug: category?.slug ?? '', description: category?.description ?? '',
      isActive: category?.isActive ?? true,
    });
    if (category?.isDefault) this.form.controls.slug.disable();
    else this.form.controls.slug.enable();
    if (focus) this.nameInput()?.nativeElement.focus();
  }

  protected save(): void {
    if (this.form.invalid || this.busy()) return;
    const value = this.form.getRawValue();
    const input = { ...value, name: value.name.trim(), description: value.description.trim() || null };
    if (!input.name) {
      this.actionError.set('Enter a category name.');
      return;
    }
    const category = this.editing();
    this.write(category ? this.api.update(category.id, input) : this.api.create(input),
      category ? 'Category updated.' : 'Category created.');
  }

  protected confirmDelete(category: Category): void {
    this.actionError.set('');
    this.deleting.set(category);
  }

  protected remove(): void {
    const category = this.deleting();
    if (category && !this.busy()) this.write(this.api.delete(category.id), 'Category deleted.');
  }

  private write(request: Observable<unknown>, message: string): void {
    this.busy.set(true);
    this.actionError.set('');
    this.notice.set('');
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.busy.set(false))).subscribe({
      next: () => {
        this.edit(null, false);
        this.notice.set(message);
        this.load();
      },
      error: error => this.actionError.set(catalogError(error)),
    });
  }
}
