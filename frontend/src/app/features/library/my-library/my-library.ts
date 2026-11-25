import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, combineLatest, debounceTime, distinctUntilChanged, filter, map, of, startWith, Subject, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { Page } from '../../../core/models/game-store.models';
import { LibraryGame } from '../../../core/models/user-library.models';
import { UserLibraryService } from '../../../core/services/user-library.service';
import { storeError } from '../../store/store-presentation';
import { LibraryGameCard } from '../components/library-game-card/library-game-card';

type Query = { search: string; page: number };
type Result = { kind: 'loading' } | { kind: 'ready'; data: Page<LibraryGame>; query: Query } | { kind: 'error'; message: string };

function normalizeSearch(value: string): string {
  return value.trim().slice(0, 200);
}

@Component({
  imports: [ReactiveFormsModule, RouterLink, LibraryGameCard],
  templateUrl: './my-library.html',
  styleUrl: './my-library.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MyLibrary {
  protected readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly library = inject(UserLibraryService);
  private readonly refresh = new Subject<void>();
  protected readonly search = new FormControl('', { nonNullable: true });
  private readonly query = this.route.queryParamMap.pipe(
    map(params => {
      const page = Number(params.get('page'));
      return { search: normalizeSearch(params.get('search') ?? ''),
        page: Number.isInteger(page) && page >= 1 && page <= 1_000_000 ? page : 1 };
    }),
    distinctUntilChanged((a, b) => a.search === b.search && a.page === b.page),
  );
  protected readonly result = toSignal(combineLatest([this.query, this.refresh.pipe(startWith(undefined))]).pipe(
    switchMap(([query]) => this.library.list(query.search, query.page).pipe(
      map(data => ({ kind: 'ready', data, query }) as const),
      catchError(error => of<Result>({ kind: 'error', message: storeError(error) })),
      startWith<Result>({ kind: 'loading' }),
    )),
  ), { requireSync: true });

  constructor() {
    this.query.pipe(
      switchMap(query => {
        this.search.setValue(query.search, { emitEvent: false });
        return this.search.valueChanges.pipe(map(normalizeSearch), debounceTime(250), filter(value => value !== query.search));
      }),
      takeUntilDestroyed(),
    ).subscribe(search => this.goTo(1, search));
  }

  protected changePage(page: number, query: Query): void {
    const search = normalizeSearch(this.search.value);
    this.goTo(search === query.search ? page : 1, search);
  }

  private goTo(page: number, search: string): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: { search: search || null, page: page > 1 ? page : null } });
  }

  protected clearSearch(): void {
    this.search.setValue('');
    this.goTo(1, '');
  }

  protected retry(): void { this.refresh.next(); }
}
