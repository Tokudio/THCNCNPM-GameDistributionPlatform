import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, finalize, forkJoin, of, startWith, switchMap } from 'rxjs';
import { AdminGameService } from '../admin-game.service';
import { catalogError } from '../catalog-http';
import { Category, GameDetails, GameInput, GameMediaInput, GameStatus, MediaType, gameStatusLabels } from '../catalog.models';
import { CategoryService } from '../category.service';

const guidPattern = /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i;
const emptyGuid = '00000000-0000-0000-0000-000000000000';

const requiredText: ValidatorFn = control => control.value.trim() ? null : { required: true };

function normalizedWebUrl(value: string): string | null {
  if (!/^https?:\/\//i.test(value)) return null;
  try {
    const url = new URL(value);
    return url.hostname && !url.username && !url.password ? url.href : null;
  } catch {
    return null;
  }
}

const webUrl: ValidatorFn = control => {
  const value: string = control.value.trim();
  return !value || normalizedWebUrl(value) ? null : { webUrl: true };
};

const cents: ValidatorFn = control => {
  const value: number | null = control.value;
  if (value === null) return null;
  // At 2^46, adjacent JavaScript numbers are more than one cent apart.
  if (value >= 2 ** 46) return { pricePrecision: true };
  return /^\d+(?:\.\d{1,2})?$/.test(String(value)) ? null : { cents: true };
};

const validReleaseDate: ValidatorFn = control => {
  const value: string = control.value;
  if (!value) return null;
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value.startsWith('0000')) return { date: true };
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.getTime()) && date.toISOString().startsWith(value)
    ? null : { date: true };
};

const validCategories: ValidatorFn = control => {
  const ids: string[] = control.value.map((id: string) => id.toLowerCase());
  return new Set(ids).size === ids.length && ids.every(id => guidPattern.test(id) && id !== emptyGuid)
    ? null : { categoryIds: true };
};

const uniqueMedia: ValidatorFn = control => {
  const items: GameMediaInput[] = control.value;
  const seen = new Set<string>();
  const duplicates = new Set<number>();
  items.forEach((item, index) => {
    const url = normalizedWebUrl(item.mediaUrl.trim());
    if (!url) return;
    const key = `${item.type}:${url}`;
    if (seen.has(key)) duplicates.add(index);
    seen.add(key);
  });
  return duplicates.size ? { duplicateMedia: duplicates } : null;
};

