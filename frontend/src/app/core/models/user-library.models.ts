import { GameStatus } from './game-store.models';

export interface LibraryGame {
  gameId: string;
  title: string;
  slug: string;
  coverImageUrl: string | null;
  status: GameStatus;
  acquiredAt: string;
  categories: string[];
  isOwned: true;
}

export interface LibraryGameDetail extends LibraryGame {
  bannerImageUrl: string | null;
  description: string;
  developer: string | null;
  publisher: string | null;
  releaseDate: string | null;
  minimumRequirements: string | null;
  recommendedRequirements: string | null;
}
