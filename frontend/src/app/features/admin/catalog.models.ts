import { Category, GameSort, GameStatus, MediaType } from '../../core/models/game-store.models';

export { GameSort, GameStatus, MediaType };
export type { Category, GameDetails, GameSummary, Page } from '../../core/models/game-store.models';

export const gameStatusLabels: Record<GameStatus, string> = {
  [GameStatus.Draft]: 'Draft',
  [GameStatus.Published]: 'Published',
  [GameStatus.Archived]: 'Archived',
};

export type CategoryInput = Omit<Category, 'id' | 'isDefault'>;

export interface GameMediaInput {
  mediaUrl: string;
  type: MediaType;
  displayOrder: number;
}

export interface GameInput {
  title: string;
  slug: string;
  description: string;
  price: number;
  currency: string;
  discountPercent: number;
  coverImageUrl: string | null;
  bannerImageUrl: string | null;
  trailerUrl: string | null;
  publisher: string | null;
  developer: string | null;
  releaseDate: string | null;
  minimumRequirements: string | null;
  recommendedRequirements: string | null;
  categoryIds: string[];
  media: GameMediaInput[];
}

export interface GameFilters {
  search: string;
  categoryId: string;
  status: GameStatus | null;
  minPrice: number | null;
  maxPrice: number | null;
  sort: GameSort;
}
