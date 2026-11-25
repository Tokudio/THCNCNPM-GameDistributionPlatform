import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { distinctUntilChanged, finalize, map } from 'rxjs';
import { AuthService } from '../../../../core/auth/auth.service';
import { AdminUserSummary } from '../../../../core/models/admin.models';
import { AdminService } from '../../../../core/services/admin.service';
import { adminError, loadAdminData } from '../../admin-data';
import { accountInitial, accountStatusLabel } from '../../admin-presentation';

@Component({
  imports: [DatePipe, RouterLink],
  templateUrl: './user-detail.html',
  styleUrl: './user-detail.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class UserDetail {
  private readonly api = inject(AdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  private readonly id = this.route.paramMap.pipe(map(params => params.get('id') ?? ''), distinctUntilChanged());
  protected readonly detail = loadAdminData(this.id, id => this.api.user(id), 'Không tìm thấy tài khoản.');
  protected readonly initial = accountInitial;
  protected readonly statusLabel = accountStatusLabel;
  protected readonly busy = signal(false);
  protected readonly feedback = signal<{ id: string; success: boolean; text: string } | null>(null);

  protected saveStatus(user: AdminUserSummary, dialog: HTMLDialogElement): void {
    if (this.busy() || this.auth.currentUser()?.id === user.id) return;
    dialog.close();
    this.busy.set(true);
    this.feedback.set(null);
    const routeId = this.route.snapshot.paramMap.get('id');
    const isActive = !user.isActive;
    // Chỉ gửi trạng thái mới. Backend xác minh quyền và bảo vệ Admin còn hoạt động.
    this.api.updateUserStatus(user.id, isActive).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.busy.set(false)),
    ).subscribe({
      next: () => {
        if (this.route.snapshot.paramMap.get('id') !== routeId) return;
        this.feedback.set({ id: user.id, success: true, text: isActive ? 'Đã kích hoạt tài khoản.' : 'Đã tạm khóa tài khoản.' });
        this.detail.reload();
      },
      error: error => this.feedback.set({ id: user.id, success: false, text: adminError(error, 'Không tìm thấy tài khoản.') }),
    });
  }
}
