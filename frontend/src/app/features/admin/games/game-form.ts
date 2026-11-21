import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidatorFn, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, catchError, combineLatest, finalize, forkJoin, of, startWith, switchMap } from 'rxjs';
import { AdminGameService } from '../admin-game.service';
import { catalogError } from '../catalog-http';
import { Category, GameDetails, GameInput, GameMediaInput, GameStatus, MediaType, gameStatusLabels } from '../catalog.models';
import { CategoryService } from '../category.service';

const webUrl: ValidatorFn = control => {
  const value: string = control.value.trim();
  if (!value) return null;
  try {
    const url = new URL(value);
    return /^https?:\/\//i.test(value) && url.hostname && !url.username && !url.password
      ? null : { webUrl: true };
  } catch {
    return { webUrl: true };
  }
};

const cents: ValidatorFn = control => {
  const value: number | null = control.value;
  if (value === null) return null;
  // At 2^46, adjacent JavaScript numbers are more than one cent apart.
  if (value >= 2 ** 46) return { pricePrecision: true };
  return /^\d+(?:\.\d{1,2})?$/.test(String(value)) ? null : { cents: true };
};

function mediaForm(fb: FormBuilder, item?: GameMediaInput) {
  return fb.group({
    mediaUrl: fb.nonNullable.control(item?.mediaUrl ?? '', [Validators.required, Validators.maxLength(500), webUrl]),
    type: fb.nonNullable.control(item?.type ?? MediaType.Image),
    displayOrder: fb.control(item?.displayOrder ?? 0, [Validators.required, Validators.min(0), Validators.max(2_147_483_647)]),
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
    title: ['', [Validators.required, Validators.maxLength(200)]],
    slug: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/^[a-z0-9]+(?:-[a-z0-9]+)*$/)]],
    description: ['', Validators.required],
    price: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), cents]),
    currency: ['VND', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
    discountPercent: this.fb.control<number | null>(0, [Validators.required, Validators.min(0), Validators.max(100), Validators.pattern(/^\d+$/)]),
    coverImageUrl: ['', [Validators.maxLength(500), webUrl]],
    bannerImageUrl: ['', [Validators.maxLength(500), webUrl]],
    trailerUrl: ['', [Validators.maxLength(500), webUrl]],
    publisher: ['', Validators.maxLength(100)],
    developer: ['', Validators.maxLength(100)],
    releaseDate: '',
    minimumRequirements: '',
    recommendedRequirements: '',
    categoryIds: this.fb.nonNullable.control<string[]>([], Validators.maxLength(100)),
    media: this.fb.array<ReturnType<typeof mediaForm>>([], Validators.maxLength(100)),
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
    return price.touched && price.invalid ? 'Enter a nonnegative price with up to two decimal places.' : '';
  }

  protected toggleCategory(id: string, checked: boolean): void {
    const control = this.form.controls.categoryIds;
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
    if (this.form.invalid || this.saving()) return;
    this.error.set('');
    this.notice.set('');
    const value = this.form.getRawValue();
    const media = value.media.map(item => ({
      ...item, mediaUrl: item.mediaUrl.trim(), displayOrder: item.displayOrder!,
    }));

    if (!value.title.trim() || !value.description.trim() || media.some(item => !item.mediaUrl || !Number.isInteger(item.displayOrder))) {
      this.error.set('Enter a title, a description and valid media details.');
      return;
    }
    if (new Set(media.map(item => `${item.type}:${new URL(item.mediaUrl).href}`)).size !== media.length) {
      this.error.set('Media URL and type pairs must be unique.');
      return;
    }

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
        if (this.gameId() === id) this.error.set(catalogError(error));
      },
    });
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
