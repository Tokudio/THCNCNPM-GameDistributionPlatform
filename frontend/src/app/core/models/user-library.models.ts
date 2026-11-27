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

export interface LibraryDownloadInfo {
  gameId: string;
  isOwned: boolean;
  isDownloadAvailable: boolean;
  fileName: string | null;
  fileSize: number | null;
  message: string | null;
}
