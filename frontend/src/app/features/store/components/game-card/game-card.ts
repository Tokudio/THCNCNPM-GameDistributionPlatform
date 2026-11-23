import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { StoreCardGame } from '../../../../core/models/game-store.models';
import { cardArtwork, gamePrice } from '../../store-presentation';
import { GameArtwork } from '../game-artwork/game-artwork';

@Component({
  selector: 'app-game-card',
  imports: [RouterLink, GameArtwork],
  templateUrl: './game-card.html',
  styleUrl: './game-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class GameCard {
  readonly game = input.required<StoreCardGame>();
  readonly index = input(0);
  readonly compact = input(false);
  protected readonly placeholder = computed(() => cardArtwork(this.index()));
  protected readonly price = computed(() => gamePrice(this.game()));
  protected readonly genres = computed(() => this.game().categories.slice(0, 2).map(category => category.name).join(' · '));
}
