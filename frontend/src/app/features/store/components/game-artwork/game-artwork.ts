import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { publicWebUrl, wideArtwork } from '../../store-presentation';

@Component({
  selector: 'app-game-artwork',
  templateUrl: './game-artwork.html',
  styleUrl: './game-artwork.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameArtwork {
  readonly source = input<string | null>(null);
  readonly alternate = input<string | null>(null);
  readonly alt = input.required<string>();
  readonly fallback = input(wideArtwork);
  readonly eager = input(false);
  private readonly failedUrls = signal<ReadonlySet<string>>(new Set());
  protected readonly image = computed(() => {
    const failed = this.failedUrls();
    const url = [publicWebUrl(this.source()), publicWebUrl(this.alternate())].find(candidate => candidate && !failed.has(candidate));
    if (url) return { url, fallback: false };
    return failed.has(this.fallback()) ? null : { url: this.fallback(), fallback: true };
  });

  protected failed(url: string): void {
    this.failedUrls.update(urls => new Set([...urls, url]));
  }
}
