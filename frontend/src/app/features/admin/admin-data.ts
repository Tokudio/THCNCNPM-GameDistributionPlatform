import { HttpErrorResponse } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { ParamMap } from '@angular/router';
import { catchError, combineLatest, map, Observable, of, startWith, Subject, switchMap } from 'rxjs';

export type AdminLoad<T> = { kind: 'loading' } | { kind: 'ready'; data: T } | { kind: 'error'; message: string };

export function adminError(error: unknown, missing = 'Không tìm thấy dữ liệu.') {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 401) return 'Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.';
    if (error.status === 403) return 'Bạn không có quyền quản trị để thực hiện thao tác này.';
    if (error.status === 404) return missing;
    if (error.status === 409 && typeof error.error?.detail === 'string') return error.error.detail;
    if (error.status === 400) return 'Thông tin gửi lên không hợp lệ. Vui lòng kiểm tra lại.';
    if (error.status === 0) return 'Không kết nối được máy chủ. Vui lòng thử lại.';
  }
  return 'Không tải được dữ liệu. Vui lòng thử lại.';
}

export function loadAdminData<P, T>(parameters: Observable<P>, request: (params: P) => Observable<T>, missing?: string) {
  const refresh = new Subject<void>();
  const state = toSignal(combineLatest([parameters, refresh.pipe(startWith(undefined))]).pipe(
    // Khi đổi trang, bộ lọc hoặc ID, hủy request cũ để kết quả đến muộn không ghi đè dữ liệu mới.
    switchMap(([params]) => request(params).pipe(
      map(data => ({ kind: 'ready', data }) as const),
      catchError(error => of<AdminLoad<T>>({ kind: 'error', message: adminError(error, missing) })),
      startWith<AdminLoad<T>>({ kind: 'loading' }),
    )),
  ), { requireSync: true });
  return { state, reload: () => refresh.next() };
}

export function readAdminPage(params: ParamMap): number {
  const page = Number(params.get('page'));
  return Number.isInteger(page) && page >= 1 && page <= 1_000_000 ? page : 1;
}

export function adminSearch(value: string | null): string {
  return (value ?? '').trim().slice(0, 200);
}

export function adminOption(value: string | null, options: readonly string[]): string {
  return value !== null && options.includes(value) ? value : '';
}
