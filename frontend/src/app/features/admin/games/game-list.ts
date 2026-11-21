import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subject, catchError, map, of, startWith, switchMap } from 'rxjs';
import { AdminGameService } from '../admin-game.service';
import { catalogError } from '../catalog-http';
import { GameFilters, GameSort, GameStatus, GameSummary, Page, gameStatusLabels } from '../catalog.models';
import { CategoryService } from '../category.service';

type ListState = { loading: true } | { loading: false; page: Page<GameSummary> } | { loading: false; error: string };

@Component({
  imports: [DecimalPipe, ReactiveFormsModule, RouterLink],
  templateUrl: './game-list.html',
  styleUrl: './game-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameList {
  private readonly api = inject(AdminGameService);
  private readonly fb = inject(FormBuilder);
  private readonly requests = new Subject<{ filters: GameFilters; page: number }>();
  protected readonly statuses = GameStatus;
  protected readonly statusLabels = gameStatusLabels;
  protected readonly sort = GameSort;
  protected readonly filters = this.fb.group({
    search: this.fb.nonNullable.control('', Validators.maxLength(200)),
    categoryId: this.fb.nonNullable.control(''),
    status: this.fb.control<GameStatus | null>(null),
    minPrice: this.fb.control<number | null>(null, Validators.min(0)),
    maxPrice: this.fb.control<number | null>(null, Validators.min(0)),
    sort: this.fb.nonNullable.control(GameSort.Newest),
  });
  private applied = this.filters.getRawValue();
  private currentPage = 1;
  protected readonly categoryOptions = toSignal(inject(CategoryService).list().pipe(
    map(categories => ({ categories, error: '' })),
    catchError(error => of({ categories: [], error: catalogError(error) })),
  ), { initialValue: { categories: [], error: '' } });
  protected readonly state = toSignal(this.requests.pipe(
    startWith({ filters: this.applied, page: 1 }),
    switchMap(({ filters, page }) => this.api.list(filters, page).pipe(
      map(value => ({ loading: false, page: value }) as ListState),
      catchError(error => of<ListState>({ loading: false, error: catalogError(error) })),
      startWith<ListState>({ loading: true }),
    )),
  ), { requireSync: true });

  protected get invalidPriceRange(): boolean {
    const { minPrice, maxPrice } = this.filters.getRawValue();
    return minPrice !== null && maxPrice !== null && minPrice > maxPrice;
  }

  protected apply(): void {
    if (this.filters.invalid || this.invalidPriceRange) return;
    const filters = this.filters.getRawValue();
    this.applied = { ...filters, search: filters.search.trim() };
    this.goTo(1);
  }

  protected goTo(page: number): void {
    this.currentPage = page;
    this.requests.next({ filters: this.applied, page });
  }

  protected retry(): void {
    this.goTo(this.currentPage);
  }
}
