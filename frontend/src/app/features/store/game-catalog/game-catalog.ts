import { DOCUMENT, NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, ParamMap, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, distinctUntilChanged, fromEvent, map, of, startWith, Subject, switchMap, tap } from 'rxjs';
import { Category, GameSort, Page, StoreCardGame, StoreQuery } from '../../../core/models/game-store.models';
import { GameStoreService } from '../../../core/services/game-store.service';
import { GameCard } from '../components/game-card/game-card';
import { minorUnits, storeError, toCards } from '../store-presentation';

type CatalogQuery = Required<Omit<StoreQuery, 'pageSize'>>;
type Result = { loading: true } | { loading: false; page: Page<StoreCardGame> } | { loading: false; error: string };
const pricePattern = /^\d{1,16}(?:\.\d{1,2})?$/;
const guidPattern = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i;
const defaults: CatalogQuery = { search: '', category: '', minPrice: '', maxPrice: '', sort: GameSort.Newest, page: 1 };

function readQuery(params: ParamMap): CatalogQuery {
  const category = params.get('category') ?? '';
  const page = Number(params.get('page'));
  const sort = Number(params.get('sort'));
  const minimum = params.get('minPrice') ?? '';
  const maximum = params.get('maxPrice') ?? '';
  const minPrice = pricePattern.test(minimum) ? minimum : '';
  const maxPrice = pricePattern.test(maximum) && (!minPrice || minorUnits(minPrice) <= minorUnits(maximum)) ? maximum : '';
  return {
    search: (params.get('search') ?? '').trim().slice(0, 200),
    category: guidPattern.test(category) ? category.toLowerCase() : '',
    minPrice, maxPrice,
    page: Number.isInteger(page) && page >= 1 && page <= 1_000_000 ? page : 1,
    sort: [GameSort.Newest, GameSort.Title, GameSort.PriceAsc, GameSort.PriceDesc].includes(sort) ? sort : GameSort.Newest,
  };
}

@Component({
  imports: [ReactiveFormsModule, RouterLink, NgTemplateOutlet, GameCard],
  templateUrl: './game-catalog.html',
  styleUrl: './game-catalog.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameCatalog {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(GameStoreService);
  private readonly refresh = new Subject<void>();
  private readonly reloadCategories = new Subject<void>();
  private readonly filterDialog = viewChild<ElementRef<HTMLDialogElement>>('filterDialog');
  private readonly media = inject(DOCUMENT).defaultView?.matchMedia?.('(max-width: 56rem)');
  protected readonly compact = toSignal(this.media
    ? fromEvent<MediaQueryListEvent>(this.media, 'change').pipe(map(event => event.matches))
    : of(false), { initialValue: this.media?.matches ?? false });
  protected readonly sort = GameSort;
  protected readonly applied = signal(defaults);
  protected readonly form = inject(FormBuilder).nonNullable.group({
    search: ['', Validators.maxLength(200)],
    category: '',
    minPrice: ['', Validators.pattern(pricePattern)],
    maxPrice: ['', Validators.pattern(pricePattern)],
    sort: GameSort.Newest,
  });
  protected readonly categoryState = toSignal(this.reloadCategories.pipe(
    startWith(undefined),
    switchMap(() => this.api.categories().pipe(
      map(items => ({ items, loading: false, error: '' })),
      catchError(error => of({ items: [] as Category[], loading: false, error: storeError(error) })),
      startWith({ items: [] as Category[], loading: true, error: '' }),
    )),
  ), { requireSync: true });
  protected readonly result = toSignal(combineLatest([
    this.route.queryParamMap.pipe(
      map(readQuery),
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      tap(query => {
        this.applied.set(query);
        this.form.reset(query, { emitEvent: false });
      }),
    ),
    this.refresh.pipe(startWith(undefined)),
  ]).pipe(
    switchMap(([query]) => this.api.list(query).pipe(
      switchMap(page => this.api.detailsFor(page.items).pipe(
        map(details => ({ loading: false, page: { ...page, items: toCards(page.items, details) } }) as const),
      )),
      catchError(error => of<Result>({ loading: false, error: storeError(error) })),
      startWith<Result>({ loading: true }),
    )),
  ), { requireSync: true });

  constructor() {
    effect(() => {
      if (this.compact()) return;
      const dialog = this.filterDialog()?.nativeElement;
      if (dialog?.open) dialog.close();
    });
  }

  protected get priceError(): string {
    const { minPrice, maxPrice } = this.form.getRawValue();
    if (this.form.controls.minPrice.invalid || this.form.controls.maxPrice.invalid) return 'Enter nonnegative prices with up to two decimal places.';
    return minPrice && maxPrice && minorUnits(minPrice) > minorUnits(maxPrice)
      ? 'Minimum price cannot exceed maximum price.' : '';
  }

  protected apply(dialog?: HTMLDialogElement): void {
    if (this.form.invalid || this.priceError) return;
    const filters = this.form.getRawValue();
    this.navigate({ ...filters, search: filters.search.trim(), page: 1 });
    if (dialog?.open) dialog.close();
  }

  protected search(): void {
    const control = this.form.controls.search;
    if (control.valid) this.navigate({ ...this.applied(), search: control.value.trim(), page: 1 });
  }

  protected changeSort(): void {
    this.navigate({ ...this.applied(), sort: this.form.controls.sort.value, page: 1 });
  }

  protected selectCategory(id: string): void {
    const query = { ...this.applied(), category: id, page: 1 };
    this.form.controls.category.setValue(id);
    this.navigate(query);
  }

  protected reset(dialog?: HTMLDialogElement): void {
    this.form.reset(defaults);
    this.navigate(defaults);
    if (dialog?.open) dialog.close();
  }

  protected goTo(page: number): void { this.navigate({ ...this.applied(), page }); }

  protected pageNumbers(page: Page<StoreCardGame>): number[] {
    const first = Math.max(1, Math.min(page.pageNumber - 1, page.totalPages - 2));
    return Array.from({ length: Math.min(3, page.totalPages) }, (_, index) => first + index);
  }

  protected retry(): void { this.refresh.next(); }
  protected retryCategories(): void { this.reloadCategories.next(); }

  private navigate(query: CatalogQuery): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        search: query.search || null, category: query.category || null,
        minPrice: query.minPrice || null, maxPrice: query.maxPrice || null,
        sort: query.sort || null, page: query.page > 1 ? query.page : null,
      },
    });
  }
}
