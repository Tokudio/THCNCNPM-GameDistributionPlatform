import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LibraryGame } from '../../../../core/models/user-library.models';
import { GameArtwork } from '../../../store/components/game-artwork/game-artwork';
import { cardArtwork } from '../../../store/store-presentation';
import { acquiredTimestamp } from '../../library-date';

@Component({
  selector: 'app-library-game-card',
  imports: [DatePipe, RouterLink, GameArtwork],
  templateUrl: './library-game-card.html',
  styleUrl: './library-game-card.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LibraryGameCard {
  readonly game = input.required<LibraryGame>();
  readonly index = input(0);
  protected readonly artwork = cardArtwork;
  protected readonly acquiredTimestamp = acquiredTimestamp;
}
