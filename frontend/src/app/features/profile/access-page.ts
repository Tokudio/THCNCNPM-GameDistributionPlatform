import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';

@Component({
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './access-page.html',
})
export class AccessPage {
  private readonly route = inject(ActivatedRoute);
  protected readonly granted = this.route.snapshot.data['granted'] === true;
  protected readonly query = toSignal(this.route.queryParamMap);
}
