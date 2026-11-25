import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Page } from '../../../core/models/game-store.models';

@Component({
  selector: 'app-admin-pager',
  templateUrl: './admin-pager.html',
  styleUrl: './admin-pager.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminPager {
  readonly page = input.required<Pick<Page<unknown>, 'pageNumber' | 'totalPages' | 'totalCount'>>();
  readonly pageChange = output<number>();
}
