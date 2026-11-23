import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { MediaType, StoreGameDetails } from '../../../../core/models/game-store.models';
import { publicWebUrl } from '../../store-presentation';
import { GameArtwork } from '../game-artwork/game-artwork';

@Component({
  selector: 'app-game-gallery',
  imports: [GameArtwork],
  templateUrl: './game-gallery.html',
  styleUrl: './game-gallery.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameGallery {
  readonly game = input.required<StoreGameDetails>();
  private readonly selection = signal<{ game: string; image: string } | null>(null);
  protected readonly images = computed(() => {
    const game = this.game();
    const media = game.media.filter(item => item.type === MediaType.Image)
      .sort((a, b) => a.displayOrder - b.displayOrder).map(item => publicWebUrl(item.mediaUrl));
    return [...new Set([publicWebUrl(game.bannerImageUrl), publicWebUrl(game.coverImageUrl), ...media]
      .filter((url): url is string => url !== null))];
  });
  protected readonly active = computed(() => {
    const selection = this.selection();
    const images = this.images();
    return selection?.game === this.game().id && images.includes(selection.image)
      ? selection.image : images[0] ?? null;
  });
  protected readonly bannerFallback = computed(() => {
    const { bannerImageUrl, coverImageUrl } = this.game();
    return this.active() === publicWebUrl(bannerImageUrl) ? coverImageUrl : null;
  });

  protected select(image: string): void {
    this.selection.set({ game: this.game().id, image });
  }
}