function mediaForm(fb: FormBuilder, item?: GameMediaInput) {
  return fb.group({
    mediaUrl: fb.nonNullable.control(item?.mediaUrl ?? '', [requiredText, Validators.maxLength(500), webUrl]),
    type: fb.nonNullable.control(item?.type ?? MediaType.Image,
      control => control.value === MediaType.Image || control.value === MediaType.Video ? null : { mediaType: true }),
    displayOrder: fb.control(item?.displayOrder ?? 0,
      [Validators.required, Validators.min(0), Validators.max(2_147_483_647), Validators.pattern(/^\d+$/)]),
  });
}

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './game-form.html',
  styleUrl: './game-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameForm {
  private readonly api = inject(AdminGameService);
  private readonly categoryApi = inject(CategoryService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly fb = inject(FormBuilder);
  private readonly destroy = inject(DestroyRef);
  private readonly reload = new Subject<void>();

  protected readonly gameId = signal<string | null>(null);
  protected readonly game = signal<GameDetails | null>(null);
  protected readonly categories = signal<Category[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadError = signal('');
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly nextStatus = this.fb.control<GameStatus | null>(null);
  protected readonly confirmation = signal<GameStatus | null>(null);
  protected readonly status = GameStatus;
  protected readonly mediaType = MediaType;
  protected readonly statusLabels = gameStatusLabels;

  protected readonly form = this.fb.nonNullable.group({
    title: ['', [requiredText, Validators.maxLength(200)]],
    slug: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/)]],
    description: ['', requiredText],
    price: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), cents]),
    currency: ['VND', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
    discountPercent: this.fb.control<number | null>(0, [Validators.required, Validators.min(0), Validators.max(100), Validators.pattern(/^\d+$/)]),
    coverImageUrl: ['', [Validators.maxLength(500), webUrl]],
    bannerImageUrl: ['', [Validators.maxLength(500), webUrl]],
    trailerUrl: ['', [Validators.maxLength(500), webUrl]],
    publisher: ['', Validators.maxLength(100)],
    developer: ['', Validators.maxLength(100)],
    releaseDate: ['', validReleaseDate],
    minimumRequirements: '',
    recommendedRequirements: '',
    categoryIds: this.fb.nonNullable.control<string[]>([], [Validators.maxLength(100), validCategories]),
    media: this.fb.array<ReturnType<typeof mediaForm>>([], [Validators.maxLength(100), uniqueMedia]),
  });
  protected readonly media = this.form.controls.media;

  constructor() {
    combineLatest([this.route.paramMap, this.reload.pipe(startWith(undefined))]).pipe(
      switchMap(([params]) => {
        const id = params.get('id');
        this.loading.set(true);
        this.loadError.set('');
        this.error.set('');
        this.notice.set('');
        this.confirmation.set(null);
        this.gameId.set(id);
        return forkJoin({ game: id ? this.api.get(id) : of(null), categories: this.categoryApi.list() }).pipe(
          catchError(error => {
            this.loading.set(false);
            this.loadError.set(catalogError(error));
            return of(null);
          }),
        );
      }),
      takeUntilDestroyed(this.destroy),
    ).subscribe(data => {
      if (data) {
        this.categories.set(data.categories);
        this.loadForm(data.game);
        this.loading.set(false);
      }
    });
  }

  protected retry(): void {
    this.reload.next();
  }

  protected get priceError(): string {
    const price = this.form.controls.price;
    if (price.hasError('pricePrecision')) return 'This price is too large to preserve cent precision in the browser.';
    if (price.hasError('required')) return 'Price is required. Enter 0 for a free game.';
    return this.fieldError(price, 'Price', 'Enter a nonnegative price with up to two decimal places.');
  }

  protected get categoryError(): string {
    return this.fieldError(this.form.controls.categoryIds, 'Categories',
      'Select distinct, valid categories.', 'Select up to 100 categories.');
  }

  protected get mediaError(): string {
    const server = this.media.getError('server');
    if (typeof server === 'string') return server;
    return this.media.hasError('maxlength') ? 'Add up to 100 media items.' : '';
  }

  protected fieldError(control: AbstractControl, label: string, invalid = '', tooLong = ''): string {
    const errors = control.errors;
    if (!errors) return '';
    if (typeof errors['server'] === 'string') return errors['server'];
    if (errors['required']) return `${label} is required.`;
    if (errors['maxlength']) return tooLong || `${label} must be at most ${errors['maxlength'].requiredLength} characters.`;
    if (errors['webUrl']) return 'Enter an absolute HTTP or HTTPS URL without credentials.';
    return invalid;
  }

  protected mediaUrlError(item: ReturnType<typeof mediaForm>, index: number): string {
    const errors: Set<number> | null = this.media.getError('duplicateMedia');
    return this.fieldError(item.controls.mediaUrl, 'Media URL') ||
      (errors?.has(index) ? 'This URL is already listed for this media type.' : '');
  }

  protected toggleCategory(id: string, checked: boolean): void {
    const control = this.form.controls.categoryIds;
    if (control.value.includes(id) === checked) return;
    control.setValue(checked ? [...control.value, id] : control.value.filter(item => item !== id));
    control.markAsDirty();
  }

  protected addMedia(): void {
    if (this.media.length >= 100) return;
    this.media.push(mediaForm(this.fb));
    this.media.markAsDirty();
  }

  protected removeMedia(index: number): void {
    this.media.removeAt(index);
    this.media.markAsDirty();
  }

  protected save(): void {
    if (this.form.invalid || this.saving() || this.confirmation() !== null) return;
    this.error.set('');
    this.notice.set('');
    const value = this.form.getRawValue();
    const media = value.media.map(item => ({
      ...item, mediaUrl: item.mediaUrl.trim(), displayOrder: item.displayOrder!,
    }));

    const date = this.game()?.releaseDate;
    const input: GameInput = {
      ...value, price: value.price!, discountPercent: value.discountPercent!, media,
      title: value.title.trim(), description: value.description.trim(), currency: value.currency.toUpperCase(),
      coverImageUrl: value.coverImageUrl.trim() || null, bannerImageUrl: value.bannerImageUrl.trim() || null,
      trailerUrl: value.trailerUrl.trim() || null, publisher: value.publisher.trim() || null,
      developer: value.developer.trim() || null, minimumRequirements: value.minimumRequirements.trim() || null,
      recommendedRequirements: value.recommendedRequirements.trim() || null,
      releaseDate: value.releaseDate
        ? date?.slice(0, 10) === value.releaseDate ? date : `${value.releaseDate}T00:00:00`
        : null,
    };

    this.saving.set(true);
    const id = this.gameId();
    const request = id ? this.api.update(id, input) : this.api.create(input);
    request.pipe(takeUntilDestroyed(this.destroy), finalize(() => this.saving.set(false))).subscribe({
      next: game => {
        if (this.gameId() !== id) return;
        if (id) {
          this.loadForm(game);
          this.notice.set('Game updated.');
        } else {
          void this.router.navigate(['/admin/games', game.id, 'edit'], { replaceUrl: true });
        }
      },
      error: error => {
        if (this.gameId() === id) this.showSaveError(error);
      },
    });
  }

  private showSaveError(error: unknown): void {
    if (error instanceof HttpErrorResponse && (error.status === 400 || error.status === 409)) {
      const body = error.error as { errors?: Record<string, unknown>; detail?: unknown } | null;
      const unmapped: string[] = [];
      let mapped = false;
      for (const [key, messages] of Object.entries(body?.errors ?? {})) {
        if (!Array.isArray(messages)) continue;
        const detail = messages.filter(message => typeof message === 'string').join(' ');
        if (!detail) continue;
        const path = key.replace(/^(?:\$\.|request\.)/i, '').replace(/\[(\d+)\]/g, '.$1')
          .split('.').map(part => part.charAt(0).toLowerCase() + part.slice(1));
        const control = path[0] === 'categoryIds' ? this.form.controls.categoryIds : this.form.get(path);
        if (control && control !== this.form) {
          control.setErrors({ ...control.errors, server: detail });
          mapped = true;
        } else {
          unmapped.push(detail);
        }
      }

      if (error.status === 409 && body?.detail === 'This game slug is already in use.') {
        this.form.controls.slug.setErrors({ server: body.detail });
        mapped = true;
      }
      if (error.status === 400 && body?.detail === 'One or more selected categories do not exist.') {
        this.form.controls.categoryIds.setErrors({ server: body.detail });
        mapped = true;
      }
      if (mapped) {
        this.error.set(unmapped.join(' ') || 'Check the highlighted fields and try again.');
        return;
      }
    }
    if (error instanceof HttpErrorResponse && error.status >= 500) {
      this.error.set('The server could not save the game. Try again.');
      return;
    }
    this.error.set(catalogError(error));
  }

  protected requestStatusChange(): void {
    const next = this.nextStatus.value;
    if (!this.form.dirty && next !== null && next !== this.game()?.status) this.confirmation.set(next);
  }

  protected applyStatus(): void {
    const game = this.game();
    const next = this.confirmation();
    if (!game || next === null || this.saving() || this.form.dirty) return;
    this.confirmation.set(null);
    this.saving.set(true);
    this.error.set('');
    this.notice.set('');
    this.api.updateStatus(game.id, next).pipe(
      takeUntilDestroyed(this.destroy), finalize(() => this.saving.set(false)),
    ).subscribe({
      next: updated => {
        if (this.gameId() !== updated.id) return;
        this.game.set(updated);
        this.nextStatus.setValue(updated.status);
        this.notice.set(`Status updated to ${this.statusLabels[updated.status]}.`);
      },
      error: error => {
        if (this.gameId() === game.id) this.error.set(catalogError(error));
      },
    });
  }

  private loadForm(game: GameDetails | null): void {
    this.game.set(game);
    this.nextStatus.setValue(game?.status ?? null);
    this.media.clear();
    for (const item of game?.media ?? []) this.media.push(mediaForm(this.fb, item));
    this.form.reset({
      title: game?.title ?? '', slug: game?.slug ?? '', description: game?.description ?? '',
      price: game?.price ?? null, currency: game?.currency ?? 'VND', discountPercent: game?.discountPercent ?? 0,
      coverImageUrl: game?.coverImageUrl ?? '', bannerImageUrl: game?.bannerImageUrl ?? '', trailerUrl: game?.trailerUrl ?? '',
      publisher: game?.publisher ?? '', developer: game?.developer ?? '', releaseDate: game?.releaseDate?.slice(0, 10) ?? '',
      minimumRequirements: game?.minimumRequirements ?? '', recommendedRequirements: game?.recommendedRequirements ?? '',
      categoryIds: game?.categories.map(category => category.id) ?? [],
      media: game?.media ?? [],
    });
  }
}
